# Regenerates docs/arenas/*.jpg — one representative in-game shot per arena.
#
# Most arenas are framed by the automatic fly-by orbit, which sizes itself from the spawn
# points. Several defeat it: a one-room donut, a 46m vertical shaft, three rooftops 40m
# apart, a long ship and an island in a lava sea cannot all be framed by one orbit rule, so
# those are aimed by hand with --flycam <radius> <height> <angleDeg> <lookAtHeight>.
#
# Domination arenas are captured in Domination (--mode 5) rather than deathmatch, so the
# control points appear in their held colours instead of neutral grey — a DOM map photographed
# in deathmatch shows none of what makes it a DOM map.
#
# Usage:  pwsh docs/capture-arenas.ps1

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'src\Unreal99\Unreal99.csproj'
$out  = Join-Path $repo 'docs\arenas'
$tmp  = Join-Path $env:TEMP 'unreal99-arena-shots'
New-Item -ItemType Directory -Force $out, $tmp | Out-Null

$names = @('morbias','stalwart','curse','grinder','codex','gothic','deck16','turbine','phobos',
           'peak','liandri','morpheus','hyperblast','coret','november','facingworlds','lavagiant',
           'leadworks','sesmar','olden','cinder')
$last = $names.Count - 1

# Arenas the automatic orbit cannot frame, with the camera authored by hand.
$authored = @{
     8 = '30 10 250 3'      # Phobos       — inside a habitat block, looking across it
    10 = '19 38 45 14'      # Liandri      — high in the shaft, looking down the glowing core
    11 = '78 64 90 42'      # Morpheus     — far enough out to hold all three rooftops
    12 = '40 24 30 6'       # HyperBlast   — along the ship's spine
    15 = '46 24 90 18'      # Facing Worlds— down the split bridge at a tower's three openings
    16 = '58 36 45 4'       # Lava Giant   — above the island, both forts in frame
    # Heights here must stay under each arena's ceiling — 22 for Leadworks, 15 for Sesmar.
    # Overshooting puts the camera above the roof and photographs the ceiling from outside.
    17 = '40 11 200 2'      # Leadworks    — across a lead pool at the tower island
    # Dead on the north axis: off it, the carved rock between chamber and corridor fills the shot.
    18 = '30 6 270 2'       # Sesmar       — from a tomb chamber down the corridor to the hall
    19 = '26 12 210 0'      # Olden        — across the moat at the spring island
    20 = '38 20 200 3'      # Cinder       — the casting channel with the furnace beyond
}

# Domination arenas: shot in DOM so the control points show their held colours.
$domFirst = 17

dotnet build $proj -c Release -v q --nologo

foreach ($i in 0..$last) {
    $png = Join-Path $tmp "map$i.png"
    $args = @('--windowed', '--nohud', '--demo', '--players', '1', '--bots', '6', '--map', $i)
    # Let the bots hold points for a while before the shutter, or every point is still neutral.
    if ($i -ge $domFirst) { $args += @('--mode', 5) }
    $frames = if ($i -ge $domFirst) { 1500 } else { 400 }
    if ($authored.ContainsKey($i)) { $args += @('--flycam') + $authored[$i].Split(' ') + @('--autoshot', $frames, $png) }
    else                          { $args += @('--flyby', '--autoshot', 460, $png) }
    & dotnet run --project $proj -c Release --no-build -- @args | Select-Object -Last 1
    Start-Sleep -Seconds 2
}

# Downscale to something a repository should carry: 960px wide, JPEG quality 82 (~90KB each).
Add-Type -AssemblyName System.Drawing
$codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
$ep = New-Object System.Drawing.Imaging.EncoderParameters 1
$ep.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), 82L
foreach ($i in 0..$last) {
    $img = [System.Drawing.Image]::FromFile((Join-Path $tmp "map$i.png"))
    $w = 960; $h = [int]($img.Height * $w / $img.Width)
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($img, 0, 0, $w, $h)
    $bmp.Save((Join-Path $out ("{0:d2}-{1}.jpg" -f $i, $names[$i])), $codec, $ep)
    $g.Dispose(); $bmp.Dispose(); $img.Dispose()
}
Write-Host "wrote $($names.Count) arena shots to $out"
