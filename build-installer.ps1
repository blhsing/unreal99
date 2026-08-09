param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "artifacts\installer"),
    [string]$ReleaseDirectory = (Join-Path $PSScriptRoot "artifacts\release"),
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"
$packageRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$releaseRoot = [System.IO.Path]::GetFullPath($ReleaseDirectory)
$payload = Join-Path $packageRoot "payload"

New-Item -ItemType Directory -Force $packageRoot | Out-Null
New-Item -ItemType Directory -Force $payload | Out-Null
New-Item -ItemType Directory -Force $releaseRoot | Out-Null

dotnet publish (Join-Path $PSScriptRoot "src\Unreal99\Unreal99.csproj") `
    -c Release -r win-x64 --self-contained false -p:Version=$Version `
    -p:DebugType=None -p:DebugSymbols=false -o $payload
if ($LASTEXITCODE -ne 0) { throw "遊戲發行失敗。" }

dotnet publish (Join-Path $PSScriptRoot "src\Unreal99.Installer\Unreal99.Installer.csproj") `
    -c Release -r win-x64 --self-contained false -p:Version=$Version `
    -p:DebugType=None -p:DebugSymbols=false -o $packageRoot
if ($LASTEXITCODE -ne 0) { throw "安裝程式發行失敗。" }

$publishedVersion = (Get-Item -LiteralPath (Join-Path $payload "Unreal99.exe")).VersionInfo.ProductVersion
if (-not $publishedVersion.StartsWith($Version, [StringComparison]::Ordinal)) {
    throw "發行版本不符：預期 $Version，實際 $publishedVersion"
}

$portableArchive = Join-Path $releaseRoot "Unreal99-$Version-win-x64.zip"
$installerArchive = Join-Path $releaseRoot "Unreal99-$Version-Setup-win-x64.zip"
Compress-Archive -Path (Join-Path $payload "*") -DestinationPath $portableArchive `
    -CompressionLevel Optimal -Force
Compress-Archive -Path (Join-Path $packageRoot "*") -DestinationPath $installerArchive `
    -CompressionLevel Optimal -Force

$checksums = @($portableArchive, $installerArchive) | ForEach-Object {
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $_
    "$($hash.Hash.ToLowerInvariant())  $([IO.Path]::GetFileName($_))"
}
$checksums | Set-Content -LiteralPath (Join-Path $releaseRoot "SHA256SUMS.txt") -Encoding utf8NoBOM

Write-Host "安裝套件已建立：$packageRoot"
Write-Host "1.0.0 發行下載已建立：$releaseRoot"
