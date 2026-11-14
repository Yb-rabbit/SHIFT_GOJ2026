# ShiftGoLevelLab

离线、双击即用的关卡编辑器。产物是**一个** HTML 文件：

```
Tools/ShiftGoLevelLab/dist/ShiftGoLevelLab.html
```

它把游戏里的位流规则（`Assets/Scripts/Gameplay/BitFlow/Runtime/Core/*.cs`）逐行移植到了 JS，
所以"在这里测通"和"游戏里能过"是同一个结论，不需要打开 Unity。

## 用法

1. 双击 `dist/ShiftGoLevelLab.html`（`file://` 直接打开，无需服务器）。
2. 左侧画笔落件（点在已有元件上只会选中，不会覆写），右侧改参数，点「运行自检」。
3. 时间轴逐 tick 前后回看：第一次出场、第一次销毁、第一次收货都能定位到拍。
4. 舞台下方「清单」列出全关元件与参数；悬停任意格子看坐标，格子自带悬浮说明。
5. 改错了就 `Ctrl+Z` / `Ctrl+Y`（也可用舞台上方按钮），最多 60 步。
6. 「导出 JSON」拿到的文件可以直接放回 `Assets/Resources/BitFlow/Levels/` 或在游戏里导入。

数据自动存进浏览器 localStorage；失败时页面顶部会亮黄条提醒（此时务必手动导出）。

## 目录

| 路径 | 说明 |
| --- | --- |
| `src/engine/dir.js` | `Dir` / `DirUtil`：方向、旋转、箭头字符 |
| `src/engine/document.js` | `LevelDocumentBuilder`：JSON → 关卡（越界/叠格/type-vs-kind/墙层/手牌） |
| `src/engine/simulation.js` | `BitFlowSimulation`：四阶段 tick、延迟器队列、冲突与销毁 |
| `src/engine/check.js` | `LevelCheck`：跑到底的报告文案 + 存档前自检（Blocking / Advisory） |
| `src/engine/catalog.js` | 类型目录（抄自 Unity 导出的 `typeCatalog.json`） |
| `src/samples.js` | 内置样关：ONE_ONLY、WAIT111（与 Resources 下文件同内容）+ NOT LAB |
| `src/selftest.js` | 启动自检 6 条硬断言，结果显示在页脚 |
| `src/grid.js` / `src/timeline.js` | DOM 棋盘舞台（格子 + 位 + 事件高亮）/ 帧序列与播放器 |
| `src/ui.js` | 编辑器交互（画笔 / 选择工具 / 撤销重做 / 清单）、自检面板、导入导出、本地存储 |
| `src/docs.js` | 帮助抽屉的全部文案（单文件不能读 md，所以内联） |
| `build.ps1` | 内联 CSS + 11 个脚本 → `dist/ShiftGoLevelLab.html` |

## 构建

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

脚本会检查：`</script` 不会截断内联、产物里没有 `<script src>` / `href` / `fetch(` / `import ... from`、
输出为 UTF-8 无 BOM。页脚会印构建时间与自检结果。

> `build.ps1` 自身必须带 BOM 保存（否则 PowerShell 5.1 会按 ANSI 读中文，把后面的引号当成汉字的一部分）。

## 与游戏引擎对拍

```powershell
# 1) C# 侧：把游戏同一份 Core 源文件编进控制台程序，导出 expected-csharp.json
cd tests\parity-csharp
dotnet run -c Release

# 2) JS 侧：跑同一批 fixture，并逐字段比对
cd ..\..
node tests\parity.js --check
```

比对内容：元件定义（含按 (y,x) 发 Id 的顺序）、读档问题文案、tick 数、是否结算、
每个接收器的收料与逐位匹配、自检意见、报告全文（含 ✅ / 📝 / ❌ 措辞）。

`tests/parity-csharp/ShiftGoParity.csproj` 用 `Compile Include` **只读**引用 Core 的 `*.cs`，
不复制、不修改 Unity 资产。当前 9 个 fixture 全部一致。

## 两条硬约束

- **不能有 ES module / fetch**：`file://` 下 module 会被 CORS 拦，fetch 也读不到同目录文件。
  所有脚本都是经典 `<script>`，按顺序内联。新增脚本时记得同时改 `build.ps1` 的 `$scriptFiles`。
- **引擎语义不许"顺便优化"**：四阶段顺序、延迟器 `T+1+N` 放行、链式阻塞、对穿判定、`count=0` 跳过……
  任何一条改动都必须先改 fixture 再跑对拍，否则就会出现"编辑器能过、游戏过不了"。
