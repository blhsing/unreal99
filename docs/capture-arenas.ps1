# Regenerates docs/arenas/*.jpg — one representative in-game shot per arena.
#
# Most arenas are framed by the automatic fly-by orbit, which sizes itself from the spawn
# points. Several defeat it: a one-room donut, a 46m vertical shaft, three rooftops 40m
# apart, a long ship and an island in a lava sea cannot all be framed by one orbit rule, so
# those are aimed by hand with --flycam <radius> <height> <angleDeg> <lookAtHeight>. Vehicle
# arenas use --vehicleflycam <angleDeg>, which centres the densest parked group before anyone
# can drive away, so the actual vehicles remain legible in the gallery rather than tiny dots.
#
# Domination arenas are captured in Domination (--mode 5) rather than deathmatch, so the
# control points appear in their held colours instead of neutral grey — a DOM map photographed
# in deathmatch shows none of what makes it a DOM map. The same applies to Onslaught (--mode 6)
# and Assault (--mode 7): power nodes, objective markers and every vehicle on the map exist only
# while their own mode is running.
#
# Usage:  pwsh docs/capture-arenas.ps1
#         pwsh docs/capture-arenas.ps1 -Maps 21,22,23,24   # only the listed arenas
#         pwsh docs/capture-arenas.ps1 -ConvertOnly        # rebuild JPGs from validated PNGs
#
# -Maps re-shoots a subset and leaves every other docs/arenas/*.jpg untouched, which is what you
# want after adding or restyling a single arena — a full run is roughly twenty minutes. A major
# geometry or polygon-density pass requires omitting -Maps so every gallery image is refreshed.

param([string[]]$Maps, [switch]$ConvertOnly)

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $repo 'src\Unreal99\Unreal99.csproj'
$out  = Join-Path $repo 'docs\arenas'
$tmp  = Join-Path $env:TEMP 'unreal99-arena-shots'
New-Item -ItemType Directory -Force $out, $tmp | Out-Null

$names = @('morbias','stalwart','curse','grinder','codex','gothic','deck16','turbine','phobos',
           'peak','liandri','morpheus','hyperblast','coret','november','facingworlds','lavagiant',
           'leadworks','sesmar','olden','cinder',
           'ons-torlan','ons-primeval','ons-crossfire','ons-dria',
           'as-convoy','as-frigate','as-glacier',
           'war-torlan','war-torlan-necris','war-serenity','war-avalanche','war-onyxcoast',
           'war-islander',
           'br-anubis','br-colossus')
$last = $names.Count - 1

# Arenas the automatic orbit cannot frame, with the camera authored by hand.
$authored = @{
     3 = '30 14 45 3'      # Grinder      — above the arena rim, clear of the central platform underside
     8 = '32 8 72 4'        # Phobos       — inside one habitat, looking past its reactor through the connector
     9 = '42 24 0 7'        # Peak         — clear over the battlements, no foreground wall slab
    10 = '19 38 45 14'      # Liandri      — high in the shaft, looking down the glowing core
    11 = '78 64 90 42'      # Morpheus     — far enough out to hold all three rooftops
    12 = '40 24 30 6'       # HyperBlast   — along the ship's spine
    15 = '145 62 35 18'     # Facing Worlds— oblique wide shot holding both opposing towers
    16 = '58 36 45 4'       # Lava Giant   — above the island, both forts in frame
    # Heights here must stay under each arena's ceiling — 22 for Leadworks, 15 for Sesmar.
    # Overshooting puts the camera above the roof and photographs the ceiling from outside.
    17 = '40 11 200 2'      # Leadworks    — across a lead pool at the tower island
    # Dead on the north axis: off it, the carved rock between chamber and corridor fills the shot.
    18 = '30 6 270 2'       # Sesmar       — from a tomb chamber down the corridor to the hall
    # Aimed at colonnade height, not at the floor. Looking at y=0 from 12m up pointed the shot
    # down into open sand and wasted the bottom third of the frame on nothing.
    19 = '20 11 200 8'      # Olden        — across the moat, colonnade ringing it, shrine above
    20 = '38 20 200 3'      # Cinder       — the casting channel with the furnace beyond
    # The Onslaught and Assault arenas are far larger than anything above and the orbit rule,
    # which sizes itself from spawn points, ends up inside the geometry on all four.
    21 = '86 44 200 12'     # Torlan       — high enough to hold the tower and both node lines
    22 = '64 30 200 8'      # Primeval     — above the canopy, looking down the centre clearing
    23 = '96 46 200 10'     # Crossfire    — over the south shelf, centre node and both middles
    24 = '104 44 200 10'    # Dria         — down the frozen river, a support tower on each side
    25 = '92 34 160 10'     # Convoy       — along the column so several rigs stack in frame
    26 = '78 30 200 12'     # Frigate      — from over the dock, along the bridge onto the ship
    27 = '120 52 195 8'     # Glacier      — from over the lake, looking along the station
    # The Warfare arenas are the largest in the game; the orbit rule ends up inside the geometry.
    28 = '104 56 200 10'    # WAR-Torlan   — the delta, both primes and the centre bridge
    29 = '104 56 200 10'    # Torlan Necris— same framing, so the two rosters compare directly
    30 = '84 40 150 8'      # Serenity     — down the valley with the mine node in frame
    31 = '100 40 180 10'    # Avalanche    — from over the red base, straight down the mountain mouth
    32 = '78 34 210 8'      # Onyx Coast   — across the channel at the bridge node and the Necris base
    33 = '74 32 215 8'      # Islander     — fallback non-vehicle overview only
    # Both Bombing Run arenas are long and narrow, but shooting straight down the length hides
    # the one thing that makes them Bombing Run maps: the goal hoop. A three-quarter angle from
    # nearer one base puts a ring in frame with the midfield behind it.
    34 = '45 34 180 6'      # Anubis       — full arena and red goal, clear of foreground slabs
    35 = '48 36 180 7'      # Colossus     — symmetric lanes, ball and red goal all readable
}

# Domination arenas: shot in DOM so the control points show their held colours. The ONS and AS
# arenas need the same treatment for the same reason — nodes and objectives only exist in mode.
# Warfare (--mode 8) is the same again, and it is the only mode that shows the orbs at all.
$domFirst = 17
$onsFirst = 21
$asFirst  = 25
$warFirst = 28
# Bombing Run (--mode 9) is the only mode that spawns the ball at all.
$brFirst  = 34

# Every arena that actually spawns vehicles. Angles are authored per arena so the parked group
# reads against the map instead of being hidden by its own garage, tower or cliff.
$vehicleCameras = @{
    # value: angle, optionally followed by an explicit VehicleSpawn index
    21 = '215 0'; 22 = '210 0'; 23 = '0 0'; 24 = '210 0'
    25 = '160 0'; 27 = '90 0'
    # Avalanche is viewed across its blue vehicle cluster so the base shelter roof
    # cannot become a foreground slab that hides both the arena and its vehicles.
    28 = '215 0'; 29 = '0 3'; 30 = '90 0'; 31 = '90 4'; 32 = '210 0'; 33 = '0 0'
}

# Which arenas to shoot this run. Accept both PowerShell arrays and the comma-delimited string
# that `pwsh -File ... -Maps 15,21` supplies, so the documented examples behave identically.
$requestedMaps = @($Maps | ForEach-Object { $_ -split ',' } | ForEach-Object {
    $parsed = 0
    if ([int]::TryParse($_.Trim(), [ref]$parsed)) { $parsed }
})
$targets = if ($requestedMaps) { @($requestedMaps | Where-Object { $_ -ge 0 -and $_ -le $last } | Sort-Object -Unique) }
           else       { 0..$last }
if (-not $targets) { throw "No valid arena ids in -Maps (range is 0..$last)." }

if (-not $ConvertOnly) {
    dotnet build $proj -c Release -v q --nologo

    foreach ($i in $targets) {
        $png = Join-Path $tmp "map$i.png"
        # A second game instance exits successfully by design. Remove the old output first and
        # require a fresh non-empty file afterward, so the pipeline cannot silently publish a
        # stale image when another instance is still holding the single-instance mutex.
        if ([System.IO.File]::Exists($png)) { [System.IO.File]::Delete($png) }
        $vehicleShot = $vehicleCameras.ContainsKey($i)
        # Vehicle shots use an idle human and no bots: all production vehicles have spawned, but
        # none can leave its pad before the shutter. Other objective modes retain the demo bots
        # so their held objectives are photographed in team colours.
        $args = @('--windowed', '--nohud', '--players', '1', '--map', $i)
        if ($vehicleShot) { $args += @('--startmatch', '--nodemo', '--bots', '0') }
        else              { $args += @('--demo', '--bots', '6') }
        # Let the bots hold points for a while before the shutter, or every point is still neutral.
        if     ($i -ge $brFirst)  { $args += @('--mode', 9) }
        elseif ($i -ge $warFirst) { $args += @('--mode', 8) }
        elseif ($i -ge $asFirst)  { $args += @('--mode', 7) }
        elseif ($i -ge $onsFirst) { $args += @('--mode', 6) }
        elseif ($i -ge $domFirst) { $args += @('--mode', 5) }
        $frames = if ($vehicleShot) { 400 } elseif ($i -ge $domFirst) { 1500 } else { 400 }
        if ($vehicleShot)                  { $args += @('--vehicleflycam') + $vehicleCameras[$i].Split(' ') + @('--autoshot', $frames, $png) }
        elseif ($authored.ContainsKey($i)) { $args += @('--flycam') + $authored[$i].Split(' ') + @('--autoshot', $frames, $png) }
        else                               { $args += @('--flyby', '--autoshot', 460, $png) }
        & dotnet run --project $proj -c Release --no-build -- @args | Select-Object -Last 1
        if (-not [System.IO.File]::Exists($png) -or (Get-Item -LiteralPath $png).Length -lt 1024) {
            throw "Arena $i did not produce a fresh screenshot. Close any running game instance and retry."
        }
        Start-Sleep -Seconds 2
    }
}

# Downscale to something a repository should carry: 960px wide, JPEG quality 82 (~90KB each).
Add-Type -AssemblyName System.Drawing
$codec = [System.Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() | Where-Object { $_.MimeType -eq 'image/jpeg' }
$ep = New-Object System.Drawing.Imaging.EncoderParameters 1
$ep.Param[0] = New-Object System.Drawing.Imaging.EncoderParameter ([System.Drawing.Imaging.Encoder]::Quality), 82L
foreach ($i in $targets) {
    $sourcePng = Join-Path $tmp "map$i.png"
    if (-not [System.IO.File]::Exists($sourcePng) -or (Get-Item -LiteralPath $sourcePng).Length -lt 1024) {
        throw "Missing or empty lossless capture for arena ${i}: $sourcePng"
    }
    $img = [System.Drawing.Image]::FromFile($sourcePng)
    $w = 960; $h = [int]($img.Height * $w / $img.Width)
    $bmp = New-Object System.Drawing.Bitmap $w, $h
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.DrawImage($img, 0, 0, $w, $h)
    $bmp.Save((Join-Path $out ("{0:d2}-{1}.jpg" -f $i, $names[$i])), $codec, $ep)
    $g.Dispose(); $bmp.Dispose(); $img.Dispose()
}
Write-Host "wrote $($targets.Count) arena shots to $out"
