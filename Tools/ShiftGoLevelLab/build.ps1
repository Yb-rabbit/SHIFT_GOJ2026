#Requires -Version 5.1
<#
    把 src/ 下的模板、样式和所有脚本内联成**一个** dist/ShiftGoLevelLab.html。

    为什么不用打包器：交付要求是"离线、单文件、双击就能用"。
    内联之后没有 <script src>、没有 fetch、没有 ES module，file:// 直接打开即可。
    这里同时做三条守卫：脚本里混进 </script、模板里残留 src/href、输出不是 UTF-8 无 BOM。
#>
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root 'src'
$distDir = Join-Path $root 'dist'
$templatePath = Join-Path $srcDir 'index.html'
$stylePath = Join-Path $srcDir 'styles.css'
$outPath = Join-Path $distDir 'ShiftGoLevelLab.html'

# 顺序即依赖顺序：前面的文件只挂 window.*，后面的文件读它。
$scriptFiles = @(
    'engine/dir.js',
    'engine/document.js',
    'engine/simulation.js',
    'engine/check.js',
    'engine/catalog.js',
    'samples.js',
    'selftest.js',
    'grid.js',
    'timeline.js',
    'docs.js',
    'hand.js',
    'ui.js'
)

function Read-Utf8([string]$path) {
    return [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
}

if (-not (Test-Path -LiteralPath $templatePath)) { throw "缺少模板：$templatePath" }
if (-not (Test-Path -LiteralPath $stylePath)) { throw "缺少样式：$stylePath" }

$style = Read-Utf8 $stylePath
$jsParts = New-Object System.Collections.Generic.List[string]
$totalLines = 0

foreach ($relative in $scriptFiles) {
    $full = Join-Path $srcDir $relative
    if (-not (Test-Path -LiteralPath $full)) { throw "缺少脚本：$full" }

    $code = Read-Utf8 $full
    if ($code -match '(?i)</script') {
        throw "$relative 里出现了 </script，内联后会截断整页脚本"
    }

    $totalLines += ($code -split "`n").Count
    $jsParts.Add("/* ===== $relative ===== */`n$code")
}

$javascript = [string]::Join("`n`n", $jsParts)

$html = Read-Utf8 $templatePath
if ($html.IndexOf('/* __STYLE__ */') -lt 0) { throw '模板里找不到样式占位 /* __STYLE__ */' }
if ($html.IndexOf('/* __SCRIPTS__ */') -lt 0) { throw '模板里找不到脚本占位 /* __SCRIPTS__ */' }

$stamp = (Get-Date).ToString('yyyy-MM-dd HH:mm')
$html = $html.Replace('/* __STYLE__ */', $style)
$html = $html.Replace('/* __SCRIPTS__ */', $javascript)

# 守卫：时间戳必须真的被替换（曾经因为把带时间戳的产物写回源码，页脚一直停在旧时间）。
if ($html.IndexOf('__BUILD_STAMP__') -lt 0) {
    throw '脚本里没有 __BUILD_STAMP__ 占位：构建时间不会更新'
}
$html = $html.Replace('__BUILD_STAMP__', $stamp)

# 守卫：单文件里不允许再有任何外部引用
$externalPattern = '(?i)(src|href)\s*=\s*"' + '|(?i)\bfetch\s*\(' + '|(?i)\bimport\s+[^;(]*\bfrom\b'
$match = [regex]::Match($html, $externalPattern)
if ($match.Success) {
    $lineStart = $html.LastIndexOf("`n", $match.Index)
    if ($lineStart -lt 0) { $lineStart = 0 }
    $snippet = $html.Substring($lineStart, [Math]::Min(160, $html.Length - $lineStart))
    throw "输出里仍有外部引用或 fetch/import：$snippet"
}

if (-not (Test-Path -LiteralPath $distDir)) {
    New-Item -ItemType Directory -Path $distDir | Out-Null
}

$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
[System.IO.File]::WriteAllText($outPath, $html, $utf8NoBom)

$sizeKb = [Math]::Round((Get-Item -LiteralPath $outPath).Length / 1KB, 1)
Write-Host "✔ 已生成 $outPath"
Write-Host "  $sizeKb KB · $($scriptFiles.Count) 个脚本 · 约 $totalLines 行 JS · build $stamp"
