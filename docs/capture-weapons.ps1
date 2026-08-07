param(
    [string]$Game = "",
    [string]$OutputDirectory = ""
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$repository = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($Game)) {
    $Game = Join-Path $repository "artifacts\game-current\Unreal99.exe"
}
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $PSScriptRoot "weapons"
}

$gamePath = [IO.Path]::GetFullPath($Game)
$outputPath = [IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $gamePath -PathType Leaf)) {
    throw "Game executable not found: $gamePath"
}
New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

# Prefer the managed entry point when it accompanies the executable. It can capture alongside an
# installed Unreal99.exe process without Windows treating both app hosts as the same GUI program.
$captureCommand = $gamePath
$capturePrefix = @()
$managedGame = [IO.Path]::ChangeExtension($gamePath, ".dll")
if (Test-Path -LiteralPath $managedGame -PathType Leaf) {
    $captureCommand = (Get-Command dotnet -ErrorAction Stop).Source
    $capturePrefix = @($managedGame)
}

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

for ($weapon = 0; $weapon -lt $slugs.Count; $weapon++) {
    $slug = $slugs[$weapon]
    $temporary = Join-Path $outputPath ($slug + ".capture.png")
    $destination = Join-Path $outputPath ($slug + ".jpg")
    $arguments = @(
        "--windowed", "--startmatch", "--players", "1", "--bots", "0", "--map", "16",
        "--weaponshot", $weapon, "--autoshot", "150", $temporary
    )
    Invoke-GameCapture $arguments
    Save-CroppedCapture $temporary $destination ([Drawing.Rectangle]::new(800, 450, 800, 450))

    $profileTemporary = Join-Path $outputPath ($slug + "-profile.capture.png")
    $profileDestination = Join-Path $outputPath ($slug + "-profile.jpg")
    $profileArguments = @(
        "--weaponprofile", $weapon, "--autoshot", "12", $profileTemporary
    )
    Invoke-GameCapture $profileArguments
    Save-CroppedCapture $profileTemporary $profileDestination ([Drawing.Rectangle]::new(500, 280, 1000, 562))

    Write-Host "Captured $slug first-person and upright profile views"
}
