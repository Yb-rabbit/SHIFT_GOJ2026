# 关卡从 JSON 到玩家手里：最短路径

一句话：**JSON 只认工程根的 `LevelJson/`；`生成内置关卡包` 把它抄进
`Assets/Resources/BitFlow/Levels/`，出包才带得上。**

做关的入口是 Unity 菜单 `SHIFT GO/BitFlow/关卡编辑器` —— 一个跑在浏览器里的编辑器
（摆在 `Tools/LevelStudio`）。它只产出一份 json，摆完存到 `LevelJson/incoming/` 就进下面的流程：
见 **[LEVEL_STUDIO.md](LEVEL_STUDIO.md)**。Unity 内置的网格窗口还在，但只在选中某份
`LevelSO` 时从检视器上的按钮进去（改资产本身，不经过 json）。

## 1. 文件放哪

| 你在做什么 | 放这里 | 会进包吗 |
|---|---|---|
| 设计侧交上来的关 | `LevelJson/incoming/*.json` | ✅ |
| 你自己在做的关 | `LevelJson/*.json`（根目录） | ✅ |
| Unity 导出的参照样例 | `LevelJson/exported/*.json` | ❌ 只是参照 |
| 运行时目录 `%AppData%Low/ShiftGo/SHIFT_GO2026/LevelJson/` | 玩家机本地 / 自测 | ❌ **永远不进包** |

> `exported/` 是导出参照（「导出全部关卡」还会写 `catalog.json` 顺序表），**不参与打包**。
> 想让它变成玩家能玩到的关：把 json 复制到 `LevelJson/` 根目录或 `incoming/`，再跑一次打包。

**最容易踩的坑**：游戏里 `MY LEVELS` 看到的关在 AppData —— 那是玩家机目录，不是打包输入。
要进包，必须先有一份**工程里的** JSON。

## 2. 打包

`SHIFT GO/BitFlow/关卡 JSON 中心` → 「出包」页 → `生成内置关卡包`
（也可以直接走菜单 `SHIFT GO/BitFlow/关卡 JSON/生成内置关卡包`）

- 出包前钩子（`IPreprocessBuildWithReport`）会自动再跑一遍，忘了点也不会打出旧包；
- 但它只扫 `LevelJson/` + `LevelJson/incoming/` —— **别处的 JSON 一个都收不到**；
- 对话框里的「N 关」就是这一版进包的关数，数字不对就是源目录不对。

## 3. 验证（不用信日志）

- `SHIFT GO/BitFlow/关卡 JSON 中心` → 「出包」页 → `打包前自检（关卡）`：源 vs 包的差集，
  外加 `Builds/` 里已出包产物的 `resources.assets` 逐关点名；
- 或者手工一条命令（把 `ONE_ONLY` 换成关卡名）：

```powershell
$enc=[System.Text.Encoding]::GetEncoding(28591)
$t=$enc.GetString([System.IO.File]::ReadAllBytes('<包>\SHIFT_GO2026_Data\resources.assets'))
$t.Contains('ONE_ONLY')   # True = 在包里
```

包里翻不到散装 `.json` 是正常的：它们被编进 `resources.assets`，**首次启动**由
`BundledLevels` 写进 AppData 才变成文件。Burst 调试目录（`*_BurstDebugInformation_DoNotShip`）与关卡无关。

## 4. 报告里的 ❌ 别慌

`试跑：📝 预设布局未通关（有手牌的关，这是预期）` = 题目要玩家用牌解，数据没问题。
只有**没发手牌**的关才会出现 `❌ 未通过` —— 那才是真的跑不通。

## 相关

- 字段 / 手牌 / 覆盖规则：`LEVEL_JSON.md`
- 玩家侧（分享目录、导入）：`PLAYER_GUIDE.md`
- 为什么"有手牌 = 预设布局必须跑不通"：`LevelCheck.RunForSave` 的注释
