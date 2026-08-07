param(
    [string]$OutputDirectory = (Join-Path $PSScriptRoot "artifacts\installer")
)

$ErrorActionPreference = "Stop"
$packageRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
$payload = Join-Path $packageRoot "payload"

New-Item -ItemType Directory -Force $packageRoot | Out-Null
New-Item -ItemType Directory -Force $payload | Out-Null

dotnet publish (Join-Path $PSScriptRoot "src\Unreal99\Unreal99.csproj") `
    -c Release -r win-x64 --self-contained false -p:DebugType=None -p:DebugSymbols=false -o $payload
if ($LASTEXITCODE -ne 0) { throw "遊戲發行失敗。" }

dotnet publish (Join-Path $PSScriptRoot "src\Unreal99.Installer\Unreal99.Installer.csproj") `
    -c Release -r win-x64 --self-contained false -p:DebugType=None -p:DebugSymbols=false -o $packageRoot
if ($LASTEXITCODE -ne 0) { throw "安裝程式發行失敗。" }

Write-Host "安裝套件已建立：$packageRoot"
