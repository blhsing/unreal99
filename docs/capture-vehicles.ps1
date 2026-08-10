param(
    [string]$Game = "",
    [string]$OutputDirectory = "",
    [string]$Python = "python",
    [switch]$NoBuild,
    [ValidateRange(0, 16)][int]$StartVehicle = 0,
    [ValidateRange(0, 16)][int]$EndVehicle = 16
)

$ErrorActionPreference = "Stop"
$repository = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Game)) {
    $Game = Join-Path $repository "src\Unreal99\bin\Release\net10.0\Unreal99.dll"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot "vehicles"
}
if ($StartVehicle -gt $EndVehicle) { throw "StartVehicle must not exceed EndVehicle." }

if (-not $NoBuild) {
    & dotnet build (Join-Path $repository "src\Unreal99\Unreal99.csproj") -c Release
    if ($LASTEXITCODE -ne 0) { throw "Release build failed with code $LASTEXITCODE" }
}

$gamePath = [IO.Path]::GetFullPath($Game)
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $gamePath -PathType Leaf)) { throw "Game not found: $gamePath" }
[IO.Directory]::CreateDirectory($outputPath) | Out-Null

$captureCommand = $gamePath
$capturePrefix = @()
if ([IO.Path]::GetExtension($gamePath) -eq ".dll") {
    $captureCommand = (Get-Command dotnet -ErrorAction Stop).Source
    $capturePrefix = @($gamePath)
}
$pythonCommand = (Get-Command $Python -ErrorAction Stop).Source
$webpBuilder = Join-Path $PSScriptRoot "build-weapon-webp.py"
$temporaryRoot = [IO.Path]::GetFullPath((Join-Path $outputPath ".capture"))
$safePrefix = $outputPath.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if (-not $temporaryRoot.StartsWith($safePrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Unsafe temporary capture directory: $temporaryRoot"
}

function Remove-CaptureDirectory([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return }
    for ($attempt = 1; $attempt -le 120; $attempt++) {
        try { Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop; return }
        catch {
            if ($attempt -eq 120) { throw }
            Start-Sleep -Milliseconds 500
        }
    }
}

function Invoke-GameCapture([object[]]$Arguments) {
    $startInfo = [Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = $captureCommand
    $startInfo.UseShellExecute = $false
    $startInfo.CreateNoWindow = $true
    foreach ($argument in ($capturePrefix + $Arguments)) { $startInfo.ArgumentList.Add([string]$argument) }
    $process = [Diagnostics.Process]::Start($startInfo)
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Vehicle capture exited with code $($process.ExitCode)" }
}

$slugs = @(
    "scorpion", "hellbender", "goliath", "leviathan", "paladin", "spma", "manta",
    "raptor", "cicada", "ion-tank", "viper", "scavenger", "nemesis", "nightshade",
    "fury", "darkwalker", "hoverboard"
)

if (Test-Path -LiteralPath $temporaryRoot) { Remove-CaptureDirectory $temporaryRoot }
[IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null
for ($vehicle = $StartVehicle; $vehicle -le $EndVehicle; $vehicle++) {
    $slug = $slugs[$vehicle]
    $frames = Join-Path $temporaryRoot $slug
    $destination = Join-Path $outputPath ($slug + "-turntable.webp")
    # Verify the frames before converting: a failed framebuffer read-back leaves zero-byte PNGs
    # behind, which is rare but clears on a retry.
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        Remove-CaptureDirectory $frames
        Invoke-GameCapture @("--vehicleturntable", $vehicle, $frames)
        $captured = @(Get-ChildItem -LiteralPath $frames -Filter "*.png" -ErrorAction SilentlyContinue)
        if ($captured.Count -eq 36 -and @($captured | Where-Object { $_.Length -eq 0 }).Count -eq 0) { break }
        if ($attempt -eq 3) { throw "Capture kept producing empty frames for $slug" }
        Write-Host "  capture produced empty frames; retrying ($attempt/3)"
    }
    & $pythonCommand $webpBuilder --input $frames --output $destination `
        --expected-frames 36 --quality 78 --alpha
    if ($LASTEXITCODE -ne 0) { throw "WebP conversion failed for $slug" }
    Write-Host "Captured $slug 360-degree turntable"
}
Remove-CaptureDirectory $temporaryRoot
