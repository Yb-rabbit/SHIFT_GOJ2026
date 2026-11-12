# Checks that an installed Unity editor and its WebGL Build Support module come
# from the SAME release.
#
# Why this exists
# ---------------
# A Unity install can be "mixed": Unity.exe from the international 2022.3.36f1
# (95a4219250e5) while PlaybackEngines\WebGLSupport was overwritten by the China
# 2022.3.36f1c1 module (b8d22fa5b824). Unity.exe then builds a .wasm stamped
# "2022.3.36f1c1" next to a .data stamped "2022.3.36f1"; at runtime the player
# dies in globalgamemanagers with
#   Invalid serialized file version. Expected 2022.3.36f1c1. Actual 2022.3.36f1.
# and the page just shows a loading bar forever. Nothing in the project can fix
# that -- only repairing/ reinstalling the module can.
#
# Usage
#   powershell -ExecutionPolicy Bypass -File Tools\check-webgl-module.ps1
#   powershell -ExecutionPolicy Bypass -File Tools\check-webgl-module.ps1 -EditorPath 'D:\UnityHubNocn\Unity2022.3.36f1\Unity 2022.3.36f1'
#
# Exit code 0 = module matches the editor, 1 = mixed or missing.

[CmdletBinding()]
param(
    [string] $EditorPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path

# --- locate the editor ------------------------------------------------------
if (-not $EditorPath) {
    $verFile = Join-Path $root '..\ProjectSettings\ProjectVersion.txt'
    $ver = if (Test-Path -LiteralPath $verFile) {
        ((Select-String -LiteralPath $verFile -Pattern 'm_EditorVersion:\s*(\S+)').Matches[0].Groups[1].Value)
    } else { $null }

    $candidates = @()
    if ($ver) {
        $candidates += "D:\UnityHubNocn\Unity$ver\Unity $ver"
        $candidates += "C:\Program Files\Unity\Hub\Editor\$ver"
        $candidates += "D:\UnityHubLab\Unity $ver"
        $candidates += "D:\Unity\Hub\Editor\$ver"
    }
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath (Join-Path $c 'Editor\Unity.exe')) { $EditorPath = $c; break }
    }
    if (-not $EditorPath) {
        throw "could not find Unity $ver automatically; pass -EditorPath <install root containing Editor\Unity.exe>"
    }
}

$EditorPath = [System.IO.Path]::GetFullPath($EditorPath).TrimEnd('\')
$unityExe   = Join-Path $EditorPath 'Editor\Unity.exe'
$webglDir   = Join-Path $EditorPath 'Editor\Data\PlaybackEngines\WebGLSupport'

Write-Host ''
Write-Host "editor : $EditorPath"
if (-not (Test-Path -LiteralPath $unityExe)) { throw "not an editor install: $unityExe" }
if (-not (Test-Path -LiteralPath $webglDir)) {
    Write-Host 'module : WebGL Build Support is NOT installed'
    Write-Host ''
    Write-Host 'status : MISSING -> add the WebGL module in Unity Hub, then rebuild.'
    exit 1
}

$product = (Get-Item -LiteralPath $unityExe).VersionInfo.ProductVersion
$editorRev = if ($product -match '_([0-9a-f]{12})$') { $Matches[1] } else { '' }
Write-Host "version: $product"

# --- scan the player libraries for their baked-in "Unity x.y.z (rev)" --------
# Only the core module carries the banner; the other .a files are per-API.
$libRoot = Join-Path $webglDir 'BuildTools\lib'
if (-not (Test-Path -LiteralPath $libRoot)) { throw "missing $libRoot" }

$files = Get-ChildItem -LiteralPath $libRoot -Directory |
    ForEach-Object { Join-Path $_.FullName 'WebGLSupport_CoreModule_Dynamic.a' } |
    Where-Object { Test-Path -LiteralPath $_ }

$found = [ordered]@{}
foreach ($f in $files) {
    $bytes = [System.IO.File]::ReadAllBytes($f)
    $text  = [System.Text.Encoding]::GetEncoding(28591).GetString($bytes)
    foreach ($m in [regex]::Matches($text, '(20\d\d\.\d+\.\d+[a-z]\d+(?:c\d+)?)[ _]\(?([0-9a-f]{12})\)?')) {
        $key = "$($m.Groups[1].Value) ($($m.Groups[2].Value))"
        if (-not $found.Contains($key)) { $found[$key] = @() }
        $dir = Split-Path -Leaf (Split-Path -Parent $f)
        if (-not $found[$key].Contains($dir)) { $found[$key] += $dir }
    }
}

if ($found.Count -eq 0) {
    Write-Host 'player : no version banner found in the player libraries'
    Write-Host ''
    Write-Host 'status : UNKNOWN -> the module looks incomplete, reinstall WebGL Build Support.'
    exit 1
}

foreach ($key in $found.Keys) {
    Write-Host "player : $key   [$($found[$key] -join ', ')]"
}

$bad = @($found.Keys | Where-Object { -not $_.EndsWith("($editorRev)") })
if ($bad.Count -eq 0) {
    Write-Host ''
    Write-Host 'status : OK -> module matches the editor, WebGL builds from this install are sane.'
    exit 0
}

Write-Host ''
Write-Host 'status : MIXED -> every WebGL build from this install aborts at boot.'
Write-Host "         editor revision : $editorRev"
Write-Host '         player library  : does not match'
Write-Host ''
Write-Host 'fix: 1. quit Unity'
Write-Host "     2. rename  $webglDir"
Write-Host '        to WebGLSupport_broken   (a broken module has no further use)'
Write-Host '     3. reinstall WebGL Build Support for this exact version:'
Write-Host '        Unity Hub -> Installs -> <version> -> Add modules -> WebGL Build Support'
Write-Host '        (or run UnitySetup-WebGL-Support-for-Editor-<version>.exe and point it at'
Write-Host "         $EditorPath )"
Write-Host '     4. re-run this script, then rebuild the project'
Write-Host '     5. on a CN connection the download 302s to unitychina.cn and you get a c1'
Write-Host '        module again -- then switch editors instead: see docs/WEBGL_PUBLISH.md'
exit 1



