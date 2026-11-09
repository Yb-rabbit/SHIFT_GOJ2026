# 关卡 JSON（外部手写 ↔ Unity 关卡资产）

`LevelSO` 仍是唯一真源；JSON 只是**双向通道**：导出给人改，改完再导入/覆盖。

```
LevelJson/
├── exported/    Unity 侧导出（含 Level_01.json 样例、catalog.json 顺序表）
├── incoming/    设计侧提交，等「批量导入」
└── reports/     每次校验/导入的诊断（设计侧看不到 Console，看这里）
```

## 流程

1. Unity：`SHIFT GO/BitFlow/关卡 JSON/导出选中关卡` → `LevelJson/exported/*.json`
2. 设计侧：手改 / 手写 JSON，丢进 `LevelJson/incoming/`
3. Unity：`校验 JSON` 看报告 → `导入为新关卡` 或 `覆盖选中关卡`（自动试跑并在报告里给结论）

## 运行时通道（选关面板 / 打包后）

`LevelSO` 仍是**编辑器**侧的真源；打包后的 Player 里没有 `Assets/`，所以运行时另有一条路：

| 角色 | 落点 |
|---|---|
| 运行时关卡目录 | `Application.persistentDataPath/LevelJson/*.json` |
| 类型注册表 | `Assets/Resources/BitFlow/LevelTypeRegistry.asset`（进包，运行时 `Resources.Load`） |

- 选关面板（游戏里按 `L` 打开）的 `MY LEVELS` 分区列出该目录里的关卡；`_` 开头的草稿不列出，读不通的文件只在 Console 报原因
- 面板顶部的 `FILES` 组是玩家侧的文件入口：
  - `IMPORT · FROM FILE` 弹系统文件对话框（Windows：编辑器 + 独立版）挑任意位置的 json；
  - `IMPORT · FROM INBOX` 把 `LevelJson/incoming/`（以及程序旁边 `LevelJson/`）里能读通的 json 一次性导进来；
  - `IMPORT · FROM CLIPBOARD` 走剪贴板原文；
  - `MY LEVELS` 每一行的 `X` 删那一关（两段式确认），`DELETE · CURRENT LEVEL` 删当前载入的那一关；
  - `SHARE FOLDER` / `COPY FOLDER PATH` 打开 / 复制目录（`persistentDataPath` 在 Windows 上是隐藏目录）。
  导入前一律先验货（和进关同一个 `Parse`），读不通的不落盘。详见 `PLAYER_GUIDE.md`
- 点一条 → `LevelFlow.LoadJson` → `LevelJsonLibrary.TryLoad` → 临时 `LevelSO` → `BitFlowRunner.LoadLevel`。
  读盘/解析失败不盖黑幕，停在面板上；临时资产由 `LevelFlow` / 面板负责回收
- 类型解析只查注册表、不碰 `AssetDatabase`，所以**编辑器 Play / EditMode 测试 / 打包 Player 跑的是同一段代码**
- 进关后重开面板、或 `REPLAY` 会重新读盘 → 改了文件立刻生效
- JSON 关卡没有「下一关」（面板里也没有），因为它不属于关卡表顺序

**把工程里的关搬进运行时目录**：菜单 `SHIFT GO/BitFlow/关卡 JSON/同步到运行时目录`
（根目录 `LevelJson/` + `incoming/` 单向复制；`exported/` 不参与，那是导出参照。
同名先到先得、目录里已有文件不删，玩家自己扔的关不会被动掉）。
复制完会用运行时那套代码现场读一遍，读不通的当场在 Console 点名。

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
| `kind` | `Wall` `Emitter` `Receiver` `Splitter` `NotGate` `Filter`，手写用这个 |
| `rot` | `0`~`3` 顺时针 90°/档（只在 `type` 模式下生效） |
| `dir` `dir2` | 绝对朝向 `Up/Right/Down/Left`（也认 `↑→↓←`、`上右下左`）；`dir2` = 分流器的 0 臂 |
| `sequence` | 发射序列 / 接收期望序列。**写了就覆盖类型资产** |
| `count` | 发射总位数 / 接收总个数。写了就覆盖；`0` 是合法值 |
| `face` | 接收器专用：`any` 四面可收 / `in` 只认进料面 |
| `pass` | 过滤器放行值 `0`/`1`（只有 `kind=Filter` 能改） |
| `playerEditable` | `true` = 玩家能搬动/拾回；缺省 `false` = 关卡预设件 |

`type` 和 `kind` 都写时以 `type` 为准（`kind`/`dir` 只是给人看的、以及资产改名后的回退线索）。

## 手牌字段

`{ "kind": "Filter", "count": 3 }`（或 `"type": "Type_Filter_F1"`）。同一类型写多条 = 再多给几张。

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
| 试跑 `❌ 未通过` | 路由/序列对不上，报告里会指出第几位不匹配 |

## 菜单

`SHIFT GO/BitFlow/关卡 JSON/`

导出选中关卡 · 导出全部关卡 · 导出空白模板 · 校验 JSON（不落盘）· 导入为新关卡 · 覆盖选中关卡（可 Ctrl+Z 撤回）· 批量导入 incoming 目录 · 检查 type 断链

运行时（打包后也要能跑的那一半）：

刷新类型注册表 · 同步到运行时目录 · 打开运行时关卡目录 · 一键：刷新注册表并同步 · 导出类型目录（给外部编辑器）· 打开外部关卡编辑器

实现：
- `Runtime/Data/LevelJsonSerializer.cs`（序列化核心，EditMode 测试覆盖）
- `Editor/BitFlow/LevelJsonTool.cs`（资产解析与菜单）
- `Runtime/Data/LevelTypeRegistrySO.cs` / `LevelTypeRegistryResolver.cs` / `LevelJsonLibrary.cs`（运行时读取，EditMode 测试覆盖）
- `Editor/BitFlow/LevelJsonRuntimeSetup.cs`（注册表刷新与单向同步）
- `Runtime/Core/LevelDocument.cs` / `LevelDocumentBuilder.cs` / `LevelDocumentWriter.cs` / `LevelTypeCatalog.cs`（纯 C# 数据层，Unity 之外也能编译 —— 见 `docs/LEVEL_STUDIO.md`）
- `Tools/LevelStudio/`（Unity 之外的关卡编辑器：放元件、看朝向、跑真引擎自测、存 JSON）

