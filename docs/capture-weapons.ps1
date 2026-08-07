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

$slugs = @(
    "impact-hammer", "enforcer", "bio-rifle", "shock-rifle", "pulse-gun", "ripper",
    "minigun", "flak-cannon", "rocket-launcher", "sniper-rifle", "redeemer"
)
$jpeg = [Drawing.Imaging.ImageCodecInfo]::GetImageEncoders() |
    Where-Object MimeType -eq "image/jpeg"
$jpegParameters = New-Object Drawing.Imaging.EncoderParameters 1
$jpegParameters.Param[0] = New-Object Drawing.Imaging.EncoderParameter(
    [Drawing.Imaging.Encoder]::Quality, 88L)

for ($weapon = 0; $weapon -lt $slugs.Count; $weapon++) {
    $temporary = Join-Path $outputPath ($slugs[$weapon] + ".capture.png")
    $destination = Join-Path $outputPath ($slugs[$weapon] + ".jpg")
    $arguments = @(
        "--windowed", "--startmatch", "--players", "1", "--bots", "0", "--map", "16",
        "--weaponshot", $weapon, "--autoshot", "150", $temporary
    )
    Start-Process -FilePath $gamePath -ArgumentList $arguments -WindowStyle Hidden -Wait

    $source = [Drawing.Bitmap]::FromFile($temporary)
    try {
        $cropped = New-Object Drawing.Bitmap 800, 450
        try {
            $graphics = [Drawing.Graphics]::FromImage($cropped)
            try {
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage(
                    $source,
                    [Drawing.Rectangle]::new(0, 0, 800, 450),
                    [Drawing.Rectangle]::new(800, 450, 800, 450),
                    [Drawing.GraphicsUnit]::Pixel)
            }
            finally { $graphics.Dispose() }
            $cropped.Save($destination, $jpeg, $jpegParameters)
        }
        finally { $cropped.Dispose() }
    }
    finally {
        $source.Dispose()
        Remove-Item -LiteralPath $temporary
    }
    Write-Host "Captured $($slugs[$weapon])"
}
