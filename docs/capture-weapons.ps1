param(
    [string]$Game = "",
    [string]$OutputDirectory = "",
    [string]$Python = "python",
    [switch]$NoBuild,
    [switch]$SkipProfiles,
    [ValidateRange(0, 10)][int]$StartWeapon = 0,
    [ValidateRange(0, 10)][int]$EndWeapon = 10
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

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
$jpeg = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
    Where-Object MimeType -eq "image/jpeg"
$jpegParameters = New-Object Drawing.Imaging.EncoderParameters 1
$jpegParameters.Param[0] = New-Object Drawing.Imaging.EncoderParameter(
    [Drawing.Imaging.Encoder]::Quality, 88L)

function Save-CroppedCapture {
    param(
        [string]$Temporary,
        [string]$Destination,
        [Drawing.Rectangle]$SourceRectangle
    )

    $source = [Drawing.Bitmap]::FromFile($Temporary)
    try {
        $cropped = New-Object Drawing.Bitmap 800, 450
        try {
            $graphics = [Drawing.Graphics]::FromImage($cropped)
            try {
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage(
                    $source,
                    [Drawing.Rectangle]::new(0, 0, 800, 450),
                    $SourceRectangle,
                    [Drawing.GraphicsUnit]::Pixel)
            }
            finally { $graphics.Dispose() }
            $cropped.Save($Destination, $jpeg, $jpegParameters)
        }
        finally { $cropped.Dispose() }
    }
    finally {
        $source.Dispose()
        Remove-Item -LiteralPath $Temporary
    }
}

if ($StartWeapon -gt $EndWeapon) { throw "StartWeapon must not exceed EndWeapon." }
for ($weapon = $StartWeapon; $weapon -le $EndWeapon; $weapon++) {
    $slug = $slugs[$weapon]
    $weaponFrameRoot = Join-Path $temporaryRoot $slug
    Invoke-GameCapture @("--weaponfootage", $weapon, "both", $weaponFrameRoot)
    foreach ($mode in @("primary", "secondary")) {
        $frameDirectory = Join-Path $weaponFrameRoot $mode
        $destination = Join-Path $outputPath ($slug + "-" + $mode + ".webp")
        & $pythonCommand $webpBuilder --input $frameDirectory --output $destination
        if ($LASTEXITCODE -ne 0) { throw "WebP conversion failed for $slug $mode" }
    }
    if (-not $SkipProfiles) {
        $profileTemporary = Join-Path $temporaryRoot ($slug + "-profile.capture.png")
        $profileDestination = Join-Path $outputPath ($slug + "-profile.jpg")
        $profileArguments = @(
            "--weaponprofile", $weapon, "--autoshot", "12", $profileTemporary
        )
        Invoke-GameCapture $profileArguments
        Save-CroppedCapture $profileTemporary $profileDestination ([Drawing.Rectangle]::new(500, 280, 1000, 562))
    }

    $captureSummary = if ($SkipProfiles) { "action footage" } else { "action footage and upright profile" }
    Write-Host "Captured $slug primary/secondary $captureSummary"
}

Remove-CaptureDirectory $temporaryRoot
