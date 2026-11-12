using System.Text;

namespace ShiftGo.BitFlow.Core
{
    /// <summary>
    /// 关卡名的唯一规则：<b>displayName 是名字，文件名由它派生</b>，两者不允许各自自由填写。
    ///
    /// 为什么要单独一个类：这条规则原本只活在 <c>BitFlowWorkshop</c> 里，而外部编辑器
    /// （<c>Tools/LevelStudio</c>）是另一种语言写的界面。两边各写一份的下场已经出现过一次 ——
    /// 编辑器存出 <c>MY_LEVEL_02_01.json</c>、游戏里这关却叫 <c>111</c>，同一关两个名字。
    /// 放进 Core 之后，<c>ShiftGoLevelStudio.csproj</c> 会把它和 <c>LevelCheck</c> 一起编进外部编辑器，
    /// 双方跑的是同一段代码，不存在漂移。
    ///
    /// 约束（照抄原有规则，一个字都没改）：只留 ASCII 的 <c>A-Z 0-9 - _</c> 与空格，自动大写、
    /// 折叠连续空格、最长 <see cref="MaxLength"/> 字符。像素字体只有 ASCII 字形，中文名在面板上
    /// 会是一排方块；文件名也只由这份结果生成，所以这里同时挡掉路径分隔符与平台非法字符。
    ///
    /// 本文件必须保持零 UnityEngine 依赖（见 csproj 顶部注释）。
    /// </summary>
    public static class LevelNaming
    {
        /// <summary>关卡名最长多少字符。超出的部分直接截断，不报错。</summary>
        public const int MaxLength = 20;

        /// <summary>名字为空时用的兜底名。文件名与面板显示都用它。</summary>
        public const string Fallback = "CUSTOM LEVEL";

        /// <summary>提交用的清理：折叠连续空格、去掉首尾空格。</summary>
        public static string Sanitize(string raw)
        {
            return Sanitize(raw, true);
        }

        /// <summary>
        /// 输入框逐字清理：同样只留 ASCII / 大写 / 上限 20 字，但<b>不折叠空格</b> ——
        /// 否则作者打到一半的两个空格会被吞掉，光标跟着跳。
        /// </summary>
        public static string SanitizeTyped(string raw)
        {
            return Sanitize(raw, false);
        }

        /// <summary>
        /// 名字 → 文件名（不含扩展名）：<c>"MY LEVEL 07" → "MY_LEVEL_07"</c>。
        /// 空格换下划线，方便玩家手打路径；空名字退回 <see cref="Fallback"/>。
        /// </summary>
        public static string BaseNameOf(string displayName)
        {
            string safe = Sanitize(displayName);
            if (string.IsNullOrEmpty(safe)) safe = Fallback;
            return safe.Replace(' ', '_');
        }

        /// <summary>名字 → 文件名：<c>"MY LEVEL 07" → "MY_LEVEL_07.json"</c>。</summary>
        public static string FileNameOf(string displayName)
        {
            return BaseNameOf(displayName) + ".json";
        }

        /// <summary>
        /// 真正的实现。<paramref name="finish"/> 为 false 时保留刚敲下的空格（输入过程中用），
        /// 为 true 时折叠并去掉首尾（提交 / 生成文件名时用）。
        /// </summary>
        public static string Sanitize(string raw, bool finish)
        {
            if (string.IsNullOrEmpty(raw)) return string.Empty;

            StringBuilder builder = new StringBuilder(raw.Length);
            bool lastWasSpace = false;

            foreach (char source in raw)
            {
                char c = char.ToUpperInvariant(source);

                bool allowed = (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '-' || c == '_' || c == ' ';
                if (!allowed) continue;

                if (c == ' ')
                {
                    // 输入过程中保留玩家刚敲下的空格；提交 / 生成文件名时折叠并去掉首尾
                    if (finish && (lastWasSpace || builder.Length == 0)) continue;
                    lastWasSpace = true;
                }
                else
                {
                    lastWasSpace = false;
                }

                builder.Append(c);
                if (builder.Length >= MaxLength) break;
            }

            string result = builder.ToString();
            return finish ? result.Trim() : result;
        }
    }
}
