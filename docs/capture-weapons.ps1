param(
    [string]$Game = "",
    [string]$OutputDirectory = "",
    [string]$Python = "python",
    [switch]$NoBuild,
    [switch]$SkipActionFootage,
    [Alias("SkipProfiles")][switch]$SkipTurntables,
    [ValidateRange(0, 10)][int]$StartWeapon = 0,
    [ValidateRange(0, 10)][int]$EndWeapon = 10
)

$ErrorActionPreference = "Stop"

$repository = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Game)) {
    $Game = Join-Path $repository "src\Unreal99\bin\Release\net10.0\Unreal99.dll"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot "weapons"
}

$project = Join-Path $repository "src\Unreal99\Unreal99.csproj"
if (-not $NoBuild) {
    & dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { throw "Release build failed with code $LASTEXITCODE" }
}

$gamePath = [IO.Path]::GetFullPath($Game)
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $gamePath -PathType Leaf)) {
    throw "Game executable not found: $gamePath"
}
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

# Prefer the managed entry point. It can capture alongside an installed Unreal99.exe process
# without Windows treating both app hosts as the same GUI program.
$captureCommand = $gamePath
$capturePrefix = @()
if ([IO.Path]::GetExtension($gamePath) -eq ".dll") {
    $captureCommand = (Get-Command dotnet -ErrorAction Stop).Source
    $capturePrefix = @($gamePath)
}
else {
    $managedGame = [IO.Path]::ChangeExtension($gamePath, ".dll")
    if (Test-Path -LiteralPath $managedGame -PathType Leaf) {
        $captureCommand = (Get-Command dotnet -ErrorAction Stop).Source
        $capturePrefix = @($managedGame)
    }
}

$pythonCommand = (Get-Command $Python -ErrorAction Stop).Source
$webpBuilder = Join-Path $PSScriptRoot "build-weapon-webp.py"
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $outputPath ".capture"))
$outputPrefix = $outputPath.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $temporaryRoot.StartsWith($outputPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe temporary capture directory: $temporaryRoot"
}

function Remove-CaptureDirectory {
    param([string]$Path, [switch]$NonRecursive)

    if (-not (Test-Path -LiteralPath $Path)) { return }
    # Antivirus/indexing can retain a just-read PNG for several seconds after Pillow exits.
    # Cleanup is deliberately deferred until the full batch is converted and then retried for
    # up to one minute so a transient scanner handle cannot abort otherwise valid footage.
    for ($attempt = 1; $attempt -le 120; $attempt++) {
        try {
            if ($NonRecursive) { Remove-Item -LiteralPath $Path -Force -ErrorAction Stop }
            else { Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop }
            return
        }
        catch {
            if ($attempt -eq 120) { throw }
            Start-Sleep -Milliseconds 500
        }
    }
}

if (Test-Path -LiteralPath $temporaryRoot) {
    Remove-CaptureDirectory $temporaryRoot
}
New-Item -ItemType Directory -Force -Path $temporaryRoot | Out-Null

function Invoke-GameCapture {
    param([object[]]$Arguments)

    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $captureCommand
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in ($capturePrefix + $Arguments)) {
        $startInfo.ArgumentList.Add([string]$argument)
    }
    $process = [Diagnostics.Process]::Start($startInfo)
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) {
        throw "Game capture exited with code $($process.ExitCode)"
    }
}

$slugs = @(
    "impact-hammer", "enforcer", "bio-rifle", "shock-rifle", "pulse-gun", "ripper",
    "minigun", "flak-cannon", "rocket-launcher", "sniper-rifle", "redeemer"
)
if ($StartWeapon -gt $EndWeapon) { throw "StartWeapon must not exceed EndWeapon." }
if ($SkipActionFootage -and $SkipTurntables) {
    throw "SkipActionFootage and SkipTurntables cannot both be selected."
}
for ($weapon = $StartWeapon; $weapon -le $EndWeapon; $weapon++) {
    $slug = $slugs[$weapon]
    $weaponFrameRoot = Join-Path $temporaryRoot $slug
    if (-not $SkipActionFootage) {
        Invoke-GameCapture @("--weaponfootage", $weapon, "both", $weaponFrameRoot)
        foreach ($mode in @("primary", "secondary")) {
            $frameDirectory = Join-Path $weaponFrameRoot $mode
            $destination = Join-Path $outputPath ($slug + "-" + $mode + ".webp")
            & $pythonCommand $webpBuilder --input $frameDirectory --output $destination
            if ($LASTEXITCODE -ne 0) { throw "WebP conversion failed for $slug $mode" }
        }
    }
    if (-not $SkipTurntables) {
        $turntableFrames = Join-Path $weaponFrameRoot "turntable"
        $turntableDestination = Join-Path $outputPath ($slug + "-turntable.webp")
        Invoke-GameCapture @("--weaponturntable", $weapon, $turntableFrames)
        & $pythonCommand $webpBuilder --input $turntableFrames --output $turntableDestination `
            --expected-frames 36 --quality 78 --alpha
        if ($LASTEXITCODE -ne 0) { throw "Turntable WebP conversion failed for $slug" }
    }

    $captureSummary = if ($SkipActionFootage) { "360-degree turntable" }
        elseif ($SkipTurntables) { "primary/secondary action footage" }
        else { "primary/secondary action footage and 360-degree turntable" }
    Write-Host "Captured $slug $captureSummary"
}

Remove-CaptureDirectory $temporaryRoot
