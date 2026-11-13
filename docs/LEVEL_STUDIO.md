# 外部关卡编辑器（Tools/LevelStudio）

一个跑在浏览器里的 BitFlow 关卡编辑器：**摆元件 → 看朝向 → 存 JSON**，全程不需要开 Unity。
左键放、右键擦，摆完点「这份 JSON 引擎读得懂吗」确认文件能进游戏，就够了。

## 为什么是这个形状

「引擎读不读得懂」必须由**游戏里那份**解析器来回答，否则迟早出现「编辑器说没问题、游戏里导不进去」。

所以这个工具不是重写一份 JS 引擎，而是把自己的 .NET 工程直接**链接编译** `Runtime/Core/*.cs`：

```
Tools/LevelStudio/ShiftGoLevelStudio.csproj
  └─ <Compile Include="..\..\Assets\Scripts\Gameplay\BitFlow\Runtime\Core\*.cs" />
```

于是：

- 「读不读得懂」走的是 `LevelDocumentBuilder.Build`（编辑器里那段 `problems` 就是它的输出），
  和 Unity 导入这份 json 时**同一份源码**；
- 顺带跑一遍 `LevelCheck.Run` + `BitFlowSimulation` 只当**参考**：有手牌的关本来就该跑不通，
  它不影响这份 json 能不能交付；
- JSON → `LevelDefinition` 和 Unity 侧 `LevelJsonSerializer.Read` + `LevelSO.ToDefinition` 逐条对齐；
- 没有第二套规则要维护。

代价是 `Runtime/Core/` 必须保持**零 UnityEngine 依赖**（现在就是）。哪天有人在 Core 里加了 `using UnityEngine;`，这个工具会立刻编译失败 —— 这是有意的护栏。

## 启动

Unity 菜单 `SHIFT GO/BitFlow/关卡编辑器`（会先顺手把类型目录刷一遍），或者双击
`Tools/LevelStudio/启动关卡编辑器.bat`。同一个按钮也在
`SHIFT GO/BitFlow/关卡 JSON 中心` → 「做关」页的最上面。

菜单那条会**优先直接起已经编译好的 exe**（`bin/Release/net10.0/ShiftGoLevelStudio.exe`），
点下去就开，不编译。只有两种情况才退回 `.bat` 让 dotnet 重编一次：

- 宿主或 Unity 的 `Runtime/Core/*.cs` 改过（exe 里的规则旧了）；
- `wwwroot/` 改过但还没 build（输出目录里的界面副本旧了）。

需要 .NET SDK 8+（命令行 `dotnet --version` 能看到版本即可）。启动后浏览器会自动打开
`http://127.0.0.1:5199/`（端口占用会往后找 5199~5209）。**关掉那个黑窗口 = 关掉编辑器。**

### 独立版（不需要 .NET、不需要 Unity）

`Tools/LevelStudio/dist/ShiftGoLevelStudio.html` 是**一个** HTML 文件，双击就能用：
没有本地服务、没有 .NET、没有 Unity，发给别人也不用带工程。

改了 `wwwroot/` 或 `web-src/` 之后重新构建一次：

```powershell
powershell -ExecutionPolicy Bypass -File Tools/LevelStudio/build-web.ps1
```

它把 `wwwroot/styles.css`、`wwwroot/app.js`、`web-src/host-static.js` 和
`web-src/catalog.json` 全部内联进 `wwwroot/index.html` 的一份副本里。
**没有第二份 app.js** —— 两个版本共用同一份编辑器代码。

和桌面板的差别只有三处，都是有意的：

| | 桌面板（`dotnet run`） | 独立版（`dist/*.html`） |
|---|---|---|
| 关卡存在哪 | 磁盘目录（`LevelJson/incoming` 等） | 浏览器 `localStorage`，点「下载」拿 `.json` |
| 类型快照 | 读 `LevelJson/exported/typeCatalog.json` | 构建时内联 `web-src/catalog.json` |
| 「这份 JSON 引擎读得懂吗」 | 真跑 `LevelDocumentBuilder`，只说读得懂 / 读不懂 | 如实显示「没跑」（引擎是 C#，还没移植） |

编辑、导入、导出、命名规则和桌面板**完全一样**：导出的 JSON 就是游戏 `IMPORT`
直接吃的格式，文件名派生（`displayName` → `MY_LEVEL_01.json`）也走同一条规则。

名字的规范化（`One only` → `ONE ONLY`，中文直接落回 `CUSTOM LEVEL`）由 Core 的
`LevelNaming.Sanitize` 判定，本地服务写盘时用的就是这份规则。前端只在一处做镜像：
`serializeDoc()` 里的 `displayName` —— 文件名、保存、导出、复制、下载因此**永远是
同一个值**。读入一份名字不规范的文件不偷偷改名，但会立刻提示「它在游戏里会显示成什么」。

为什么非得内联：`file://` 下浏览器拒绝 `fetch()` 本地文件，连 `catalog.json` 都读不到。
所以样式、脚本、类型快照必须长在 HTML 里 —— 这也顺带解释了为什么没上 Blazor WASM
（WASM 的加载同样要 `fetch`，一样被拦）。

> `localStorage` 是按「整个 `file://` 源」共享的：换一个目录下的另一份 `dist` 会看到同一批关卡。
> 要干净的存储就换个浏览器 profile，或者直接用桌面板。

代码从这儿分叉（改之前先看清自己改的是哪一层）：

```
wwwroot/index.html      ← 布局          两边共用
wwwroot/styles.css      ← 样式          两边共用
wwwroot/app.js          ← 编辑器逻辑    两边共用，只认 HOST.*
wwwroot/host.js         ← HOST 实现之一 连本地 /api/*
web-src/host-static.js  ← HOST 实现之二 localStorage + 内联快照
web-src/catalog.json    ← 类型快照副本
dist/*.html             ← 构建产物，别手改
```

加新能力时先问一句「纯前端能不能自己做」：能 → 写进 `host-static.js`；
必须跑 C# 的（比如「引擎读不读得懂」）→ 才在 `wwwroot/host.js` 加一条 `/api` 路由。

## 界面

| 区域 | 用途 |
|---|---|
| 左栏「元件」 | 选画笔：发射器 / 接收器 / 分流器 / 非门 / 过滤器 / 墙 |
| 中间网格 | **左键放、右键擦**；格子四边的箭头就是位会走的方向；数字是「发射位数 / 需要收到的个数」 |
| 网格下方「本关清单」 | 这一关摆了什么：尺寸 / tick / 元件按类型和参数分组 / 墙几面 / 手牌几种几张。**纯前端算，随每一笔即时更新** |
| 右栏「选中格子」 | 改朝向（四个方向键）、序列、数量、收位面、放行值、玩家可动 |
| 右栏「引擎检查」 | 只回答一件事：这份 json 引擎读不读得懂。读不懂才标红并逐条说原因 |
| 右栏「文件」 | 读 / 存关卡、导出 / 粘贴 JSON、打开目录 |

清单里「手牌」那行是**唯一**需要盯的交付信息之一：它决定了玩家拿得到什么。
至于这关好不好、能不能解，编辑器不评价 —— 那要拿着手牌按玩家身份进游戏试。

快捷键：`R` 顺时针转选中格 · `Delete` 擦掉选中格 · `1`~`6` 换画笔。

## JSON 契约

编辑器内部**一律用 `kind` 模式**，而且把每格参数都写全：

```json
{ "kind": "Emitter", "x": 0, "y": 1, "dir": "Right", "sequence": "1011", "count": 8 }
```

为什么不写 `type`：`type` 指向 Unity 里的类型资产，参数要回资产里取。写全参数之后这份 JSON
**自描述**，拷到哪里都能跑出一模一样的结果，也不会因为「资产被改了」而悄悄变味。

打开 Unity 导出的关（那些关是 `type` 模式）时，编辑器会用
`LevelJson/exported/typeCatalog.json` 这份类型快照把参数展开成 `kind` 模式再显示 ——
所以你看到的就是玩家实际会拿到的参数。**存盘时写回去的是 kind 模式**，不是原文件。

### 类型快照

`LevelJson/exported/typeCatalog.json` 由 Unity 的 `SHIFT GO/BitFlow/关卡 JSON 中心` → 「做关」→ `导出类型目录（给外部编辑器）` 生成，
内容来自每个类型资产真实的 `CreateDefinition(...)`（不是另抄一份默认值）。

- 改了 / 新增了类型资产 → 回 Unity 再导一次；
- `LevelDocumentBuilderTests.ExportedCatalogFile_MatchesLiveTypeAssets` 会在 EditMode 测试里
  比对「快照 vs 现状」，忘了导就会红。

没导出过快照也能用：`kind` 模式不依赖它，只有「打开 `type` 模式的关」才需要。

独立版把快照**烤进 HTML**（`file://` 读不到旁边的 json）。`build-web.ps1` 会优先取
`LevelJson/exported/typeCatalog.json`（工程在手时总是最新的），工程不在才退回
`web-src/catalog.json` 这份提交进仓库的副本。所以改了类型资产之后的顺序是：
Unity 菜单导一次 → 跑 `build-web.ps1` → 独立版里也能认出新类型。

## 玩家怎么玩到自己做的关

1. 编辑器里点「保存」，目录选 `LevelJson/incoming`（工程里的中转站）；
2. 回 Unity 跑菜单 `SHIFT GO/BitFlow/关卡 JSON/同步到运行时目录`（或 `一键：刷新注册表并同步`）；
   打包后的玩家则**直接点 `IMPORT · FROM FILE` 挑那个 json**（Windows 系统文件对话框），
   或者把 json 丢进 `SHARE FOLDER`（程序旁边 `Levels\share\`）再点 `IMPORT · FROM SHARE FOLDER · BATCH IMPORT`；
3. 选关面板（游戏里按 `L` 打开）的 `MY LEVELS` 分区里就会出现这一关，点一下进关。

`MY LEVELS` 每一行右边有 `...`：展开后是 `EDIT` / `SHARE · SAVE COPY` / `DELETE · JSON FILE`，
删除仍是两段式确认。
顶部 `FILES` 那一组负责 `IMPORT / SHARE FOLDER / REFRESH`，
完整操作说明见 **[PLAYER_GUIDE.md](PLAYER_GUIDE.md)**。

想让玩家进关后还能自己动手改布局，勾上「允许玩家进关后自己改」（= JSON 的 `allowBuild: true`）。

- **发了手牌**的关卡：玩家靠手牌盘动手（点卡放牌、空格抓起已有件搬走），`allowBuild` 由手牌自动打开；
- **没发手牌**的沙盒关：`allowBuild: true` 才会给作者那套 BUILD MODE（无限涂刷，B 键进出）。

两者不共存 —— 手牌关一旦挂上 BUILD MODE，玩家就能凭空补件（有限手牌失效），
而且手牌盘整排会让位给作者面板（表现就是"卡看得见、点不动"）。

## 测试

- 纯 C# 数据层：`Assets/Scripts/Gameplay/BitFlow/Tests/EditMode/LevelDocumentBuilderTests.cs`
  （资产 → 文档 → 关卡 逐字段往返、kind 模式自足、坏数据变成可读问题、墙层两种写法、类型快照是否过期）
- 护栏：`Assets/Scripts/Gameplay/BitFlow/Tests/EditMode/CoreLayerPurityTests.cs`
  （Core 里不许出现 UnityEngine / UnityEditor / MonoBehaviour / ScriptableObject；工具工程的 `Core\*.cs` 通配也不许被改成逐个列举）
- 工具自己：`dotnet build` 就是它的整条编译检查；改完 UI 用浏览器实测一遍「引擎检查」按钮和网格下方的清单。
- 独立版：改完跑一次 `build-web.ps1`，再双击 `dist/ShiftGoLevelStudio.html` 实测一遍
  「造一格 → 保存 → 列表里出现 → 下载 → 再拖进来」的闭环。
  构建脚本会在找不到内联锚点、或某个源文件里混进 `</script>` 时直接报错停下 ——
  静默产出一个坏的单文件，比构建失败难查得多。

## 两个已经踩到的坑（改这个工具时别踩回去）

1. **`System.Text.Json` 默认不序列化字段。** `LevelDocument` / `LevelTypeCatalog` 的成员全是 public 字段
   （这样 Unity 的 `JsonUtility` 才能直读），所以工具侧两份 `JsonSerializerOptions` 都必须写
   `IncludeFields = true`。漏了不会报错，只会让 `/api/catalog` 的兜底路径悄悄少掉整个 `types`。
2. **`kind` 模式的默认值必须取自 `EditableElement.CreateDefault`**，不能拿类型目录里「同种类的第一份资产」当默认。
   否则副臂朝向、放行值这些会跟着工程里排第一的那个资产走，编辑器和游戏就跑出两种结果。

