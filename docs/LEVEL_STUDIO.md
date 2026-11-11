# 外部关卡编辑器（Tools/LevelStudio）

一个跑在浏览器里的 BitFlow 关卡编辑器：**放元件 → 看朝向 → 自测可解 → 存 JSON**，全程不需要开 Unity。

## 为什么是这个形状

「自测可解」必须跑**游戏里那份**规则，否则迟早出现「编辑器说能过、游戏里过不了」。

所以这个工具不是重写一份 JS 引擎，而是把自己的 .NET 工程直接**链接编译** `Runtime/Core/*.cs`：

```
Tools/LevelStudio/ShiftGoLevelStudio.csproj
  └─ <Compile Include="..\..\Assets\Scripts\Gameplay\BitFlow\Runtime\Core\*.cs" />
```

于是：

- 校验走的是 `LevelCheck.Run` + `BitFlowSimulation`，和 Unity 里**同一份源码**；
- JSON → `LevelDefinition` 走的是 `LevelDocumentBuilder`，和 Unity 侧 `LevelJsonSerializer.Read` + `LevelSO.ToDefinition` 逐条对齐；
- 没有第二套规则要维护。

代价是 `Runtime/Core/` 必须保持**零 UnityEngine 依赖**（现在就是）。哪天有人在 Core 里加了 `using UnityEngine;`，这个工具会立刻编译失败 —— 这是有意的护栏。

## 启动

双击 `Tools/LevelStudio/启动关卡编辑器.bat`，或者 Unity 菜单
`SHIFT GO/BitFlow/关卡 JSON/打开外部关卡编辑器`（会先顺手把类型目录刷一遍）。

需要 .NET SDK 8+（命令行 `dotnet --version` 能看到版本即可）。启动后浏览器会自动打开
`http://127.0.0.1:5199/`（端口占用会往后找 5199~5209）。**关掉那个黑窗口 = 关掉编辑器。**

## 界面

| 区域 | 用途 |
|---|---|
| 左栏「元件」 | 选画笔：发射器 / 接收器 / 分流器 / 非门 / 过滤器 / 墙 |
| 中间网格 | **左键放、右键擦**；格子四边的箭头就是位会走的方向；数字是「发射位数 / 需要收到的个数」 |
| 右栏「选中格子」 | 改朝向（四个方向键）、序列、数量、收位面、放行值、玩家可动 |
| 右栏「自测」 | 跑一遍真引擎，直接告诉你通没通关、几个 tick、卡在哪一位 |
| 右栏「文件」 | 读 / 存关卡、导出 / 粘贴 JSON、打开目录 |

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

`LevelJson/exported/typeCatalog.json` 由 Unity 菜单 `关卡 JSON/导出类型目录（给外部编辑器）` 生成，
内容来自每个类型资产真实的 `CreateDefinition(...)`（不是另抄一份默认值）。

- 改了 / 新增了类型资产 → 回 Unity 再导一次；
- `LevelDocumentBuilderTests.ExportedCatalogFile_MatchesLiveTypeAssets` 会在 EditMode 测试里
  比对「快照 vs 现状」，忘了导就会红。

没导出过快照也能用：`kind` 模式不依赖它，只有「打开 `type` 模式的关」才需要。

## 玩家怎么玩到自己做的关

1. 编辑器里点「保存」，目录选 `LevelJson/incoming`（工程里的中转站）；
2. 回 Unity 跑菜单 `同步到运行时目录`（或一键那个）；
   打包后的玩家则**直接点 `IMPORT · FROM FILE` 挑那个 json**（Windows 系统文件对话框），
   或者把 json 丢进 `SHARE FOLDER`（程序旁边 `LevelJson\share\`）再点 `IMPORT · FROM SHARE FOLDER · BATCH IMPORT`；
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
- 工具自己：`dotnet build` 就是它的整条编译检查；改完 UI 用浏览器实测一遍自测按钮。

## 两个已经踩到的坑（改这个工具时别踩回去）

1. **`System.Text.Json` 默认不序列化字段。** `LevelDocument` / `LevelTypeCatalog` 的成员全是 public 字段
   （这样 Unity 的 `JsonUtility` 才能直读），所以工具侧两份 `JsonSerializerOptions` 都必须写
   `IncludeFields = true`。漏了不会报错，只会让 `/api/catalog` 的兜底路径悄悄少掉整个 `types`。
2. **`kind` 模式的默认值必须取自 `EditableElement.CreateDefault`**，不能拿类型目录里「同种类的第一份资产」当默认。
   否则副臂朝向、放行值这些会跟着工程里排第一的那个资产走，编辑器和游戏就跑出两种结果。

