using System;
using System.Collections.Generic;

namespace ShiftGo.BitFlow.Core
{
    /// <summary>
    /// 关卡 JSON 的内存形态（纯 C#、零 UnityEngine 依赖）。
    ///
    /// 存在的理由：这段数据要被**两个地方**同时使用 ——
    /// 一是 Unity 里的导入/运行时读取，二是 Unity 之外的关卡编辑器
    /// （<c>Tools/LevelStudio</c>，直接把 Core 的源文件编进自己的工程）。
    /// 只要两边都走 <see cref="LevelDocumentBuilder"/>，外部编辑器里「测试可解」跑的就是
    /// 玩家实际会跑的那一份规则，不存在「编辑器说能过、游戏里过不了」。
    ///
    /// 字段名与 docs/LEVEL_JSON.md 一一对应（camelCase，JsonUtility 可以直读，
    /// System.Text.Json 配 IncludeFields 也能直读）。
    /// </summary>
    [Serializable]
    public sealed class LevelDocument
    {
        /// <summary>当前格式版本。字段只增不改，旧文件缺字段就用这里的默认值。</summary>
        public const int CurrentSchema = 2;

        public int schema = CurrentSchema;

        public string displayName = string.Empty;

        public int width = 9;
        public int height = 8;

        /// <summary>一个 tick 的墙钟时长（秒）。</summary>
        public float tickInterval = 0.35f;

        /// <summary>墙类型资产 Key（留空 = 用目录里第一份墙类型）。</summary>
        public string wallType = string.Empty;

        /// <summary>墙字符画：一行一个 y（第一行是 y=0，从下往上），'#' 是墙。</summary>
        public string[] wallRows;

        /// <summary>旧写法（\n 分行）。只在没有 wallRows 时生效。</summary>
        public string wallMap = string.Empty;

        public List<LevelDocumentCell> elements = new List<LevelDocumentCell>();

        public List<LevelDocumentSupply> supplies = new List<LevelDocumentSupply>();

        /// <summary>
        /// schema 2 起：这一关允许玩家进关后直接编辑（沙盒关）。
        /// 缺省 false = 玩家只能玩作者摆好的样子（旧文件不受影响）。
        /// </summary>
        public bool allowBuild;

        public int SafeWidth => width > 0 ? width : 1;
        public int SafeHeight => height > 0 ? height : 1;
    }

    /// <summary>一格元件。数值字段 -1 / 字符串空串表示「没写」（跟随类型资产或默认值）。</summary>
    [Serializable]
    public sealed class LevelDocumentCell
    {
        /// <summary>精确模式：类型资产 Key。</summary>
        public string type = string.Empty;

        /// <summary>宽松模式：Wall / Emitter / Receiver / Splitter / NotGate / Filter。</summary>
        public string kind = string.Empty;

        public int x;
        public int y;

        /// <summary>整体旋转 0~3（顺时针 90°/档）。<c>type</c> 模式靠它定位朝向。</summary>
        public int rot;

        /// <summary>绝对朝向 Up / Right / Down / Left（也认 ↑→↓← 与 上右下左）。</summary>
        public string dir = string.Empty;

        /// <summary>分流器的 0-臂。</summary>
        public string dir2 = string.Empty;

        /// <summary>发射序列 / 接收期望序列。出现即覆盖。</summary>
        public string sequence = string.Empty;

        /// <summary>发射总位数 / 接收总个数。-1 = 没写。</summary>
        public int count = -1;

        /// <summary>接收器专用：any = 四面可收，in = 只认进料面。空串 = 没写。</summary>
        public string face = string.Empty;

        /// <summary>过滤器放行值（0/1）。-1 = 没写。</summary>
        public int pass = -1;

        /// <summary>延迟器的延迟 tick 数（1~4）。-1 = 没写。</summary>
        public int delay = -1;

        /// <summary>允许玩家搬动 / 拾回这一格。</summary>
        public bool playerEditable;
    }

    /// <summary>一摞手牌。同一类型写多条 = 再多给几张。</summary>
    [Serializable]
    public sealed class LevelDocumentSupply
    {
        public string type = string.Empty;
        public string kind = string.Empty;

        /// <summary>本关给几张。-1 = 没写 = 1 张。</summary>
        public int count = -1;
    }
}
