<#
.SYNOPSIS
Runs the mandatory deterministic bot-traversal validation suite for Unreal99 maps.

.DESCRIPTION
Builds the Release game unless -NoBuild is supplied, then runs a Godlike demo player against
Newbie opponents on every selected map. The command returns 0 only when every automated gate
passes and writes per-map logs/screenshots plus JSON and CSV summaries. Human review and the
required procedure for new maps are documented in docs/bot-traversal-validation.md.

.PARAMETER Frames
Active-play frames per map. The submission gate uses 3600 frames; the minimum is 600.

.PARAMETER MapIds
Map identifiers to test. The default must be expanded whenever a map is added.

.PARAMETER OutputDirectory
Directory for per-map screenshots/logs and aggregate JSON/CSV results.

.PARAMETER NoBuild
Skips the Release build. Use only for focused reruns against a known-current build.

.EXAMPLE
.\scripts\test-bot-traversal.ps1 -Frames 3600

.EXAMPLE
.\scripts\test-bot-traversal.ps1 -MapIds 7,11 -Frames 3600 -NoBuild
#>
param(
    [int]$Frames = 3600,
    [int[]]$MapIds = (0..24),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts\bot-traversal'),
    [switch]$NoBuild
)

$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$project = Join-Path $repoRoot 'src\Unreal99\Unreal99.csproj'
$game = Join-Path $repoRoot 'src\Unreal99\bin\Release\net10.0\Unreal99.dll'
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)

if ($Frames -lt 600) { throw 'Frames must be at least 600 (10 active-play seconds).' }
foreach ($mapId in $MapIds) {
    if ($mapId -lt 0 -or $mapId -gt 24) { throw "Map id is outside 0..24: $mapId" }
}

if (-not $NoBuild) {
    & dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw "Release build failed with exit code $LASTEXITCODE." }
}
if (-not (Test-Path -LiteralPath $game)) { throw "Game build not found: $game" }
[System.IO.Directory]::CreateDirectory($outputRoot) | Out-Null

$mapNames = @(
    'morbias', 'stalwart', 'curse', 'grinder', 'codex', 'gothic', 'deck16',
    'turbine', 'phobos', 'peak', 'liandri', 'morpheus', 'hyperblast',
    'coret', 'november', 'facing-worlds', 'lava-giant',
    'leadworks', 'sesmar', 'olden', 'cinder',
    'ons-torlan', 'ons-primeval', 'as-convoy', 'as-frigate'
)

$results = [System.Collections.Generic.List[object]]::new()
$previousBotDebug = $env:UNREAL99_BOT_DEBUG
$previousNavDebug = $env:UNREAL99_NAV_DEBUG
$env:UNREAL99_BOT_DEBUG = '1'
$env:UNREAL99_NAV_DEBUG = '1'

try {
    foreach ($mapId in $MapIds) {
        $name = $mapNames[$mapId]
        # Each arena is exercised in the ruleset it was authored for. Onslaught and Assault also
        # gate the vehicle and objective code paths, which nothing else reaches.
        $mode = if ($mapId -ge 23) { 7 }
                elseif ($mapId -ge 21) { 6 }
                elseif ($mapId -ge 17) { 5 }
                elseif ($mapId -ge 13) { 2 }
                else { 0 }
        $screenshot = Join-Path $outputRoot ('{0:D2}-{1}.png' -f $mapId, $name)
        $log = Join-Path $outputRoot ('{0:D2}-{1}.log' -f $mapId, $name)
        Write-Host ("[{0}/{1}] {2}: {3} active-play frames ({4:N1} s), mode {5}" -f `
            ($results.Count + 1), $MapIds.Count, $name, $Frames, ($Frames / 60.0), $mode)

        $arguments = @(
            $game,
            '--traversaltest', $Frames, $screenshot,
            '--map', $mapId,
            '--mode', $mode
        )
        # Some older Intel WGL drivers need a short recovery window after a native GLFW shutdown.
        # A driver reset can happen after a complete result was printed, then make the next process
        # report ApiUnavailable before game code runs. Re-run only those identifiable infrastructure
        # failures; ordinary non-zero behavioral results remain failures on their first attempt.
        $attempt = 0
        do {
            $attempt++
            if ($attempt -gt 1) { Start-Sleep -Seconds 3 }
            $output = @(& dotnet @arguments 2>&1 | Tee-Object -FilePath $log)
            $processExit = $LASTEXITCODE
            $resultLine = $output | Where-Object { "$_".StartsWith('TRAVERSAL_RESULT ') } |
                Select-Object -Last 1
            $joinedOutput = $output -join "`n"
            $wglUnavailable = -not $resultLine -and
                $joinedOutput -match 'ApiUnavailable: WGL|driver does not appear to support OpenGL'
            $nativeShutdownCrash = $resultLine -and $processExit -eq -1073740791
            if (($wglUnavailable -or $nativeShutdownCrash) -and $attempt -lt 3) {
                Write-Warning ("  graphics driver reset on attempt {0}; retrying after recovery" -f $attempt)
            }
        } while (($wglUnavailable -or $nativeShutdownCrash) -and $attempt -lt 3)
        if (-not $resultLine) {
            $result = [pscustomobject]@{
                MapId = $mapId
                Map = $name
                Mode = switch ($mode) {
                    2 { 'CaptureTheFlag' }
                    5 { 'Domination' }
                    6 { 'Onslaught' }
                    7 { 'Assault' }
                    default { 'Deathmatch' }
                }
                Passed = $false
                Failures = @('missing-result')
                ProcessExit = $processExit
                Screenshot = $screenshot
                Log = $log
            }
        }
        else {
            $result = $resultLine.Substring('TRAVERSAL_RESULT '.Length) | ConvertFrom-Json
            $result | Add-Member -NotePropertyName ProcessExit -NotePropertyValue $processExit
            $result | Add-Member -NotePropertyName Screenshot -NotePropertyValue $screenshot
            $result | Add-Member -NotePropertyName Log -NotePropertyValue $log
            if ($processExit -ne 0 -and $result.Passed) {
                $result.Passed = $false
                $result.Failures = @($result.Failures) + "process-exit=$processExit"
            }
        }
        $results.Add($result)
        $status = if ($result.Passed) { 'PASS' } else { 'FAIL' }
        Write-Host ("  {0}: travel={1}m cells={2} stall={3}s oscillation={4}s failures=[{5}]" -f `
            $status, $result.TravelMeters, $result.VisitedCells, $result.LongestStallSeconds,
            $result.LongestOscillationSeconds, (@($result.Failures) -join ', '))
        Start-Sleep -Milliseconds 750
    }
}
finally {
    $env:UNREAL99_BOT_DEBUG = $previousBotDebug
    $env:UNREAL99_NAV_DEBUG = $previousNavDebug
}

$jsonPath = Join-Path $outputRoot 'results.json'
$csvPath = Join-Path $outputRoot 'summary.csv'
[System.IO.File]::WriteAllText($jsonPath, ($results | ConvertTo-Json -Depth 8),
    [System.Text.UTF8Encoding]::new($false))
$results | Select-Object MapId, Map, Mode, Passed, ActiveSeconds, TravelMeters,
    RequiredTravelMeters, VisitedCells, RequiredCells, LongestStallSeconds,
    LongestOscillationSeconds, LongestSteepDownSeconds, OscillationEpisodes,
    WorstWindowPathMeters,
    WorstWindowNetMeters, WorstWindowExtentMeters, WorstWindowVerticalExtentMeters,
    WorstWindowReversals,
    WorstState, WorstGoalNode, WorstPathCursor, WorstPathCount, MainSkill,
    MaxOpponentSkill, WeaponPickupGoals, AmmoPickupGoals, VoidDeaths, FallDeaths, LavaDeaths,
    ControlPointsCaptured, ControlPointCount, ControlPointCaptures, DominationScoreRed,
    DominationScoreBlue,
    @{ Name = 'Failures'; Expression = { @($_.Failures) -join ';' } }, Screenshot, Log |
    Export-Csv -LiteralPath $csvPath -NoTypeInformation -Encoding utf8

$failed = @($results | Where-Object { -not $_.Passed })
Write-Host ''
Write-Host ("Traversal suite: {0} passed, {1} failed. Results: {2}" -f `
    ($results.Count - $failed.Count), $failed.Count, $jsonPath)
if ($failed.Count -gt 0) {
    Write-Host ('Failed maps: ' + (($failed | ForEach-Object { "#$($_.MapId) $($_.Map)" }) -join ', '))
    exit 2
}
exit 0
