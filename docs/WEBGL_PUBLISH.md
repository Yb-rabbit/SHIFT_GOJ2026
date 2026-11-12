# WebGL 发布手册（itch.io）

> 适用版本：Unity 2022.3.36f1 ｜ 构建目录：`Builds/WebGL/` ｜ 打包脚本：`Tools/pack-webgl.ps1`
>
> 本文只讲 **把游戏发到 itch.io 并且真能跑起来**。关卡编辑在 [BUILD_MODE.md](BUILD_MODE.md)，
> 玩家操作在 [PLAYER_GUIDE.md](PLAYER_GUIDE.md)。

## 一分钟流程

```powershell
# 1) 出包（Unity：File > Build Settings > WebGL > Build，输出到 Builds/WebGL）
# 2) 校验 + 打包（这些检查会在出坏包时直接报错中止）
powershell -ExecutionPolicy Bypass -File Tools\pack-webgl.ps1
# 3) itch.io: Edit game > Uploads > 上传 Builds\SHIFT_GO2026-webgl.zip
#             Kind = HTML，勾选 "This file will be played in the browser"
```

`pack-webgl.ps1` 只做检查和压缩，不构建；`-NoZip` 只检查，`-BuildDir/-OutZip` 可换路径。

## 构建设置

`ProjectSettings/ProjectSettings.asset` 当前值：

| 设置 | 值 | 说明 |
|---|---|---|
| Development Build | **关** | 打开后 wasm 会从 ~28 MB 涨到 ~53 MB，itch 上首次加载明显变慢 |
| Compression Format | Disabled（`webGLCompressionFormat: 2`） | itch 的静态托管对 `.br` 支持不稳；禁用后直接传原始 wasm |
| Decompression Fallback | 不适用 | 只有压缩格式不是 Disabled 时才需要勾 |
| Data Caching | 开（`webGLDataCaching: 1`） | 用 IndexedDB 缓存 `.data`；iframe 内可能失败，失败只会变慢、不会卡死 |
| Memory Size | 256 MB（`webGLMemorySize`），初始 32 MB 几何增长 | 足够本作 |
| Threads / Wasm SIMD | 关 | 开了会强制要求 COOP/COEP 响应头，itch 默认不给 |

## itch.io 设置

1. **Kind of project**：`HTML`
2. 上传后勾选 **"This file will be played in the browser"**
3. **Embed 尺寸**：`960 x 600`（和 `index.html` 里的 canvas 一致），勾上 scrollbars
4. **Mobile**：关掉（模板里虽然有移动端分支，但本作没做触屏）
5. zip **根目录必须直接是 `index.html`**，不能是 `WebGL/index.html`——脚本会验证这一点

## 踩过的坑：混合版本构建（2026-11-13 那次白屏）

症状：页面能打开，Unity 的进度条走完/走一半就停住，游戏永远不启动，itch 上不出任何错误提示。

浏览器控制台里的真话：

```
Invalid serialized file version. File: "globalgamemanagers".
Expected version: 2022.3.36f1c1. Actual version: 2022.3.36f1.
PlayerInitEngineNoGraphics settings: Failed to load PlayerSettings (internal index #0).
```

`.wasm`（引擎）和 `.data`（玩家数据）**来自两个不同的 Unity 编辑器**：

| 文件 | 里面记的编辑器 |
|---|---|
| `Build/WebGL.wasm` | `2022.3.36f1c1 (b8d22fa5b824)` — 中国版 |
| `Build/WebGL.data` | `2022.3.36f1`（AssetBundle 头 `5.x.x` + `2022.3.36f1`）— 国际版 |

版本号只差一个 `c1`，序列化格式就互不兼容，引擎在 `globalgamemanagers` 阶段直接中止。
**这跟服务器、压缩、跨域都无关**，换任何宿主都一样。

为什么改了设置也没用：Unity 会把上次链接出来的 wasm 缓存在
`Library/Bee/artifacts/WebGL/build/debug_WebGL_wasm/build.wasm`，
后面每次构建直接复制它。但**这次连清掉缓存 + 全量重建都还是 `c1` 的 wasm**，
继续查下去发现是**编辑器安装本身被污染**：

```
Editor\Data\PlaybackEngines\WebGLSupport\BuildTools\lib\modules*\
    WebGLSupport_CoreModule_Dynamic.a      ← 里面写的是 b8d22fa5b824（中国版）
```

`Unity.exe` 报的是 `2022.3.36f1_95a4219250e5`（国际版），但 WebGL 播放器模块（那 7 个
`WebGLSupport_*_Dynamic.a`）来自中国版 2022.3.36f1c1（`b8d22fa5b824`）。整个 `lib` 目录里
找不到任何 `95a4219250e5`。**这种安装出的每一个 WebGL 包都启动不了**，跟项目无关。

## 修复路径（二选一）

**A. 修编辑器（推荐，不动项目）**

1. 关闭 Unity
2. Unity Hub → `2022.3.36f1` → 卸载/重装 **WebGL Build Support** 模块
   （Hub 里认不出这个自定义路径的话，直接用官方 `UnitySetup-WebGL-Support-for-Editor-2022.3.36f1.exe`
   装到 `D:\UnityHubNocn\Unity2022.3.36f1\Unity 2022.3.36f1`）
3. 删掉 `Library\Bee\artifacts\WebGL`、`Library\PlayerDataCache`
4. 重新 Build，然后 `pack-webgl.ps1 -NoZip` 确认两个版本号一致

**B. 换一个自洽的编辑器**

本机 `D:\UnityHubNocn\Unity2023.2.20f1\Unity 2023.2.20f1` 的 WebGL 模块是自洽的
（`0e25a174756c` 对得上），但会把项目升到 2023.2.20f1（`ProjectVersion.txt`、资源重导入都变）。
比赛项目不建议轻易升级，除非 A 走不通。

> 一句话：**一个项目的 WebGL 包必须由同一个、未混装的编辑器产出**。
> 本机同时装过 Unity 中国版（`c1`）和国际版，切编辑器后一定要清 `Library/Bee/artifacts/WebGL`。

## 本地复现（上传前先自己跑一遍）

```powershell
# 任意静态服务器都行，注意 .wasm 的 Content-Type 必须是 application/wasm
cd Builds\WebGL
python -m http.server 8099
# 浏览器打开 http://127.0.0.1:8099/ ，F12 看 Console
```

要点：`file://` 直接双击 `index.html` 一定不行（fetch 被拦），必须走 http。
如果本地 http 正常、itch 上不正常，问题才在 itch 设置；如果本地就卡在进度条，
先看控制台里是不是又出现了 `Invalid serialized file version`。

## 上传前检查清单

- [ ] `pack-webgl.ps1` 跑过且没有报错（版本号一致、没有 `.br/.gz`、zip 根目录有 `index.html`）
- [ ] 本地 `http://127.0.0.1:8099/` 能进主菜单，能开一关
- [ ] zip 体积在 35 MB 上下（Development Build 会是 ~60 MB，说明忘了关）
- [ ] itch：Kind = HTML、勾了浏览器运行、嵌入尺寸 960x600
- [ ] 上传后强刷（Ctrl+F5）再看，itch 会缓存旧包
