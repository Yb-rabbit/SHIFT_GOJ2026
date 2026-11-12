using System.Collections.Generic;
using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 手牌卡面的贴图库：把 <see cref="CellKind"/> 映射到 <c>Assets/Arts/Resources/Cards/*.png</c>。
    ///
    /// 卡面本身是完整的 500×500 设计稿（图标、名字、副标题都画在图上，底部留了一条浅灰槽给状态/张数），
    /// 所以 <see cref="BitFlowCardTray"/> 直接用整张图当卡面、只往那条槽里写张数，不再叠第二份名字。
    /// 找不到贴图就返回 null，手牌会退回原来的纯色卡片，所以美术资源缺失不会让 UI 直接崩掉。
    /// </summary>
    public static class CardArtLibrary
    {
        const string CardFolder = "Cards/";

        static readonly Dictionary<CellKind, Sprite> cache = new Dictionary<CellKind, Sprite>();
        static readonly HashSet<CellKind> missing = new HashSet<CellKind>();

        /// <summary>取某种元件对应的卡面；没有对应资源时返回 null。</summary>
        public static Sprite Icon(CellKind kind)
        {
            if (cache.TryGetValue(kind, out Sprite cached)) return cached;
            if (missing.Contains(kind)) return null;

            string fileName = FileNameOf(kind);
            Sprite sprite = string.IsNullOrEmpty(fileName) ? null : Resources.Load<Sprite>(CardFolder + fileName);
            if (sprite == null)
            {
                missing.Add(kind);
                return null;
            }

            cache[kind] = sprite;
            return sprite;
        }

        /// <summary>卡面文件名（不含扩展名）；没有美术的元件返回空字符串。</summary>
        static string FileNameOf(CellKind kind)
        {
            switch (kind)
            {
                case CellKind.Emitter: return "Card_EMIT";
                case CellKind.Receiver: return "Card_Rece";
                case CellKind.Splitter: return "Card_Split";
                case CellKind.NotGate: return "Card_NoGate";
                case CellKind.Filter: return "Card_filt";
                case CellKind.Wall: return "Card_Wall";
                case CellKind.DelayGate: return "Card_Delay";
                default: return string.Empty;
            }
        }
    }
}
