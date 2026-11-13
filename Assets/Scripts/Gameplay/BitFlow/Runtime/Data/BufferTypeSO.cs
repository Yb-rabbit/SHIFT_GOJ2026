using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// BUFFER 卡（B）：把一堵<b>关卡预设件</b>收回手牌。
    ///
    /// 为什么需要这张牌：玩家自己放的件，右键（<c>LevelDraft.TryPick</c>）就能免费拾回；
    /// 但作者用 wallMap / <c>playerEditable=false</c> 摆的预设件是「题目的一部分」，
    /// 玩家永远搬不动。BUFFER 就是唯一能打开那一层的钥匙：用掉一张牌，换回那件本身。
    ///
    /// 三条硬边界（都在 <c>LevelDraft.TryRetract</c> 里判定，不在这里）：
    /// - 只对<b>锁定</b>格生效 —— 自己的件用右键，别浪费一张牌；
    /// - 只对 <see cref="SupplyKinds.IsSupplyKind"/> 的种类生效 ——
    ///   发射器/接收器是「定义关卡」的件，谁也不能把它收走，否则玩家等于自己改题；
    /// - 用一张少一张（<c>supplies</c> 里发几张，这一关就能动几件预设件）。
    ///
    /// 它没有自己的元件外观：<see cref="CreateDefinition"/> 借墙的形态，只为让
    /// <c>LevelTypeRegistrySO.KindOf</c> 有一个合法种类（存档自检、卡排去重都要用它）。
    /// 卡面由 <see cref="CardArt"/> 单独指定，不跟着墙走。
    /// </summary>
    [CreateAssetMenu(menuName = "SHIFT GO/Elements/Buffer", fileName = "Type_Buffer")]
    public sealed class BufferTypeSO : ElementTypeSO
    {
        /// <summary>工具卡：点格子 = 收回那一格，而不是往那一格放东西。</summary>
        public override bool IsTool => true;

        /// <summary>卡面：<c>Assets/Arts/Resources/Cards/Card_Buffer.png</c>。</summary>
        public override string CardArt => "Card_Buffer";

        /// <summary>
        /// 借墙的形态。用墙而不是自造一种 <see cref="CellKind"/>：
        /// 新增一种 Kind 要改模拟器、渲染、存档、注册表五处，而 BUFFER 根本不进棋盘 —— 它只活在手牌里。
        /// 用墙的另一个好处是存档自检（<c>SupplyKinds</c>）天然放行，不会把这张牌当成「发错的发射器」。
        /// </summary>
        public override ElementDefinition CreateDefinition(GridPoint coord, int rotationSteps, int id)
        {
            return new WallDefinition { Id = id, Coord = coord };
        }

        // 屏上提示只用 ASCII 大写：位图字体没有中文字形（见 View/PixelFont）
        public override string Describe()
        {
            return "BUFFER  TAKE ONE FIXED PIECE BACK TO HAND";
        }
    }
}
