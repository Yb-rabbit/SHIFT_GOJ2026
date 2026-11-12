# Builds the standalone editor: dist/ShiftGoLevelStudio.html
#
# One HTML file, no server, no .NET, no Unity. Double-click it and it runs.
# (Browsers refuse fetch() from file://, so the stylesheet, both scripts and the
#  type catalog are inlined here instead of loaded as separate files.)
#
# Single source of truth -- this script only packages, it never edits:
#   wwwroot/index.html        layout (shared with the local-service build)
#   wwwroot/styles.css        styling
#   wwwroot/app.js            editor logic (shared, host-agnostic)
#   web-src/host-static.js    the browser-only HOST implementation
#   web-src/catalog.json      Unity-exported element type snapshot
#
# Re-run after touching any of those. Output is overwritten every time.
#
# Usage:  powershell -ExecutionPolicy Bypass -File build-web.ps1

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$www = Join-Path $root 'wwwroot'
$src = Join-Path $root 'web-src'
$dist = Join-Path $root 'dist'

function Read-Utf8([string] $path) {
    if (-not (Test-Path -LiteralPath $path)) { throw "missing source: $path" }
    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}

function Replace-Once([string] $text, [string] $needle, [string] $replacement, [string] $label) {
    if (-not $text.Contains($needle)) { throw "$label anchor not found -- did index.html change?" }
    return $text.Replace($needle, $replacement)
}

$html    = Read-Utf8 (Join-Path $www 'index.html')
$css     = Read-Utf8 (Join-Path $www 'styles.css')
$app     = Read-Utf8 (Join-Path $www 'app.js')
$hostJs  = Read-Utf8 (Join-Path $src 'host-static.js')

# Type catalog: prefer the live export from Unity when the project is at hand,
# fall back to the committed snapshot so this still builds from a bare checkout.
$liveCatalog = Join-Path $root '..\..\LevelJson\exported\typeCatalog.json'
if (Test-Path -LiteralPath $liveCatalog) {
    $catalogPath = $liveCatalog
} else {
    $catalogPath = Join-Path $src 'catalog.json'
}
$catalog = (Read-Utf8 $catalogPath).Trim()

# Nothing may smuggle a closing script tag into the bundle.
foreach ($item in @(
    @{ name = 'app.js';          body = $app },
    @{ name = 'host-static.js';  body = $hostJs },
    @{ name = 'catalog.json';    body = $catalog }
)) {
    if ($item.body -match '</script') {
        throw "$($item.name) contains '</script>' and would break the inline bundle"
    }
}

# Accept either { "types": [...] } or a bare [ ... ] array.
if ($catalog.StartsWith('[')) {
    $catalog = '{ "schema": 2, "types": ' + $catalog + ' }'
}

# --- 1. stylesheet -> <style>
$html = Replace-Once $html '<link rel="stylesheet" href="styles.css" />' `
    "<style>`n$css`n</style>" 'stylesheet'

# --- 2. host.js -> inlined catalog + host-static.js
$hostBlock = "<script>`nwindow.__CATALOG__ = $catalog;`n</script>`n<script>`n$hostJs`n</script>"
$html = Replace-Once $html '<script src="host.js"></script>' $hostBlock 'host.js script'

# --- 3. app.js -> inline
$html = Replace-Once $html '<script src="app.js"></script>' "<script>`n$app`n</script>" 'app.js script'

# --- 4. no external references may survive
foreach ($leak in @('src="app.js"', 'src="host.js"', 'href="styles.css"')) {
    if ($html.Contains($leak)) { throw "external reference survived the bundle: $leak" }
}

New-Item -ItemType Directory -Force -Path $dist | Out-Null
$out = Join-Path $dist 'ShiftGoLevelStudio.html'
[System.IO.File]::WriteAllText($out, $html, (New-Object System.Text.UTF8Encoding $false))

$kb = [math]::Round((Get-Item -LiteralPath $out).Length / 1KB, 1)
Write-Host ''
Write-Host "built  : $out"
Write-Host "size   : $kb KB"
Write-Host "catalog: $catalogPath"
Write-Host "run    : double-click it -- no server, no .NET, no Unity"
Write-Host ''
