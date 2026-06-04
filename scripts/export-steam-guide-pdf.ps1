# 将 docs/SteamModDevGuide.md 导出为 PDF（需已安装 Python、markdown、playwright 及 Chromium）
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$md = Join-Path $root "docs\SteamModDevGuide.md"
$pdf = Join-Path $root "docs\SteamModDevGuide.pdf"

python (Join-Path $PSScriptRoot "md_to_pdf.py") $md -o $pdf
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "PDF: $pdf"
