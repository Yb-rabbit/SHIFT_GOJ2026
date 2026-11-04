# SHIFT_GO2026

基于 **Unity 2022.3.36f1（Built-in 渲染管线）** 的游戏项目，目前处于**立项与机制测试阶段**。

## 环境

- Unity Editor `2022.3.36f1`
- 旧版 InputManager（未启用 Input System Package）
- 目标平台：Windows Standalone

## 目录

```
Assets/
├── Arts/       美术资源（Characters / Environment / Props / UI / Materials / Textures / Animations / Effects）
├── Audio/      音频（BGM / SFX / Voice）
├── Scenes/     场景
└── Scripts/    代码（Core / Data / Gameplay / Managers / Player / UI）
```

## 当前测试内容

| 场景 | 内容 |
|---|---|
| `Scenes/SampleScene.unity` | 默认场景 + 一个 Cube，用于验证工程与工具链正常 |
| `Scenes/PushBoxTest.unity` | 推箱子网格步进测试：Cube 搭的 7×7 关卡，蓝色=玩家、棕色=箱子、绿色=目标 |

`PushBoxTest` 操作：`W/A/S/D` 或方向键，**一次走一格**；撞墙不动，推箱子时箱子前方必须是空的。
相关脚本：`Scripts/Gameplay/PushBox/GridStepper.cs`、`Pushable.cs`。

## 约定

- `.meta` 文件必须与资源一起提交；`Library/`、`Temp/`、`obj/`、`*.csproj`、`*.sln` 不入库。
- 提交信息格式：`【标签】简述`，例：`【立项】初始化仓库`、`【测试】推箱子000A`。
- 同一时间只由一人编辑同一个场景，避免 `.unity` 文件冲突。
