# 关卡 JSON（外部手写 ↔ Unity 关卡资产）

> 只想知道「我的 JSON 怎么进包、进没进」，看 `LEVELS_QUICKSTART.md`（30 行）。本文是字段与机制全量说明。

`LevelSO` 仍是唯一真源；JSON 只是**双向通道**：导出给人改，改完再导入/覆盖。

```
LevelJson/
├── exported/    Unity 侧导出（「导出全部关卡」另写 catalog.json 顺序表）；只是参照，**不进包**
├── incoming/    设计侧提交，等「批量导入」
└── reports/     每次校验/导入的诊断（设计侧看不到 Console，看这里）
```

打包只认根目录 `LevelJson/*.json` 和 `incoming/*.json` 两处（`LevelSeedSources.Folders`），
`exported/` 不在里面 —— 想让玩家玩到某一关，得把它放进这两处之一，再跑 `生成内置关卡包`
（见 `LEVELS_QUICKSTART.md`）。

## 流程

1. Unity：`SHIFT GO/BitFlow/关卡 JSON 中心` → 「做关」→ `导出选中关卡` → `LevelJson/exported/*.json`
2. 设计侧：手改 / 手写 JSON，丢进 `LevelJson/incoming/`
3. Unity：`校验 JSON` 看报告 → `导入为新关卡` 或 `覆盖选中关卡`（自动试跑并在报告里给结论）

## 运行时通道（选关面板 / 打包后）

`LevelSO` 仍是**编辑器**侧的真源；打包后的 Player 里没有 `Assets/`，所以运行时另有一条路：

| 角色 | 落点 |
|---|---|
| 运行时关卡目录 | `Application.persistentDataPath/LevelJson/*.json`（内部存储，玩家不需要手动翻） |
| 玩家分享 / 导入目录 | 程序旁边 `Levels/share/`（独立版 = exe 同级；编辑器 = 工程根目录）；不可写时退到 `文档/SHIFT GO/Levels`。目录说明 `README.txt` 首行带版本戳、**不写绝对路径**，旧版说明会在下次启动时自动改写 |
| 类型注册表 | `Assets/Resources/BitFlow/LevelTypeRegistry.asset`（进包，运行时 `Resources.Load`） |
| 内置关卡包（作者关卡） | `Assets/Resources/BitFlow/Levels/*.json`（**生成物但要提交**；首次启动由 `BundledLevels` 播种进运行时关卡目录） |

> **浏览器（WebGL / itch）**：上表里的「目录」一个都不存在 —— 浏览器只有 `PlayerPrefs`（落在 IndexedDB）
> 这一块存储，`EnsureShareFolder` 永远失败，`FilePicker.IsSupported` 是 `false`。
> 浏览器里：`MY LEVELS` 存在同一个浏览器的站点存储里（换浏览器 / 清站点数据 = 没了）；
> 导入走浏览器自己的文件选择器（`Assets/Plugins/WebGL/BitFlowBrowserIo.jslib` + `BrowserJsonImport.cs`）；
> 导出 / 分享变成浏览器下载的 `.json`。详见 `WEBGL_PUBLISH.md`。

- 选关面板（游戏里按 `L` 打开）的 `MY LEVELS` 分区列出运行时关卡目录里的关卡；`_` 开头的草稿不列出，读不通的文件只在 Console 报原因
- 面板顶部的 `FILES` 组是玩家侧的文件入口：
  - `IMPORT · FROM FILE` 弹系统文件对话框（Windows：编辑器 + 独立版）挑任意位置的 json；
  - `IMPORT · FROM SHARE FOLDER · BATCH IMPORT` 把玩家可见的分享目录（以及旧版 `LevelJson/incoming/`、程序旁边 `Levels/`）里能读通的 json 一次性导进来；
  - `IMPORT · FROM CLIPBOARD` 走剪贴板原文；
  - `MY LEVELS` 每一行 `...` 里的 `EDIT` / `SHARE · SAVE COPY` / `DELETE · JSON FILE` 负责编辑、复制到分享目录、删除；
  - `SHARE FOLDER` / `COPY SHARE FOLDER PATH` 打开 / 复制玩家可见的分享目录。
  导入前一律先验货（和进关同一个 `Parse`），读不通的不落盘。分享目录里的文件必须先导入，不会自动出现在列表。详见 `PLAYER_GUIDE.md`
  - **浏览器构建**里只剩 `IMPORT · FROM FILE`（浏览器文件选择器）、`EXPORT`（下载 `.json`）
    和行内的 `SHARE · SAVE COPY`（也是下载）；分享目录 / 剪贴板 / 复制路径这些按钮不会出现。
    下载文件的默认名 = 关卡的 `displayName`，导回来时按这个文件名落盘，重名规则和桌面完全一样。
- 点一条 → `LevelFlow.LoadJson` → `LevelJsonLibrary.TryLoad` → 临时 `LevelSO` → `BitFlowRunner.LoadLevel`。
  读盘/解析失败不盖黑幕，停在面板上；临时资产由 `LevelFlow` / 面板负责回收
- 类型解析只查注册表、不碰 `AssetDatabase`，所以**编辑器 Play / EditMode 测试 / 打包 Player 跑的是同一段代码**
- 进关后重开面板、或 `REPLAY` 会重新读盘 → 改了文件立刻生效
- JSON 关卡没有「下一关」（面板里也没有），因为它不属于关卡表顺序

**把工程里的关搬进运行时目录**：菜单 `SHIFT GO/BitFlow/关卡 JSON/同步到运行时目录`（或 `一键：刷新注册表并同步`）
（根目录 `LevelJson/` + `incoming/` 单向复制；`exported/` 不参与，那是导出参照。
同名先到先得、目录里已有文件不删，玩家自己扔的关不会被动掉）。
复制完会用运行时那套代码现场读一遍，读不通的当场在 Console 点名。

**作者关卡预置打包（桌面 / 浏览器都要有）**：`SHIFT GO/BitFlow/关卡 JSON 中心` → 「出包」→ `生成内置关卡包`
（出包前 `IPreprocessBuildWithReport` 也会自动跑一遍，忘了点也不会打出旧包）。

```
LevelJson/*.json + LevelJson/incoming/*.json
        │  原文照搬（复制前用运行时 Parse 验货，坏文件只在这里报一次）
        ▼
Assets/Resources/BitFlow/Levels/*.json        ← 生成物，但要提交进版本库
        │  首次启动：LevelJsonLibrary.EnsureFolder() → BundledLevels.InstallIfNeeded()
        ▼
persistentDataPath/LevelJson/*.json            ← 和玩家自己造的关躺在同一个目录
```

`exported/` 是导出参照，**不参与**打包；`_` 开头、`catalog.json`、`typeCatalog.json` 会被跳过；
源里删掉的关卡会连文件带 `.meta` 一起从包里清掉。运行时不做「内置关」的特殊分支 ——
它们就是普通 JSON 关卡，列表 / 进关 / 编辑 / 导出 / 删除全都一样。

> **运行时目录不在打包范围**：`persistentDataPath/LevelJson/`（`%AppData%Low/...`）里出现的关，
> 来自「同步到运行时目录」或玩家自己导入，`生成内置关卡包` 看不到它们。想让玩家开箱就有的关，
> 必须先落成工程里的 `LevelJson/*.json`。跑一次 `打包前自检（关卡）` 就能看出差的是哪一份。

覆盖规则（清单 `PlayerPrefs("BitFlow.Seeds.Manifest")` 记「上次播种的内容摘要」）：

| 本地状态 | 结果 |
|---|---|
| 没有这个文件，清单里也没有 | 播种 |
| 没有这个文件，清单里有（玩家删过） | **不复活** |
| 内容 == 包内内容 | 不动（已经是最新） |
| 内容 == 上次播种的内容（没人动过） | 跟着新版本覆盖 |
| 内容被改过（或玩家自己导入过同名关） | **玩家赢**，一个字节都不动 |

一句话：作者改版能到老玩家手里，玩家的改动和删除永远优先。

**新增或改名元件类型资产后**必须再跑一次 `刷新类型注册表`，否则 JSON 里的 `type` 会断链。

新拉一份工程时跑 `一键：刷新注册表并同步` 即可。

## 顶层字段

| 字段 | 说明 |
|---|---|
| `schema` | 格式版本，当前 `2`，可省。旧文件（`1`）照读：缺的字段一律取默认值 |
| `displayName` `width` `height` `tickInterval` | 关卡信息（tickInterval = 秒/步） |
| `wallType` | 墙的类型资产名，一般 `"Type_Wall"`，可省 |
| `wallRows` | 墙字符画：**一行一个 y，第 1 行是 y=0（从下往上）**，`#` 是墙，其余留空 |
| `wallMap` | 旧写法（`\n` 分行）；没有 `wallRows` 时才看它 |
| `elements` | 元件数组 |
| `supplies` | 手牌数组 |
| `allowBuild` | `schema 2` 新增：`true` = 玩家进关后可以自己动手（搬件 / 放手牌）；缺省 `false` = 只玩作者摆好的样子。<br>有 `supplies` 时自动为 `true`（发得出牌就必须放得下）。<br>**注意**：它开的不是工坊那套 BUILD MODE（无限涂刷）—— 那只给「没发手牌的沙盒关」，否则玩家能凭空补件、手牌盘也会被整个压掉 |


## 元件字段

| 字段 | 说明 |
|---|---|
| `x` `y` | 坐标，x 向右、y 向上 |
| `type` | 类型资产名（如 `Type_Emitter_E2`）。导出总会带，**写它就不用管朝向**（靠 `rot`） |
| `kind` | `Wall` `Emitter` `Receiver` `Splitter` `NotGate` `Filter` `DelayGate`，手写用这个 |
| `rot` | `0`~`3` 顺时针 90°/档（只在 `type` 模式下生效） |
| `dir` `dir2` | 绝对朝向 `Up/Right/Down/Left`（也认 `↑→↓←`、`上右下左`）；`dir2` = 分流器的 0 臂 |
| `sequence` | 发射序列 / 接收期望序列。**写了就覆盖类型资产** |
| `count` | 发射总位数 / 接收总个数。写了就覆盖；`0` 是合法值 |
| `face` | 接收器专用：`any` 四面可收 / `in` 只认进料面 |
| `pass` | 过滤器放行值 `0`/`1`（只有 `kind=Filter` 能改） |
| `delay` | 延迟器延迟 tick 数 `1`~`4`（只有 `kind=DelayGate` 能改）：比直通元件多停 N 个 tick，再从对面直行穿出（`1` = 晚 1 tick，`4` = 晚 4 tick） |
| `playerEditable` | `true` = 玩家能搬动/拾回；缺省 `false` = 关卡预设件 |

> 延迟器没有方向、也不能旋转：`dir` / `rot` 对它没有意义（四面等价）。它只改**时间**，
> 不改值也不改朝向；队列 FIFO、容量 8 个位，出口被占时队首原地等。

`type` 和 `kind` 都写时以 `type` 为准（`kind`/`dir` 只是给人看的、以及资产改名后的回退线索）。

## 手牌字段

`{ "kind": "Filter", "count": 3 }`（或 `"type": "Type_Filter_F1"`）。同一类型写多条 = 再多给几张。

要发**具体变体**（F1 而不是 F0、D3 而不是 D1）必须写 `type`：`kind` 只按「同种类 + 朝向」去挑，
挑中的是该种类在类型清单里的第一个（Filter→F0、DelayGate→D1）。手牌不像 `elements` 那样有
`pass` / `delay` 通道，变体差异只能靠 `type` 表达 —— 编辑器的手牌下拉列的就是类型资产，「过滤器 · F1（PASS 1）」
存下来是 `{ "type": "Type_Filter_F1", "kind": "Filter", "count": 1 }`。

## 最小例子

```json
{
  "displayName": "HELLO",
  "width": 7, "height": 3,
  "wallRows": ["#######", ".......", "#######"],
  "elements": [
    { "kind": "Emitter",  "x": 1, "y": 1, "dir": "Right", "sequence": "1", "count": 3 },
    { "kind": "Receiver", "x": 5, "y": 1, "dir": "Left",  "sequence": "1", "count": 3 }
  ],
  "supplies": [ { "kind": "Filter", "count": 1 } ]
}
```

## 常见错误（都会写进报告，不会崩）

| 现象 | 原因 |
|---|---|
| `elements[i] 解析不出元件类型` | `type` 写错（资产改名/删除），或 `kind` 拼错 |
| `坐标 (x,y) 越界` | 超出 `width`/`height`，这一格被跳过 |
| `坐标重复` | 同一格写了两条，后一条被跳过 |
| `墙层和 elements 有 N 格重叠` | 那几格的墙会被元件盖掉（placements 优先） |
| `将新建类型变体` | `kind` 写法没匹配到现成资产，导入时会新建一份新类型资产（想复用就改 `type`） |
| 模拟器参考 `📝 预设布局未通关` | **不是错误、也不影响出 json**：这一关发了手牌，预设布局本来就该跑不通（跑得通反而说明手牌是摆设）。玩家要自己用牌解 |
| 模拟器参考 `❌ 未通过` | 没发手牌的关跑不通 —— 路由/序列对不上，报告里会指出第几位不匹配。同样不影响 json 能否交付 |

## 菜单

高频的两个动作在窗口里：`SHIFT GO/BitFlow/关卡 JSON 中心`。只有两个页签，
按钮名和菜单项一字不差；窗口本身不含业务逻辑 —— 每个按钮调的就是原来那个方法（日志、弹窗没改）。

| 页签 | 能做什么 |
|---|---|
| 做关 | 打开关卡编辑器（浏览器版）· 导出选中 / 全部 / 空白模板 · 导出类型目录（给外部编辑器）· 校验 JSON（不落盘）· 检查 type 断链 · 导入为新关卡 · 覆盖选中关卡（可 Ctrl+Z 撤回）· 批量导入 incoming 目录 |
| 出包 | **生成内置关卡包** · **打包前自检（关卡）** · 发布前自检：本机分享目录 |

窗口顶部常驻一行 `① 作者源 N 关 · ② 内置包 N 关`，点一下直接打开对应目录。

其余动作都不是「每次做关都要点」的，全部回到菜单栏：

| 菜单项 | 什么时候用 |
|---|---|
| `SHIFT GO/BitFlow/导入 custom_level.json 为关卡资产` | 把游戏内 BUILD MODE 存的草稿转成 LevelSO |
| `SHIFT GO/BitFlow/试跑关卡表` | 按关卡表逐关跑一遍模拟器 |
| `SHIFT GO/BitFlow/试跑所有关卡（扫全盘）` | 不看关卡表，扫全部 LevelSO |
| `SHIFT GO/BitFlow/清理关卡表空引用` | 删掉的关卡在表里留下的洞 |
| `SHIFT GO/BitFlow/清空本轮存档（通关打勾 + 元件首见）` | 自测完想验「新玩家看到的样子」 |
| `SHIFT GO/BitFlow/在资源管理器里显示存档` | 打开存档目录 |
| `SHIFT GO/BitFlow/关卡 JSON/刷新类型注册表` | 改了 / 加了类型资产之后 |
| `SHIFT GO/BitFlow/关卡 JSON/同步到运行时目录` | 工程 JSON → `persistentDataPath` |
| `SHIFT GO/BitFlow/关卡 JSON/打开运行时关卡目录` | 打开 `persistentDataPath/LevelJson` |
| `SHIFT GO/BitFlow/关卡 JSON/一键：刷新注册表并同步` | 新拉一份工程，跑这一个就够 |
| `SHIFT GO/BitFlow/关卡 JSON/迁移旧关卡目录（DefaultCompany → 当前公司名）` | 改过公司名，搬旧日志目录 |
| `SHIFT GO/BitFlow/关卡 JSON/显示关卡目录总览` | 四处目录 + 可写性一张表 |

`生成内置关卡包` / `打包前自检（关卡）` / `打开关卡编辑器（浏览器版）` 在菜单和窗口里各有一个入口：
重复的只是入口，逻辑只有一份。

| 菜单项 | 什么时候用 |
|---|---|
| `SHIFT GO/BitFlow/关卡编辑器` | **做关的主入口**：拉起源码里那份浏览器版编辑器（`Tools/LevelStudio`）。已经编译过就直接起 exe（秒开），源码 / 界面改过才退回 `.bat` 重编一次 |
| `SHIFT GO/BitFlow/关卡 JSON 中心` | 导 JSON、导入、出包前自检 —— 和浏览器版是两条路：这条处理**资产 ↔ JSON**，浏览器版负责**从零摆一关** |

实现：
- `Editor/BitFlow/LevelJsonCenterWindow.cs`（两个页签的窗口；仅调用下面这些方法，不含业务逻辑）
- `Runtime/Data/LevelJsonSerializer.cs`（序列化核心，EditMode 测试覆盖）
- `Editor/BitFlow/LevelJsonTool.cs`（资产解析与菜单）
- `Runtime/Data/LevelTypeRegistrySO.cs` / `LevelTypeRegistryResolver.cs` / `LevelJsonLibrary.cs`（运行时读取，EditMode 测试覆盖）
- `Editor/BitFlow/LevelJsonRuntimeSetup.cs`（注册表刷新与单向同步）
- `Editor/BitFlow/LevelSeedBundler.cs`（作者关卡 → `Resources/BitFlow/Levels`，菜单 + 出包前钩子）
- `Runtime/Data/BundledLevels.cs`（首次启动把内置关卡播进关卡目录，内含覆盖规则，EditMode 测试覆盖）
- `Runtime/Core/LevelDocument.cs` / `LevelDocumentBuilder.cs` / `LevelDocumentWriter.cs` / `LevelTypeCatalog.cs`（纯 C# 数据层，Unity 之外也能编译 —— 见 `docs/LEVEL_STUDIO.md`）
- `Tools/LevelStudio/`（Unity 之外的关卡编辑器：摆元件、看朝向、自查「引擎读不读得懂」、存 JSON —— 见 `docs/LEVEL_STUDIO.md`）

