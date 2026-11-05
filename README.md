# SHIFT_GO2026

基于 **Unity 2022.3.36f1（URP 14.0.11）** 的作品项目，目前处于**机制原型阶段**（Game Off "SHIFT" 主题）。

## 环境

- Unity Editor `2022.3.36f1`
- 渲染管线：URP 14.0.11（`Assets/Settings/URP_Asset.asset`，全部质量档已绑定）
- 后处理：全局 Volume `Assets/Settings/URP_GlobalVolumeProfile.asset`（Bloom / ACES / Vignette）
- 旧版 InputManager（未启用 Input System Package）
- UI：uGUI + 内置 `LegacyRuntime.ttf`（**不依赖 TextMeshPro**）
- 目标平台：Windows Standalone

## 目录

```
Assets/
├── Arts/       美术资源（Characters / Environment / Props / UI / Materials / Textures / Animations / Effects）
├── Audio/      音频（BGM / SFX / Voice）
├── Data/       关卡与元件数据（Elements/ 元件类型、Levels/ 关卡摆放）
├── Editor/     编辑器工具（ProjectSetup / BitFlowAssetBuilder）
├── Scenes/     场景
├── Settings/   URP 管线资产与 Volume Profile
└── Scripts/    代码（Core / Data / Gameplay / Managers / Player / UI）
```

## 当前测试内容

| 场景 | 内容 |
|---|---|
| `Scenes/Test_2_BitFlow.unity` | **位流路由演示**：9×8 网格，2 个发射器、L 分流器、非门、过滤器、3 个接收器，按 tick 自动推演 |
| `Scenes/Test_1_PushBox.unity` | 推箱子网格步进测试（原 `PushBoxTest`）：7×7 关卡，蓝色=玩家、棕色=箱子、绿色=目标 |
| `Scenes/SampleScene.unity` | 默认场景，仅用于验证工程与工具链 |

### 位流路由（Bit Flow）

- 每个 tick 三步：①发射器弹栈吐位 ②所有位朝自身朝向前进 1 格 ③元件裁决（分流/取反/过滤/销毁）
- 位只会沿朝向自动前进；撞墙、出界、从接收器非进料面撞入、逆行撞入元件出口 → 销毁红闪
- 两个位同 tick 抢同一格（或对穿）→ 双双销毁红闪（时序谜题的设计资源）
- 接收器：逐位比对 + 累计计数，全部接收器都恰好收满且每一位都正确才算通关
- 操作：底部 `PAUSE / STEP / REPLAY` + 倍速滑条；鼠标悬停元件可看该元件的配置说明

代码分层（命名空间统一 `ShiftGo.*`）：

| 命名空间 | 位置 | 职责 |
|---|---|---|
| `ShiftGo.BitFlow.Core` | `Scripts/Gameplay/BitFlow/Runtime/Core` | 纯 C# 规则内核（`BitFlowSimulation` / `TickReport` / `LevelDefinition`），零 Unity 依赖 |
| `ShiftGo.BitFlow.Data` | `Scripts/Gameplay/BitFlow/Runtime/Data` | ScriptableObject：元件类型 + 关卡摆放（运行时只读） |
| `ShiftGo.BitFlow.View` | `Scripts/Gameplay/BitFlow/Runtime/View` | 表现层：场景搭建、位插值、元件反馈、tick 驱动、固定机位、HUD |
| `ShiftGo.BitFlow.Tests` | `Scripts/Gameplay/BitFlow/Tests/EditMode` | EditMode 单元测试（规则内核） |
| `ShiftGo.PushBox` | `Scripts/Gameplay/PushBox` | 旧的推箱子步进测试 |

### 编辑器工具

菜单 `SHIFT GO/Setup/`：

| 命令 | 作用 |
|---|---|
| `全部执行（URP 切换 + 场景改名 + 材质升级）` | 生成 URP 管线资产、绑定质量档、生成 Volume Profile、旧材质升级到 URP/Lit、旧场景改名 |
| `生成关卡资产 + 演示场景` | 生成 `Assets/Data/*` 与 `Scenes/Test_2_BitFlow.unity` |

命令行等价写法：

```bat
Unity.exe -batchmode -quit -projectPath "<项目根>" ^
  -executeMethod ShiftGo.EditorTools.ProjectSetup.RunAll -logFile "<日志>"
Unity.exe -batchmode -quit -projectPath "<项目根>" ^
  -executeMethod ShiftGo.EditorTools.BitFlowAssetBuilder.BuildAll -logFile "<日志>"
```

跑 EditMode 单元测试：

```bat
Unity.exe -batchmode -quit -projectPath "<项目根>" ^
  -runTests -testPlatform EditMode -testResults "<结果 xml>" -logFile "<日志>"
```

## 约定

- `.meta` 文件必须与资源一起提交；`Library/`、`Temp/`、`obj/`、`*.csproj`、`*.sln` 不入库。
- 提交信息格式：`【标签】简述`，例：`【立项】初始化仓库`、`【测试】位流路由演示`。
- 同一时间只由一人编辑同一个场景，避免 `.unity` 文件冲突。
- **运行时不写回 ScriptableObject**：进度、锁定状态一律放在内存里，避免编辑器里资产被改脏。

