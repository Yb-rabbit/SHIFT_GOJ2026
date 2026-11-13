# WebGL 发布手册（itch.io）

> 目标编辑器：**Unity 2023.2.20f1**（`D:\UnityHubNocn\Unity2023.2.20f1\Unity 2023.2.20f1`，模块自洽）
> 构建目录：`Builds/WebGL/` ｜ 体检：`Tools/check-webgl-module.ps1` ｜ 打包：`Tools/pack-webgl.ps1`
>
> 本文只讲 **把游戏发到 itch.io 并且真能跑起来**。关卡编辑在 [BUILD_MODE.md](BUILD_MODE.md)，
> 玩家操作在 [PLAYER_GUIDE.md](PLAYER_GUIDE.md)。

## 一分钟流程

```powershell
# 0) 先体检编辑器，输出 status : OK 才继续（MIXED 的话构建出来必然是废包）
powershell -ExecutionPolicy Bypass -File Tools\check-webgl-module.ps1
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

## 最终结论与修复路径（2026-11-13）

A 方案（修好 2022.3.36f1 的模块）在本机**不可行**，卡的不是技术是网络：

```
download.unity3d.com/.../UnitySetup-WebGL-Support-for-Editor-2022.3.36f1.exe
  302 -> download.unitychina.cn/...        (证书 *.unitychina.cn, Server=AliyunOSS)
```

Unity 按出口 IP 做地理重定向，给 CN 用户发的都是中国版模块安装器：那个 560 MB 的安装器装完，
`WebGLSupport_CoreModule_Dynamic.a` 里依旧是 `b8d22fa5b824`。两个下载 hash 的 ETag 完全相同
= 同一个文件。代理 / 改 UA / 换 hash 都绕不过去，除非整条链路换出口 IP。

所以走 B：**改用本机已自洽的 2023.2.20f1（`0e25a174756c`）**，代价是项目要升版：

1. 关闭 Unity，备份 `ProjectSettings/ProjectVersion.txt` 和未提交改动
2. 用 `D:\UnityHubNocn\Unity2023.2.20f1\Unity 2023.2.20f1\Editor\Unity.exe` 打开项目
   （Hub → Installs → Locate 指到该目录），等全量重导入完成（首次 10~30 分钟）
3. 按 2022→2023 的 API 差异修编译错误（本作只用基础 2D 功能，风险低）
4. 删 `Library\Bee\artifacts\WebGL` 和 `Library\PlayerDataCache`（换编辑器后必做，否则会复用旧 wasm）
5. 体检 → 构建 → 打包：

```powershell
powershell -ExecutionPolicy Bypass -File Tools\check-webgl-module.ps1 -EditorPath 'D:\UnityHubNocn\Unity2023.2.20f1\Unity 2023.2.20f1'
# 期望输出：player : 2023.2.20f1 (0e25a174756c)   status : OK
# Unity: Build Settings > WebGL > Build -> Builds\WebGL
powershell -ExecutionPolicy Bypass -File Tools\pack-webgl.ps1 -NoZip
```

> 一句话：**一个项目的 WebGL 包必须由同一个、未混装的编辑器产出**。
> 本机同时装过 Unity 中国版（`c1`）和国际版，任何换编辑器动作之后都要清 `Library/Bee/artifacts/WebGL`。

## 体检脚本 check-webgl-module.ps1

读编辑器 `Data\PlaybackEngines\WebGLSupport\BuildTools\lib\modules*\` 下那 7 个
`WebGLSupport_*_Dynamic.a`，跟编辑器自身的版本串对比：

| 输出 | 含义 |
|---|---|
| `status : OK`，exit 0 | 模块 revision 与编辑器一致，出包可以放心 |
| `status : MIXED`，exit 1 | 播放器库来自另一个版本（本机就是 `f1c1`），**怎么构建都是废包** |
| `status : MISSING`，exit 1 | 没装 WebGL 模块，或 `-EditorPath` 指错了 |

不传 `-EditorPath` 时会在 `C:\Program Files\Unity\Hub\Editor\*`、`secondaryInstallPath` 等常见位置自动找。
注意 `.a` 里的版本串**没有** `Unity ` 前缀，形如 `2022.3.36f1c1 (b8d22fa5b824)` / `2022.3.36f1c1_b8d22fa5b824`。

## 本地复现（上传前先自己跑一遍）

```powershell
# 静态服务器：自带 application/wasm MIME（Unity 缺了它不启动）
node Tools\webgl-verify\serve.js Builds\WebGL 8099
# 浏览器打开 http://127.0.0.1:8099/ ，F12 看 Console

# 可选：无头 Edge 自动探测，打印控制台报错 / 5s 一次 canvas+进度条状态 / 截屏
msedge --headless=new --disable-gpu --remote-debugging-port=9222 --user-data-dir=%TEMP%\edge-verify about:blank
node Tools\webgl-verify\cdp-probe.js http://127.0.0.1:8099/ 9222 120
# 产物：%TEMP%\shiftgo_cdp.log、%TEMP%\shiftgo_shot.png
```

要点：`file://` 直接双击 `index.html` 一定不行（fetch 被拦），必须走 http。
如果本地 http 正常、itch 上不正常，问题才在 itch 设置；如果本地就卡在进度条，
先看控制台里是不是又出现了 `Invalid serialized file version`。

## 运行时坑：MeshCollider 被剥离（2026-11-13 切到 2023 后暴露）

坏包修好之后，浏览器里还有一类只在打包后出现的报错：

```
Can't add component because class 'MeshCollider' doesn't exist!
```

页面能启动、能进主菜单。现有这几处调用紧接着就把图元自带的碰撞体 `Destroy` 掉了，
所以画面看起来正常，只是在控制台刷屏；但只要以后有地方真的依赖图元自带的碰撞体，
就会静默失去碰撞（拖上去的物体穿透地板），很难查。

- **谁在要 MeshCollider**：`GameObject.CreatePrimitive(PrimitiveType.Quad)`
  （`LevelBuilder.cs:200/242`、`BitFlowBuildMode.cs:445`、`BitFlowCardTray.cs:832/850/1902`）。
  图元自带的碰撞体是 Cube→`BoxCollider`、**Quad/Plane→`MeshCollider`**、Cylinder→`CapsuleCollider`。
- **为什么被剥掉**：`ProjectSettings.asset` 里 `stripEngineCode: 1`（引擎代码剥离开着），
  而项目里没有任何脚本 / 场景 / 预制体直接引用 `MeshCollider`，
  IL2CPP 链接器就把它当无用引擎类删了。编辑器不剥离，所以只有打包后炸。
- **修法**：仓库根 `Assets/link.xml` 只保留这一个类型（不关整体剥离，wasm 体积基本不变）：

```xml
<linker>
  <assembly fullname="UnityEngine.PhysicsModule">
    <type fullname="UnityEngine.MeshCollider" preserve="all" />
  </assembly>
</linker>
```

写 link.xml 时注意 **XML 注释里不能出现 `--`**（`-----` 分隔线会让 UnityLinker 直接抛
`System.Xml.XmlException: An XML comment cannot contain '--'`，构建失败，日志在
`%LOCALAPPDATA%\Unity\Editor\Editor.log`）。

同类风险：以后再用 `CreatePrimitive` 造别的图元（Plane / Sphere / Capsule），
如果项目里没别处引用对应碰撞体类型，同样要补进 link.xml。

## 本次核验结果（2026-11-13，Unity 2023.2.20f1）

| 步骤 | 结果 |
|---|---|
| `check-webgl-module.ps1 -EditorPath '...\Unity2023.2.20f1\Unity 2023.2.20f1'` | `status : OK`，`player : 2023.2.20f1 (0e25a174756c)` |
| Unity 构建（MCP，Output = `Builds/WebGL`，关闭 Development Build） | `Build succeeded: WebGL → Builds/WebGL (42.43 MB, 189.9s)` |
| 产物 | `WebGL.wasm` 32.3 MB、`WebGL.data` 9.7 MB、`framework.js` 0.4 MB |
| `pack-webgl.ps1 -NoZip` | `player : wasm=2023.2.20f1  data=2023.2.20f1`，`OK checks passed` |
| 无头 Edge 探针（`serve.js` + `cdp-probe.js`） | `Initialize engine version: 2023.2.20f1 (0e25a174756c)`；`hasCanvas:true 960x600`；进度条 `100% / none`；`MeshCollider` 报错 **0**；`Invalid serialized file version` **0**；截图是开场终端剧情 |
| `pack-webgl.ps1` | `Builds\SHIFT_GO2026-webgl.zip` 12.7 MB / 17 个条目，根目录有 `index.html` |

console 里还剩两类**无害**刷屏，认识一下就不会误判：

- `The AudioContext was not allowed to start ... after a user gesture`：
  浏览器自动播放策略，headless 里没人点页面就一直提示；玩家点一下就好。
- `ERROR: Shader Hidden/Universal Render Pipeline/... is not supported on this GPU`：
  URP 的调试用 shader 在无 GPU 的 headless 或部分集显上不可用，跟游戏画面无关。

## 关卡导入 / 导出（浏览器里没有文件夹）

WebGL 里 `System.IO` 只有 Emscripten 的 IndexedDB 虚拟盘（`PlayerPrefs` 就落在那里），
`Application.persistentDataPath` 不是一个玩家能翻到的目录；`LevelJsonLibrary.EnsureShareFolder` 必然失败，
`FilePicker.IsSupported` 也是 `false`。所以浏览器走另一条最小的桥：

- 导入：页面里插一个 `<input type="file" accept=".json,application/json">`，`FileReader` 读成文本后
  用 `SendMessage('BitFlowWebGLBridge', 'OnJsonPicked', 文件名 + '\u0001' + 正文)` 交回 C#。
- 导出 / 分享副本：`Blob` + `a[download]`，文件名取关卡 `displayName`（非法字符换成 `_`）。
- 实现：`Assets/Plugins/WebGL/BitFlowBrowserIo.jslib`（只做弹控件 / 收文本）
  + `Assets/Scripts/Gameplay/BitFlow/Runtime/Data/BrowserJsonImport.cs`（回调、桥对象、DllImport）。

约定（以后加浏览器能力照这个来）：

- 验货、重名、落盘全在 C#：`LevelJsonLibrary.TryImportJson` 和桌面那条 `TryImportFile`
  共用同一份 `WriteImport`，不会一边宽松一边严格。
- `SendMessage` 只接受一个参数，所以文件名与正文用 `\u0001` 拼起来，C# 按第一个 `\u0001` 拆开。
- jslib 里 `SendMessage` 的目标对象名固定 `BitFlowWebGLBridge`，改名字必须两边一起改。
- 弹文件选择器靠浏览器的「瞬时用户激活」：Unity 在自己的输入循环里处理点击（仍是同一个事件循环），
  Chrome / Edge / Firefox 实测都能弹。某个浏览器没反应时，先查是不是无头模式，或嵌在 iframe 里被
  `allow` 策略挡了。
- 浏览器里的关卡就是站点存储：清站点数据 / 换浏览器 / 换设备 = 关卡没了，要留就 `EXPORT` 成 `.json` 发出去。
- 作者关卡已经进包了：`Assets/Resources/BitFlow/Levels/*.json` 由 `BundledLevels` 在首次启动时播种进同一份站点存储，
  所以 `MY LEVELS` 里既有内置关、也有玩家自己造的关（怎么生成见下一节）。

### 内置关卡（作者关）怎么进浏览器

```
LevelJson/*.json + LevelJson/incoming/*.json
   │  SHIFT GO/BitFlow/关卡 JSON/生成内置关卡包（出包前也会自动跑）
   ▼
Assets/Resources/BitFlow/Levels/*.json        ← 生成物，要提交
   │  首次启动：LevelJsonLibrary.EnsureFolder() → BundledLevels.InstallIfNeeded()
   ▼
PlayerPrefs（IndexedDB）里的 LevelJson/*.json  ← 和玩家自己造的关同一份存储
```

- **生成**：菜单 `SHIFT GO/BitFlow/关卡 JSON/生成内置关卡包`。出包前 `IPreprocessBuildWithReport` 会自动再跑一次，
  Console 里会出现 `[BitFlow] 出包前已刷新内置关卡包：N 关 → ...`；源里删掉的关会连 `.meta` 一起从包里清掉。
  `exported/` 不参与打包（那是导出参照）。
- **为什么浏览器也能用**：播种只调 `LevelJsonLibrary` 的 `FileExists / ReadAllText / WriteAllText`，
  在 WebGL 上这些自动落到 `BrowserFileSystem`（PlayerPrefs → IndexedDB），不需要另写一套浏览器代码。
- **覆盖规则**：清单 `BitFlow.Seeds.Manifest` 记「上次播种时的内容摘要」——
  没人动过的内置关跟着新版更新；玩家改过 / 删掉的关永远以玩家为准（删了不复活）。
  详见 `LEVEL_JSON.md`。
- **加一关**：json 放进 `LevelJson/`（或 `incoming/`）→ 跑菜单 → 重新出包。
- **验证**：浏览器 `MY LEVELS` 能看到内置关 → 通关 / `EDIT` / `EXPORT` 都正常 → 删掉后刷新页面不再出现。

## 上传前检查清单

- [ ] `check-webgl-module.ps1` 输出 `status : OK`
- [ ] `pack-webgl.ps1` 跑过且没报错（wasm/data 版本一致、无 `.br/.gz`、zip 根有 `index.html`）
- [ ] `Assets/link.xml` 在（保住 `MeshCollider`，否则控制台刷 `Can't add component`）
- [ ] 本地 `http://127.0.0.1:8099/` 能进主菜单、能开一关
- [ ] zip 体积 12~13 MB（未压缩产物 ~42 MB；~60 MB 说明 Development Build 忘了关）
- [ ] itch：Kind = HTML、勾了浏览器运行、嵌入尺寸 960x600
- [ ] 浏览器里 `+ NEW LEVEL` 造一关 → `EXPORT` 能下载 `.json` → `IMPORT · FROM FILE` 能把同一份导回来
- [ ] `Assets/Resources/BitFlow/Levels/` 里就是要内置的作者关（出包日志有 `出包前已刷新内置关卡包`），
      浏览器 `MY LEVELS` 能看到它们，且删掉后刷新不复活
- [ ] 上传后 Ctrl+F5 强刷再看（itch 会缓存旧包）

## 清理记录（2026-11-13）

为排除污染删掉的东西，全部可再生或本就是错版，合计 ~1.40 GB：

| 路径 | 体积 | 为什么删 |
|---|---|---|
| `Library\Bee\artifacts\WebGL` | 584.7 MB | 缓存里就是 `c1` 的 `build.wasm` |
| `%TEMP%\UnitySetup-WebGL-Support-for-Editor-2022.3.36f1.exe` | 560.1 MB | 中国镜像发的 `c1` 模块安装器 |
| `%TEMP%\shiftgo_edge_p3` | 180.7 MB | 无头 Edge 探针的临时 profile |
| `Builds\WebGL` | 35.4 MB | 已知坏包（wasm `c1` / data `f1`） |
| `Desktop\webgl26` | 35.4 MB | 同一个坏包的桌面副本 |
| `Library\PlayerDataCache` | 3.6 MB | 播放器数据缓存，同属 `c1` |
| `D:\UnityHubNocn\Unity2022.3.36f1\...\PlaybackEngines\WebGLSupport` | 2606 MB | 中国版 `c1` 模块，跟编辑器对不上，Hub 里还会显示"已装 WebGL" |

保留：`Builds\FinalSizeCheck`（Windows 桌面构建，跟 WebGL 无关）、
`D:\UnityHubNocn\Unity2022.3.36f1` 编辑器本体（已无 WebGL 模块，Hub 里不会再显示 WebGL；
等确认新包没问题后再决定删不删，约 4~5 GB）。
