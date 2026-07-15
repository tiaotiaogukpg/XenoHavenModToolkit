$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "app\app.csproj"
$output = Join-Path $root "dist\win-x64"

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $output

$modsDir = Join-Path $output "Mods"
if (-not (Test-Path $modsDir)) {
    New-Item -ItemType Directory -Path $modsDir | Out-Null
}

Write-Host "Published to: $output"
Write-Host "Mods directory: $modsDir"
