$ErrorActionPreference = "Stop"

function Copy-ModsToBuild {
    param(
        [string]$Source,
        [string]$Destination
    )

    if (Test-Path $Destination) {
        Remove-Item $Destination -Recurse -Force
    }

    if (-not (Test-Path $Source)) {
        New-Item -ItemType Directory -Force -Path $Destination | Out-Null
        return
    }

    Get-ChildItem -Path $Source -Recurse -Force -File |
        Where-Object { $_.Extension -ne ".meta" -and $_.Name -ne ".gitkeep" -and $_.Name -ne "steamPublishedFileId.id" } |
        ForEach-Object {
            $relativePath = $_.FullName.Substring($Source.Length).TrimStart("\", "/")
            $targetPath = Join-Path $Destination $relativePath
            $targetDir = Split-Path $targetPath -Parent
            if (-not (Test-Path $targetDir)) {
                New-Item -ItemType Directory -Force -Path $targetDir | Out-Null
            }
            Copy-Item -Path $_.FullName -Destination $targetPath -Force
        }
}

$root = Split-Path -Parent $PSScriptRoot
$repoRoot = Split-Path -Parent (Split-Path -Parent $root)
$project = Join-Path $root "app\app.csproj"
$output = Join-Path $repoRoot "build"
$modsSrc = Join-Path $root "Mods"
$modsDst = Join-Path $output "Mods"

New-Item -ItemType Directory -Force -Path $output | Out-Null

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -o $output

Copy-ModsToBuild -Source $modsSrc -Destination $modsDst

$steamAppId = Join-Path $output "steam_appid.txt"
if (Test-Path $steamAppId) {
    Remove-Item $steamAppId -Force
    Write-Host "Removed steam_appid.txt from build output."
}

$steamDll = Join-Path $output "steam_api64.dll"
if (-not (Test-Path $steamDll)) {
    $srcDll = Join-Path $root "third_party\Steamworks.NET\native\win-x64\steam_api64.dll"
    if (Test-Path $srcDll) {
        Copy-Item -Path $srcDll -Destination $steamDll -Force
        Write-Host "Copied steam_api64.dll to build output."
    } else {
        Write-Warning "steam_api64.dll not found in build or source."
    }
}

$publishedVersion = (Select-Xml -Path $project -XPath "/Project/PropertyGroup/Version").Node.InnerText
Write-Host "Published to: $output"
Write-Host "Version: $publishedVersion"
Write-Host "Mods copied to: $modsDst"
Write-Host "Mods files:" (Get-ChildItem $modsDst -Recurse -File -ErrorAction SilentlyContinue).Count
