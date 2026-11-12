# Packages the Unity WebGL build for itch.io: Builds/WebGL -> Builds/SHIFT_GO2026-webgl.zip
#
# Why this exists
# ---------------
# itch.io needs index.html at the zip ROOT (not inside a WebGL/ directory), and it
# will happily accept a broken upload, so this script refuses to zip anything that
# cannot boot. It also catches the failure mode that actually broke the 11-13
# upload: a .wasm/.data pair produced by two different Unity editors, which aborts
# boot with "Invalid serialized file version ... globalgamemanagers".
#
# Checks
#   - every file index.html points at exists (loader / framework / data / code)
#   - WebGL.wasm starts with the magic bytes 00 61 73 6d
#   - WebGL.data starts with UnityWebData1.0
#   - the player version baked into .wasm matches the version baked into .data
#   - no hand-made .br / .gz strays (Unity writes .unityweb, never those)
#   - the produced zip has index.html at its root
#
# Usage
#   powershell -ExecutionPolicy Bypass -File Tools\pack-webgl.ps1
#   powershell -ExecutionPolicy Bypass -File Tools\pack-webgl.ps1 -NoZip
#   powershell -ExecutionPolicy Bypass -File Tools\pack-webgl.ps1 -BuildDir D:\x\WebGL -OutZip D:\x\game.zip

[CmdletBinding()]
param(
    [string] $BuildDir,
    [string] $OutZip,
    [switch] $NoZip
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

# $PSScriptRoot is not bound yet while parameter defaults are evaluated.
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $BuildDir) { $BuildDir = Join-Path $root '..\Builds\WebGL' }
if (-not $OutZip) { $OutZip = Join-Path $root '..\Builds\SHIFT_GO2026-webgl.zip' }

$latin1 = [System.Text.Encoding]::GetEncoding(28591)
$verPat = '20\d\d\.\d+\.\d+[a-z]\d+(?:c\d+)?'
$quote  = '"'

function Fail([string] $message) {
    throw $message
}

function Read-Text([string] $path) {
    if (-not (Test-Path -LiteralPath $path)) { Fail "missing file: $path" }
    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}

$BuildDir = [System.IO.Path]::GetFullPath($BuildDir)
$OutZip   = [System.IO.Path]::GetFullPath($OutZip)

Write-Host ''
Write-Host "source : $BuildDir"

# --- 1. index.html must be at the root and must name its own payload ----------
$indexPath = Join-Path $BuildDir 'index.html'
$html = Read-Text $indexPath

$buildUrlMatch = [regex]::Match($html, 'var\s+buildUrl\s*=\s*' + $quote + '([^' + $quote + ']+)' + $quote)
if (-not $buildUrlMatch.Success) { Fail 'index.html: no "var buildUrl = ..."' }
$buildUrl  = $buildUrlMatch.Groups[1].Value
$buildRoot = Join-Path $BuildDir $buildUrl

$refs = [ordered]@{}
foreach ($key in @('loaderUrl', 'frameworkUrl', 'dataUrl', 'codeUrl')) {
    $pattern = $key + '\s*[:=]\s*buildUrl\s*\+\s*' + $quote + '([^' + $quote + ']+)' + $quote
    $m = [regex]::Match($html, $pattern)
    if (-not $m.Success) { Fail "index.html: cannot read $key" }
    $refs[$key] = $m.Groups[1].Value.TrimStart('/')
}

$resolved = [ordered]@{}
foreach ($key in $refs.Keys) {
    $full = Join-Path $buildRoot $refs[$key]
    if (-not (Test-Path -LiteralPath $full)) {
        Fail "index.html references $key = $($refs[$key]) but the file is missing: $full"
    }
    $resolved[$key] = $full
}

# --- 2. WebGL payload magic numbers -----------------------------------------
$wasmPath = $resolved['codeUrl']
$dataPath = $resolved['dataUrl']

$wasmBytes = [System.IO.File]::ReadAllBytes($wasmPath)
if ($wasmBytes.Length -lt 8 -or
    $wasmBytes[0] -ne 0x00 -or $wasmBytes[1] -ne 0x61 -or
    $wasmBytes[2] -ne 0x73 -or $wasmBytes[3] -ne 0x6D) {
    Fail "$wasmPath is not a WebAssembly module (bad magic)"
}

$dataBytes = [System.IO.File]::ReadAllBytes($dataPath)
if ($latin1.GetString($dataBytes, 0, 15) -ne 'UnityWebData1.0') {
    Fail "$dataPath does not start with UnityWebData1.0"
}

# --- 3. one editor must have produced both halves ----------------------------
# The player carries "2022.3.36f1 (95a4219250e5)" style banner text, the player
# data carries the AssetBundle revision "5.x.x\0" + "2022.3.36f1". If those two
# disagree the build dies in PlayerInitEngineNoGraphics before the first frame.
$wasmText = $latin1.GetString($wasmBytes)
$dataText = $latin1.GetString($dataBytes)
$wasmBytes = $null
$dataBytes = $null

$wasmVersion = $null
$banner = [regex]::Match($wasmText, '(' + $verPat + ') \([0-9a-f]{12}\)')
if ($banner.Success) { $wasmVersion = $banner.Groups[1].Value }

$dataVersion = $null
$bundleHeader = [regex]::Match($dataText, '5\.x\.x' + [char]0 + '(' + $verPat + ')')
if ($bundleHeader.Success) { $dataVersion = $bundleHeader.Groups[1].Value }

$wasmText = $null
$dataText = $null

if (-not $wasmVersion -or -not $dataVersion) {
    Write-Host 'WARN  : could not read the baked editor version from .wasm/.data -- cross-check skipped'
} elseif ($wasmVersion -ne $dataVersion) {
    Fail @"
MIXED-EDITOR BUILD -- this cannot boot.

  player (.wasm) built by : $wasmVersion
  player data (.data)     : $dataVersion

The WebGL player aborts with
  'Invalid serialized file version. File: "globalgamemanagers"'
before the first frame, which on screen looks like a loading bar that never ends.

Fix:
  1. close Unity
  2. delete Library\Bee\artifacts\WebGL   (stale player/wasm cache)
  3. delete Library\PlayerDataCache
  4. rebuild with ONE editor, then re-run this script

A stale Library\Bee\artifacts\WebGL\...\build.wasm is one carrier: Unity reuses
it for later builds, so settings changes never reach the browser.

If the mismatch survives a FULL rebuild (Bee artifacts deleted), the editor
install itself is mixed. Check that

  Editor\Data\PlaybackEngines\WebGLSupport\BuildTools\lib\modules*\
      WebGLSupport_CoreModule_Dynamic.a

carries the same revision as the editor: a build from another edition (e.g. the
China 2022.3.36f1c1 player inside an international 2022.3.36f1 install) makes
EVERY WebGL build from that install unbootable. Reinstall the WebGL Build
Support module.
"@
}

# --- 4. compression leftovers ------------------------------------------------
$strays = @(Get-ChildItem -LiteralPath $BuildDir -Recurse -File |
    Where-Object { $_.Extension -eq '.br' -or $_.Extension -eq '.gz' })
if ($strays.Count -gt 0) {
    Fail ("hand-compressed files found -- Unity never writes these, delete them: " +
        (($strays | ForEach-Object { $_.Name }) -join ', '))
}

$unityWeb = @(Get-ChildItem -LiteralPath $BuildDir -Recurse -File -Filter '*.unityweb')
if ($unityWeb.Count -gt 0) {
    Write-Host 'WARN  : compressed payload (*.unityweb) -- Decompression Fallback must be on'
}
if (-not (Test-Path -LiteralPath (Join-Path $BuildDir 'StreamingAssets'))) {
    Write-Host 'note  : no StreamingAssets folder (nothing streamed, fine for this game)'
}

# --- 5. report ---------------------------------------------------------------
$files = Get-ChildItem -LiteralPath $BuildDir -Recurse -File
$total = ($files | Measure-Object Length -Sum).Sum
foreach ($f in ($files | Sort-Object Length -Descending)) {
    if ($f.Length -ge 1MB) {
        $rel = $f.FullName.Substring($BuildDir.Length + 1)
        Write-Host ('        {0,-34} {1,8:N1} MB' -f $rel, ($f.Length / 1MB))
    }
}
Write-Host ('        {0,-34} {1,8:N1} MB' -f '(all files)', ($total / 1MB))
Write-Host "player : wasm=$wasmVersion  data=$dataVersion  (must match)"

if ($NoZip) {
    Write-Host 'OK    : checks passed, zip skipped (-NoZip)'
    Write-Host ''
    return
}

# --- 6. zip with index.html at the root --------------------------------------
if (Test-Path -LiteralPath $OutZip) { Remove-Item -LiteralPath $OutZip -Force }
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $OutZip) | Out-Null

[System.IO.Compression.ZipFile]::CreateFromDirectory(
    $BuildDir, $OutZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$zip = [System.IO.Compression.ZipFile]::OpenRead($OutZip)
try {
    $entries = @($zip.Entries | ForEach-Object { $_.FullName })
} finally {
    $zip.Dispose()
}
if ($entries -notcontains 'index.html') {
    Fail "zip is missing index.html at its root: $OutZip"
}
$nested = @($entries | Where-Object { $_ -like '*/index.html' })
if ($nested.Count -gt 0) {
    Fail ("index.html is nested (" + $nested[0] + ") -- itch.io would show a bare file listing")
}

Write-Host ''
Write-Host "built  : $OutZip"
Write-Host ('size   : {0:N1} MB  ({1} entries)' -f ((Get-Item -LiteralPath $OutZip).Length / 1MB), $entries.Count)
Write-Host 'upload : itch.io -> project -> Edit game -> Uploads, kind "HTML",'
Write-Host '         tick "This file will be played in the browser"'
Write-Host ''
