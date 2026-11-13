using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 玩家手牌盘（待排区）：把 <see cref="LevelSO.supplies"/> 变成一排"躺在网格前方地上的卡"，
    /// 鼠标靠近就把那张卡拿起来贴到镜头前读说明，点卡选中 → 卡缩进屏幕底带继续跟着鼠标 →
    /// 鼠标扫过哪一格，就在那一格显示半透明的同款元件（能放=类型色，不能放=红），点一下落下。
    ///
    /// 为什么做成场景物件而不是底部 UI 条：
    /// - 卡面本来就是 500×500 的设计稿（图标、名字、说明都在图里），直接贴在地上就是一张实体牌，
    ///   以后换说明 = 换 Resources/Cards 里的图，不用动任何布局代码；
    /// - 屏幕底部那条 138px 的 UI 带可以整条让出来，网格不再被压着；
    /// - 命中判定变成"射线 → 卡片矩形"的纯数学，不再依赖 EventSystem 的 UI 遮挡状态。
    ///
    /// 几何是算出来的，不是随手摆的（固定机位 (4,10.6,-9)、FOV 42 下，卡排那个深度上 1 世界单位 ≈ 113px）：
    /// - 卡平躺时必须沿 z 拉长 <see cref="CardLengthFactor"/> 倍，屏幕上才接近正方形、文字才不被压扁；
    /// - 卡排占屏幕 y 53..157px，而 9×8 最前面一排格子从 188px 开始 —— 所以它一点格子都不挡（留 31px 余量）；
    /// - 平躺时一张卡只有约 100px，说明读不清，所以鼠标一靠近就"拿起来"读（<see cref="LiftPosition"/>，210px）；
    /// - 选中以后换成"拿在手里"（<see cref="HeldPosition"/>，149px 沉在底带）—— 挑格子时卡只会挪到鼠标下方，
    ///   绝不压在格子上，落点全部交给 <see cref="PlacementGhost"/> 的幽灵预览。
    ///
    /// 交互：左键 = 拿卡 / 放卡 ｜ 右键 = 拾回 或 放下手里的卡 ｜
    /// R = 转待放朝向或转鼠标下的件 ｜ Ctrl+Z = 撤销 ｜ ESC = 放下手里的卡。
    ///
    /// 工坊里左键的空闲语义不一样：手里没牌时，左键点已有元件 = <b>选中</b>并打开参数小窗
    /// （作者要改发射序列 / 位数，不能一点就把件搬走），右键才是拾回。
    /// 正式关保持原样：手里没牌时左键点自己的元件 = 拿起来重新摆。
    /// </summary>
    public sealed class BitFlowCardTray : MonoBehaviour
    {
        static readonly Color TextColor = new Color(0.85f, 0.92f, 1f);
        static readonly Color AccentColor = new Color(0.35f, 0.85f, 1f);
        static readonly Color MutedColor = new Color(0.55f, 0.62f, 0.72f);
        static readonly Color EmptyTint = new Color(0.40f, 0.40f, 0.40f, 1f);
        static readonly Color CountTextColor = new Color(0.08f, 0.11f, 0.18f);
        static readonly Color CountTextEmptyColor = new Color(0.42f, 0.45f, 0.52f);

        /// <summary>所有"按像素"写的常量都以 1080 高为基准，实际用时乘 <c>Screen.height / 1080</c>。</summary>
        const float ReferenceHeight = 1080f;

        // ---------------------------------------------------------------- 待排区几何（世界单位 = 格）

        /// <summary>卡面离地一点，避免和地板 z-fighting。</summary>
        const float RowY = 0.02f;

        /// <summary>一张卡的宽度。0.95 格 → 屏幕约 100px，和原来 104px 的 UI 卡几乎一样大。</summary>
        const float CardWidth = 0.95f;

        /// <summary>平躺时沿 z 的拉长倍数。1/sin(37°) ≈ 1.66 是屏幕完全不变形的理想值，
        /// 取 1.15 是因为卡排还要塞进前排格线以下（屏幕 55..156px），拉太长会顶到格子上。</summary>
        const float CardLengthFactor = 1.15f;

        const float CardGap = 0.12f;

        /// <summary>卡排最靠玩家的一端（世界 z）。比前排格线 -0.5 更靠外，才不压到格子。</summary>
        const float RowFrontZ = -0.86f;

        /// <summary>鼠标离卡边多远也算"靠近"（世界单位）。</summary>
        const float HoverMargin = 0.06f;

        // ---------------------------------------------------------------- 拿起（像素，按 1080 归一）

        /// <summary>
        /// 举起来（悬停读说明 / 选中拿着）时，卡面在屏幕上的整体放大倍数。
        /// 只乘在这两个状态的像素尺寸上（<see cref="LiftSizePx"/> / <see cref="HeldSizePx"/> 及其高度），
        /// 平躺在待排区的卡不受影响 —— 那一排要和格子保持"一格一张卡"的比例关系。
        ///
        /// 1.5 的代价：选中时那张卡从 149px 长到 224px，而屏幕底部留给它的空带只有 188px，
        /// 所以它会压住网格最前面一排 50~60px（一格出头）。落点始终由格子上的幽灵预览指示，
        /// 想换回"一格不挡"就把这里调回 1。
        /// </summary>
        const float CardFaceScale = 1.5f;

        /// <summary>
        /// 拿起来时卡离相机多远（米）。<b>这个值是"手持感"的关键</b>：卡在屏幕上多大是由像素常量
        /// （<see cref="LiftSizePx"/> / <see cref="HeldSizePx"/>）定死的，距离只决定它在世界里有多大 ——
        /// 距离越远，鼠标横移时卡就在场景里扫得越远，看起来像"漂移"而不是"被手举着"。
        ///
        /// 早先是反过来按"卡自己的世界尺寸"算距离的：1 格卡（0.95m）算出 9m、两格卡（1.9m）算出 18m。
        /// 屏幕上看一样大，但鼠标从屏幕左边划到右边时，卡在世界里要横穿 12~24 米 ——
        /// 网格才 9 米宽、相机到网格中心也才 12.6 米，所以宽卡会直接飘到网格后面被挡住。
        /// 钉死在镜头前以后，同样的鼠标行程只走 2 米出头，而且不论几格宽都一样。
        /// </summary>
        const float LiftDistance = 2.6f;

        /// <summary>已经拿到手里、正在挑格子时离相机多远（米）。比 <see cref="LiftDistance"/> 更近一点，更像捏在指尖。</summary>
        const float HeldDistance = 1.8f;

        /// <summary>
        /// 拿起来以后这张卡在屏幕上的边长（px）。原来 UI 卡 104px，这里 315px（= 210 × <see cref="CardFaceScale"/>），
        /// 举到眼前基本占满一条可见区域，说明字都看得清。
        /// </summary>
        const float LiftSizePx = 210f * CardFaceScale;

        /// <summary>拿起来时卡片中心距屏幕底部的高度（px）。乘上 <see cref="CardFaceScale"/> 一起长，底边才不会被顶出屏幕。</summary>
        const float LiftCenterPx = 150f * CardFaceScale;

        /// <summary>拿起/放回的时间常数（SmoothDamp，秒）。</summary>
        const float LiftTime = 0.10f;

        /// <summary>
        /// 拿起过程中额外抬起的世界高度，做出"被捏起来"的弧线。
        /// 世界弧高在屏幕上会被放大成 <c>弧高 / 距离</c>：卡现在被放到镜头前 1.8~2.6m，
        /// 同样的米数比原来（9m 外）显眼两倍多，所以从 0.22 收到 0.09，屏幕上的观感才和以前一致。
        /// </summary>
        const float LiftArc = 0.09f;

        /// <summary>
        /// 已经选中（拿在手里）的卡在屏幕上的边长（px）。比 <see cref="LiftSizePx"/> 小：
        /// 这时候玩家是在挑格子，需要的只是"我还拿着哪张"，不是读说明。
        /// 基数 149px 是 124px 实机偏小后放大的结果（124 × 1.2），再整体乘 <see cref="CardFaceScale"/>。
        /// </summary>
        const float HeldSizePx = 149f * CardFaceScale;

        /// <summary>
        /// 拿在手里的卡中心距屏幕底部的高度（px）：按卡高加上 16px 底边留白自动算 ——
        /// 留白要够宽，摆动时绕视轴歪几度（<see cref="HeldSwayRoll"/>）包围盒会长高约 11px，
        /// 8px 时卡的下角会被屏幕底边切掉一截。
        /// 224px 的卡已经装不进原来的底带（网格最前排从 188px 开始），它会压住最前排 50~60px ——
        /// 这是 <see cref="CardFaceScale"/> 放大 1.5 倍必然的取舍，落点仍由幽灵预览指示。
        /// 底部那行反馈落在 16..32px，卡只在横向摆到最左时才会靠近它。
        /// </summary>
        const float HeldCenterPx = HeldSizePx * 0.5f + 16f;

        /// <summary>
        /// 拿在手里的卡跟着鼠标横向摆动的幅度。1 = 卡贴着鼠标横扫整屏；0.26 = 只在屏幕中间那一段小幅晃。
        /// 卡本来就已经贴在镜头前 <see cref="HeldDistance"/> 处了，再让它跨满全屏反而不像"手举着牌"，
        /// 所以收在四分之一上下：鼠标从屏幕最左划到最右，卡总共只挪半屏的一半左右。
        /// </summary>
        const float HeldSwayGain = 0.26f;

        /// <summary>摆动和微转的平滑时间（SmoothDamp，秒）。留一点惯性，鼠标猛甩时牌不会硬跳过去。</summary>
        const float HeldSwayTime = 0.10f;

        /// <summary>
        /// 摆到最左/最右时，牌面绕视轴的倾斜角（度）。
        /// 平面卡上这是最看得出"手持感"的一味，代价是包围盒变高（224px 的卡转 3° 会长高约 11px）。
        /// 卡面放大 <see cref="CardFaceScale"/> 倍之后它本来就压着网格最前排，
        /// 这点余量不再影响观感，所以比早先"一格不挡"的版本放得松：想更明显可以试到 5~6°。
        /// </summary>
        const float HeldSwayRoll = 3f;

        /// <summary>
        /// 摆到最左/最右时，牌面朝鼠标那一侧微转的角度（度）。
        /// 零厚度的卡 yaw 只会让它略微收窄（8° 约 1%），作用主要是给一点侧面透视；
        /// 角度再大就只收窄不长高了，所以压在 8° 是为了别让卡看得出"变窄"。
        /// </summary>
        const float HeldSwayYaw = 8f;

        /// <summary>
        /// 张数文字的世界高度，按"卡面是满尺寸（<see cref="CardWidth"/>）"时算：平躺约 7px、拿起来约 19px。
        /// 卡面缩小（卡排自适应、拿起来贴到镜头前）时 <c>PoseDecor</c> 会按同一比例缩字号，
        /// 屏幕上看到的字才不会跟着卡一起变大到出框。
        ///
        /// 0.11 是这么来的：卡面这一轮整体放大 <see cref="CardFaceScale"/>（1.5 倍），
        /// 而字号是按卡面比例缩的，1.5 倍卡会让数字在屏幕上也胖一圈；0.18 除以 1.5 约 0.12
        /// 才是"屏幕上大小不变"，再往下压一点到 0.11，就是这么"小一号"。
        /// 改卡面尺寸时记得连这个一起算，否则数字会显得忽大忽小。
        /// </summary>
        const float CountTextSize = 0.11f;

        /// <summary>
        /// 张数文字压在卡面哪条线上（占卡高的比例，卡本地 Y；负值 = 卡心往下）。
        /// 卡面原稿是 500×500，底部那条浅灰槽实测落在 y 335..449px，中心 392px（从上往下 0.784），
        /// 换成本地坐标就是卡心往下 0.284 个卡高 —— 早先按"下方 25%"猜的 0.375，所以数字整体偏低了。
        /// </summary>
        const float CountSlotCenter = -0.284f;

        // ---------------------------------------------------------------- 排版

        const float BandMarginPx = 26f;
        const float BandGapPx = 16f;

        /// <summary>排版多久重算一次（秒）。开场机位推近、窗口拉伸都靠它自己收敛回来。</summary>
        const float LayoutRefresh = 0.25f;

        // ---------------------------------------------------------------- 演算期间收走

        /// <summary>演算期间把整排手牌收走 / 放回的时间常数（SmoothDamp，秒）。</summary>
        const float HideTime = 0.12f;

        /// <summary>
        /// 收走时卡往画面下沿之外滑多远（1080 基准像素，向下）。
        /// 420px 比最高的那张卡还多出一截（拿起来 315px，半高 158px），
        /// 所以任何画幅、任何姿势下都确定落在画面外，不用再按分辨率单独算。
        /// </summary>
        const float HideSlidePx = 420f;

        /// <summary>一张卡 = 一摞牌。Count 显示的是「还没放下的张数」。</summary>
        sealed class CardEntry
        {
            public ElementTypeSO Type;
            public string Key;
            public CellKind Kind;

            /// <summary>驱动位置/朝向的节点（平躺 ↔ 拿起）。</summary>
            public Transform Root;

            /// <summary>卡面四边形。平躺时沿 z 拉长，拿起时是正方形（贴图 1:1，屏幕上也就不变形）。</summary>
            public Transform Plane;

            public Material Material;
            public TextMesh Count;

            /// <summary>没有卡面美术时顶上的那行类型名。它挂在 Root 下（尺度＝世界米），
            /// 所以卡面缩小/放大时必须跟着等比缩，见 <c>PoseDecor</c>。</summary>
            public Transform Label;

            public Transform Mark;
            public Material MarkMaterial;
            public bool HasArt;

            /// <summary>平躺时的落点。可用宽度跟画面比例有关，所以每次重排版都可能变。</summary>
            public Vector3 SlotPosition;
            public float SlotWidth = CardWidth;
            public float SlotLength = CardWidth * CardLengthFactor;

            /// <summary>0 = 躺在待排区，1 = 拿在镜头前。</summary>
            public float Lift;
            public float LiftVelocity;

            /// <summary>
            /// 手举着牌时的横向摆动量（-1 = 摆到最左，1 = 摆到最右），已经平滑过。
            /// 位置和牌面微转都由它驱动，所以两者永远同步，不会出现"卡挪了但没转"的割裂感。
            /// </summary>
            public float Sway;
            public float SwayVelocity;
        }

        BitFlowRunner runner;
        LevelFlow flow;
        Camera mainCamera;
        Font font;

        LevelSO level;
        LevelDraft draft;

        /// <summary>工坊模式：卡排内容由外部注入（见 <see cref="EnterWorkshop"/>），库存无限。</summary>
        bool workshopMode;

        /// <summary>工坊模式的卡面来源（六种元件，顺序 = 注册表顺序）。</summary>
        IReadOnlyList<ElementTypeSO> workshopPalette;

        /// <summary>草稿被改动（放置 / 拾回 / 旋转 / 撤销）后触发。工坊用它刷新自己的读数。</summary>
        public event System.Action DraftChanged;

        /// <summary>
        /// 是否处于工坊模式。
        /// 工坊的草稿没有对应的 <see cref="LevelSO"/>，所以不能走 <see cref="OnLevelLoaded"/> 那条路：
        /// 那条路会按 <c>supplies</c> 把库存压成有限张（supplies 为空时干脆把卡排整排关掉）。
        /// </summary>
        public bool IsWorkshop => workshopMode;

        /// <summary>本关手牌总数（<see cref="LevelSO.supplies"/> 里所有张数之和，含已经放下的）。状态行显示 "手牌 2/3" 用。</summary>
        int handTotal;

        /// <summary>
        /// 当前手牌总数：关卡发的张数，加上 BUFFER 从板上收回来的那几件。
        ///
        /// 收回来的件会变成手里的一张牌（<see cref="LevelDraft.TryRetract"/> 往它那一摞里加一张），
        /// 所以总数只增不减 —— 只用 <see cref="handTotal"/> 会在收回一件后读出 "HAND 2/1" 这种数字，
        /// 玩家一眼就会觉得计数坏了。
        /// </summary>
        int HandTotal => draft != null && draft.StockEnabled ? Mathf.Max(handTotal, draft.StockTotal) : handTotal;

        /// <summary>
        /// 工坊里当前选中的格子（左键点已有元件 = 选中，参数小窗跟着它显示）。
        /// 用 <c>GridPoint?</c> 而不是引用：撤销会把格子里的元件换成新克隆，
        /// 存引用会指到一个已经不在板上的旧对象。
        /// </summary>
        GridPoint? selectedCell;

        /// <summary>选中格子里的元件；没选中 / 格子空了返回 null。</summary>
        public EditableElement SelectedElement
        {
            get
            {
                if (draft == null || !selectedCell.HasValue) return null;
                return draft.GetAt(selectedCell.Value);
            }
        }

        /// <summary>选中项变了（含"变成没有选中"时传 null）。参数小窗靠它刷新。</summary>
        public event System.Action<EditableElement> CellSelected;

        ElementTypeSO armedType;

        /// <summary>
        /// 手里那张牌「具体是哪一件」——只有从板上拾回（右键 / 抓起再放下）时才有值。
        ///
        /// 有它才能保住序列 / 张数 / 放行值这些作者填过的参数：光靠 <see cref="armedType"/> 重建，
        /// 拾回来的是一个默认配置的发射器，玩家挪一次就丢掉自己刚设好的序列。
        /// 空 = 这张牌来自卡面（用类型资产的默认值）。
        /// </summary>
        EditableElement armedPayload;

        int armedRotation;
        string statusMessage = string.Empty;

        GameObject cardRoot;
        Text statusText;
        readonly List<CardEntry> cards = new List<CardEntry>();

        Transform cursor;
        Material cursorMaterial;

        /// <summary>格子上的半透明元件预览：手里有牌、鼠标悬在格子上时才显示。</summary>
        PlacementGhost ghost;

        CardEntry hovered;

        /// <summary>幽灵预览当前算的是哪一格/哪张牌：鼠标在同一格上移动时不用每帧重建一份草稿。</summary>
        GridPoint ghostCell;
        ElementTypeSO ghostType;
        int ghostRotation = -1;
        EditableElement ghostPreview;

        /// <summary>上面那份缓存是「抓起来的原件」的副本，而不是卡面的默认件。</summary>
        bool ghostGrab;

        /// <summary>
        /// 空格「抓娃娃机」：抓起时所在的格子。有值 = 手上正提着一件（<see cref="grabElement"/>）。
        ///
        /// 注意：抓起<b>不</b>把元件从草稿里摘掉 —— 摘掉的话，抓着一件去按 PLAY，
        /// 模拟里就凭空少一件，玩家会因为一个纯操作习惯输掉关卡。
        /// 真正改数据的只有放下那一下（<see cref="LevelDraft.TryMove"/>，一步撤销、不动库存）。
        /// </summary>
        GridPoint? grabCell;

        /// <summary>提在手上的那一件（仍在板上，等一次落点）。</summary>
        EditableElement grabElement;

        float nextLayoutTime;
        int lastScreenWidth;
        int lastScreenHeight;

        /// <summary>
        /// 演算期间手牌收走的进度：0 = 躺在待排区，1 = 已经整排滑到画面外。
        /// 由 <see cref="UpdateHide"/> 每帧推进，<see cref="ApplyHide"/> 拿它给所有卡一起加位移。
        /// </summary>
        float hide;
        float hideVelocity;

        /// <summary>平躺姿态：Quad 的法线朝 -Z，绕 X 转 90° 就朝上躺平，贴图上方朝 +Z（远端）。</summary>
        static readonly Quaternion FlatRotation = Quaternion.Euler(90f, 0f, 0f);

        /// <summary>卡面比地面高一点点，避免和地板抢深度。</summary>
        const float FaceLift = 0.004f;

        /// <summary>鼠标预览格子的四边形离地高度。</summary>
        const float CursorHeight = 0.05f;

        // ---------------------------------------------------------------- 生命周期

        void Start()
        {
            runner = FindFirstObjectByType<BitFlowRunner>();
            flow = FindFirstObjectByType<LevelFlow>();
            mainCamera = Camera.main;
            font = PixelFont.Get();

            if (runner == null)
            {
                Debug.LogWarning("[BitFlow] 没找到 BitFlowRunner，手牌盘关闭");
                enabled = false;
                return;
            }

            EnsureEventSystem();
            BuildStatusLine();

            // 卡排挂在场景里（不是挂在 Canvas 下）：它本来就是场景物件
            cardRoot = new GameObject("TRAY_Cards");
            cardRoot.transform.SetParent(transform, false);

            GameObject ghostRoot = new GameObject("TRAY_Ghost");
            ghostRoot.transform.SetParent(transform, false);
            ghost = ghostRoot.AddComponent<PlacementGhost>();

            if (flow != null)
            {
                flow.LevelLoaded += OnLevelLoaded;

                // LevelFlow 的 Start 比这里早（执行顺序 -10），第一次 LevelLoaded 已经错过了，
                // 所以这里补一次；之后都靠事件。
                if (flow.Current != null) OnLevelLoaded(flow.Current);
            }
            else
            {
                OnLevelLoaded(runner.LevelAsset);
            }
        }

        void OnDestroy()
        {
            if (flow != null) flow.LevelLoaded -= OnLevelLoaded;
            if (cursorMaterial != null) Destroy(cursorMaterial);
        }

        static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null) return;
            GameObject go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        /// <summary>换关：按新关卡重新生成草稿与卡排（预设件、手牌、撤销栈全部重来）。</summary>
        void OnLevelLoaded(LevelSO next)
        {
            // 工坊期间草稿归工坊所有：这时候若有人换关（自动开场的补加载、外部脚本调 Load），
            // 把草稿换掉会让「PIECES n」和板上的东西对不上。工坊退出时会自己 flow.Retry() 装回关卡。
            if (workshopMode) return;

            level = next;
            armedType = null;
            armedPayload = null;
            ReleaseGrab();
            armedRotation = 0;
            statusMessage = string.Empty;
            hovered = null;

            // 新关卡的手牌 / 元件朝向可能整套都变了，旧的预览缓存直接扔掉
            if (ghost != null) ghost.Clear();

            draft = level != null ? level.CreatePlayerDraft() : null;
            handTotal = CountSupply(level);

            if (cardRoot != null) cardRoot.SetActive(draft != null && level.HasSupplies);
            if (draft != null) RebuildCards();

            RefreshStatus();
            RefreshCards();
        }

        // ---------------------------------------------------------------- 工坊模式

        /// <summary>
        /// 切进工坊：草稿由外部持有（<paramref name="target"/>），卡排按 <paramref name="palette"/> 铺一排卡。
        ///
        /// 数量取决于草稿有没有发牌：没发（<see cref="LevelDraft.StockEnabled"/> 为 false）时
        /// <see cref="LevelDraft.Remaining"/> 恒为 -1，卡面不显示张数、想摆多少摆多少；
        /// 作者在参数小窗里发过牌之后，卡面就显示张数并按剩余量限制 —— 那是玩家进关时看到的样子。
        /// </summary>
        public void EnterWorkshop(LevelDraft target, IReadOnlyList<ElementTypeSO> palette)
        {
            if (target == null) return;

            workshopMode = true;
            workshopPalette = palette;

            level = null;
            draft = target;
            armedType = null;
            armedPayload = null;
            ReleaseGrab();
            armedRotation = 0;
            hovered = null;
            selectedCell = null;
            handTotal = target.StockTotal;
            statusMessage = "CLICK A PIECE TO EDIT IT";

            if (ghost != null) ghost.Clear();
            if (cardRoot != null) cardRoot.SetActive(true);

            RebuildCards();
            RefreshStatus();
            RefreshCards();
            CellSelected?.Invoke(null);
        }

        /// <summary>退出工坊：收起卡排、清掉草稿。下一关由工坊自己负责重载。</summary>
        public void ExitWorkshop()
        {
            if (!workshopMode) return;

            workshopMode = false;
            workshopPalette = null;
            armedType = null;
            armedPayload = null;
            ReleaseGrab();
            level = null;
            draft = null;
            statusMessage = string.Empty;
            selectedCell = null;

            if (ghost != null) ghost.Clear();
            RebuildCards();
            RefreshStatus();
            CellSelected?.Invoke(null);
        }

        /// <summary>
        /// 外部改状态行文字（工坊的 DONE / TEST / CLEAR 反馈都走这一条）。
        /// 不直接暴露 <c>statusMessage</c> 字段：改完必须重新排版一次，否则玩家看不到。
        /// </summary>
        public void SetStatus(string message)
        {
            statusMessage = message ?? string.Empty;
            RefreshStatus();
        }

        /// <summary>
        /// 外部改完草稿（例如参数小窗改了发射序列）之后调它：重建舞台 + 刷新读数 + 通知工坊。
        /// 走的就是放置/拾回同一条 <see cref="Apply"/>，所以撤销栈和存档看到的永远是同一份草稿。
        /// </summary>
        public void CommitDraft(string message)
        {
            if (draft == null) return;
            Apply(message ?? string.Empty);
        }

        void Update()
        {
            if (draft == null) return;

            // 演算（PLAY）期间手牌整排收走：它既点不动（点卡 = 重新摆草稿重跑模拟），
            // 又正好挡在信号流跑过的最前排前面，留着一层牌纯属挡板。
            bool playing = UpdateHide();

            // 黑幕演出 / 选关面板 / 换关的瞬间把输入挡掉。
            // 这里只返回而不 enabled = false：Start 里订阅了 LevelFlow.LevelLoaded，
            // 关掉组件会把"换关之后重建卡排"一起停掉（换一次关手牌就再也不刷新了）。
            if (InputGate.Locked || playing || hide > 0f)
            {
                if (ghost != null) ghost.Hide();
                if (cursor != null && cursor.gameObject.activeSelf) cursor.gameObject.SetActive(false);

                // 收走 / 放回的这几帧要看得见：输入虽然挡着，姿态照样得推，
                // 否则牌会停在画面边上的半路里等着
                if (playing || hide > 0f) UpdateCardPoses();
                return;
            }

            // 工坊本来就在编辑，作者模式的 BUILD MODE 不该再把手牌盘压掉
            if (runner == null || (!workshopMode && runner.IsBuildModeEnabled))
            {
                // 作者模式接管输入时不抢事件，也别留下一个过期的格子预览
                if (ghost != null) ghost.Hide();
                return;
            }

            HandleHotkeys();
            UpdateLayoutIfNeeded();
            UpdateHover();
            UpdateCardPoses();
            UpdateCursor();
            UpdateGhost();
            HandleMouse();
        }

        /// <summary>
        /// 演算期间把整排手牌滑出画面，暂停 / 重播 / 跑完再滑回来。
        ///
        /// 只看 <see cref="BitFlowRunner.IsPaused"/> 这一个位：换关、重播、工坊试跑、
        /// 结算后自动播放全都走它，所以不需要在每个入口各关一次卡排。
        /// 单步（STEP）会先把自己置成暂停，所以单步时牌仍然留在原位 —— 那是想要的：
        /// 一步步看的时候玩家正需要摆牌。
        /// </summary>
        /// <returns>这一帧是不是在演算（调用方据此挡掉鼠标输入）。</returns>
        bool UpdateHide()
        {
            // Simulation == null 是"关卡还没建起来"（开场演出期间就是这样），
            // 那会儿 paused 也还是默认的 false，不排掉的话手牌盘会误判成正在演算。
            bool playing = runner != null && runner.Simulation != null && !runner.IsPaused && !runner.IsFinished;

            // 收走的半路上手里那张必须放回去：否则它会跟着滑出画面，回来时还举在半空
            if (playing && (armedType != null || grabCell.HasValue))
            {
                ReleaseGrab();
                armedType = null;
                armedPayload = null;
                armedRotation = 0;
                statusMessage = string.Empty;
                RefreshCards();
            }

            if (playing) hovered = null;

            hide = Mathf.SmoothDamp(hide, playing ? 1f : 0f, ref hideVelocity, HideTime);

            // SmoothDamp 只会无限逼近目标：贴上了就钉死。
            // 不钉死的话 hide 永远回不到 0，输入会一直卡在"牌还没回来"那一边。
            if (playing)
            {
                if (hide > 0.995f)
                {
                    hide = 1f;
                    hideVelocity = 0f;
                }
            }
            else if (hide < 0.05f)
            {
                hide = 0f;
                hideVelocity = 0f;
            }

            // 状态行写的是"HAND 2/3"，手牌收走了它就是一行噪音；
            // 工坊例外 —— 试跑时作者正盯着那行反馈
            if (statusText != null) statusText.enabled = workshopMode || hide < 0.5f;

            return playing;
        }

        void HandleHotkeys()
        {
            // 空格 = 抓 / 放（「抓娃娃机」）。按一下提起来，再按一下落在鼠标指着的格子上。
            // 放在最前面：它是这个界面里最高频的操作，不该被别的键先拦走。
            if (Input.GetKeyDown(KeyCode.Space))
            {
                ToggleGrab();
                return;
            }

            if ((grabCell.HasValue || armedType != null) &&
                Input.GetKeyDown(KeyCode.Escape) && EscapeInput.Consume())
            {
                if (grabCell.HasValue)
                {
                    ReleaseGrab();
                    statusMessage = "PUT DOWN";
                    RefreshAll();
                }
                else
                {
                    armedType = null;
                    armedPayload = null;
                    statusMessage = "CARD RELEASED";
                    RefreshAll();
                }
                return;
            }

            if (Input.GetKeyDown(KeyCode.Z) &&
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                 Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)))
            {
                // 手上还提着东西时撤销没有意义（草稿里那一件根本没动过），
                // 这里让 Ctrl+Z 当"放下"用：玩家的意图都是"反悔"
                if (grabCell.HasValue)
                {
                    ReleaseGrab();
                    statusMessage = "PUT DOWN";
                    RefreshAll();
                    return;
                }

                if (draft.Undo())
                {
                    Apply("UNDO");

                    // 撤销可能把"收回来的件"退回板上（那一摞牌也就没了），卡排必须跟着重排
                    RebuildCards();
                    RefreshCards();
                }
                else
                {
                    statusMessage = "NOTHING TO UNDO";
                    RefreshAll();
                }
                return;
            }

            if (!Input.GetKeyDown(KeyCode.R)) return;

            // 提着东西时 R 不转：转的是鼠标下那格，而那一格现在只是"落点"，
            // 真正该被转的件还在原位，转谁都像是 bug。放下之后再按 R 就正常了。
            if (grabCell.HasValue)
            {
                statusMessage = "DROP IT TO ROTATE";
                RefreshAll();
                return;
            }

            // 手里有牌时 R 转「待放朝向」，否则转鼠标下的元件
            if (armedType != null)
            {
                // 延迟器四面等价：转它不改行为，也不该让玩家以为「转一下就能换出口」
                if (KindOf(armedType) == CellKind.DelayGate)
                {
                    statusMessage = "FIXED - NO ROTATE";
                    RefreshAll();
                    return;
                }

                armedRotation = (armedRotation + 1) % 4;
                statusMessage = "AIM " + RotationName(armedRotation);
                RefreshAll();
                return;
            }

            if (TryPickCell(out GridPoint cell))
            {
                if (LockedForMe(cell))
                {
                    statusMessage = "FIXED - NO ROTATE";
                    RefreshAll();
                }
                else if (draft.GetAt(cell) is EditableElement pickedRotation && !pickedRotation.SupportsRotation)
                {
                    statusMessage = "FIXED - NO ROTATE";
                    RefreshAll();
                }
                else if (draft.Rotate(cell))
                {
                    Apply("ROTATED");
                    // 和放置一样，动画要挂在 Apply 新建出来的那个视图上
                    PlayRotateAt(cell, true);
                }
            }
        }

        /// <summary>
        /// 给刚放下的元件补一段落地动画。
        /// 找不到视图就静默跳过 —— 动画是纯表现，绝不该因为它影响"操作是否成功"。
        /// </summary>
        void PlayDropInAt(GridPoint cell)
        {
            ElementView view = FindViewAt(cell);
            if (view != null) view.PlayDropIn();
        }

        /// <summary>给刚旋转的元件补一段转身动画，让玩家看清"转的是这一个"。</summary>
        void PlayRotateAt(GridPoint cell, bool clockwise)
        {
            ElementView view = FindViewAt(cell);
            if (view != null) view.PlayRotateIn(clockwise);
        }

        ElementView FindViewAt(GridPoint cell)
        {
            if (runner == null || runner.Builder == null) return null;
            return runner.Builder.GetElementAt(cell);
        }

        static string RotationName(int steps)
        {
            switch (((steps % 4) + 4) % 4)
            {
                case 1: return "R90";
                case 2: return "R180";
                case 3: return "R270";
                default: return "R0";
            }
        }

        // ---------------------------------------------------------------- 卡排生成

        void BuildStatusLine()
        {
            GameObject canvasGo = new GameObject("TRAY_Canvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            // 层序只有一条规则：HUD 是 0 且必须最上 —— 结算面板和悬停提示都住在 HUD 里，
            // 被手牌盘盖住就是"过关了看不见结算"。所以这一层永远往下排。
            canvas.sortingOrder = -1;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.transform.SetParent(transform, false);

            // 底部只剩这一行反馈（已放几件 / 为什么放不下），没有底板也没有边框：
            // 底部空间现在是场景里那排卡的，UI 再压一块上去就白搬了。
            // 文字本身落在屏幕 16..32px（截图框 16..40px）：平躺的卡排从 53px 开始，
            // 拿在手里的卡（<see cref="HeldSizePx"/> 见方、底边留 8px）也压不到左边这一小段 ——
            // 卡横向只在屏幕中间那一片摆（HeldSwayGain），和左边的状态文字错开。
            statusText = CreateText("Status", canvasGo.transform, string.Empty, 16, TextAnchor.LowerLeft, MutedColor);
            RectTransform rect = statusText.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = Vector2.zero;
            rect.sizeDelta = new Vector2(760f, 24f);
            rect.anchoredPosition = new Vector2(28f, 16f);
        }

        void RebuildCards()
        {
            foreach (CardEntry entry in cards)
            {
                if (entry.Root != null) Destroy(entry.Root.gameObject);
                if (entry.Material != null) Destroy(entry.Material);
                if (entry.MarkMaterial != null) Destroy(entry.MarkMaterial);
            }
            cards.Clear();
            hovered = null;

            if (cardRoot == null) return;

            // 工坊：卡面来源是外部注入的调色板，与 supplies 无关；库存无限所以没有"用完了"这回事
            if (workshopMode)
            {
                if (workshopPalette == null) return;

                // 按「种类」去重，而不是按类型 Key：卡面美术是 <see cref="CardArtLibrary"/> 一个种类一张图，
                // 注册表里常有好几份同种类的变体（不同序列的发射器之类），摆出来就是几张一模一样的牌 ——
                // 玩家分不清哪张是哪张。一种只给一张，用的就是注册表里第一份。
                HashSet<CellKind> drawnKinds = new HashSet<CellKind>();
                foreach (ElementTypeSO type in workshopPalette)
                {
                    if (type == null || string.IsNullOrEmpty(type.Key)) continue;

                    // 工具卡不进工坊：BUFFER 借的是墙的形态，摆出来会是一张"假墙"，
                    // 而作者在工坊里要的是能真的放到板上的件。
                    if (type.IsTool) continue;
                    if (!drawnKinds.Add(KindOf(type))) continue;

                    cards.Add(CreateCard(type));
                }

                RefreshLayout();
                return;
            }

            if (level == null || level.supplies == null) return;

            // 库存按类型 Key 记账，同一个类型写两条 = 一摞牌里多几张。
            // 这里必须按 Key 去重：不然会画出两张一模一样的卡，还共用同一个数字（看着像计数坏了）。
            HashSet<string> drawn = new HashSet<string>();

            foreach (ElementSupply supply in level.supplies)
            {
                if (supply == null || supply.type == null) continue;
                if (!drawn.Add(supply.type.Key)) continue;

                cards.Add(CreateCard(supply.type));
            }

            // BUFFER 收回来的件不在 supplies 里，只存在于草稿的库存里。
            // 不补一张卡的话，玩家手里明明有这件却看不见 —— 那件就只能永远压在手里，等于被工具牌吃掉了。
            // （放进 drawn 里判重：supplies 里有的类型前面已经画过了。）
            if (draft != null)
            {
                foreach (KeyValuePair<string, int> pair in draft.Stock)
                {
                    if (pair.Value <= 0) continue;
                    if (!drawn.Add(pair.Key)) continue;

                    ElementTypeSO recovered = FindSupplyType(pair.Key);
                    if (recovered != null) cards.Add(CreateCard(recovered));
                }
            }

            RefreshLayout();
        }

        /// <summary>
        /// 造一张卡：Root（位置/朝向）→ Face（Quad，卡面）+ Mark（选中时露出的强调条）+ Count（张数）。
        /// 全部用无光照材质：卡面是美术稿，被场景灯光打一下就变色，屏幕上跟设计稿对不上。
        /// </summary>
        CardEntry CreateCard(ElementTypeSO type)
        {
            CellKind kind = KindOf(type);
            Sprite art = CardArtLibrary.Icon(type);

            GameObject root = new GameObject("Card_" + type.Key);
            root.transform.SetParent(cardRoot.transform, false);

            GameObject face = GameObject.CreatePrimitive(PrimitiveType.Quad);
            face.name = "Face";
            // 命中判定走射线 + 矩形数学，不需要物理碰撞体；留着反而会被别的射线检测撞到
            Destroy(face.GetComponent<Collider>());
            face.transform.SetParent(root.transform, false);

            MeshRenderer faceRenderer = face.GetComponent<MeshRenderer>();
            faceRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            faceRenderer.receiveShadows = false;

            Material material = MaterialFactory.Textured(
                art != null ? art.texture : null,
                art != null ? Color.white : LevelBuilder.KindColor(kind),
                "M_Card_" + type.Key);
            faceRenderer.sharedMaterial = material;

            // 选中标记：卡排前沿的一根强调条。卡面是不透明美术，只能用"外挂一根条"表达选中，
            // 直接改卡面底色会把图标和说明一起染掉。
            GameObject mark = GameObject.CreatePrimitive(PrimitiveType.Quad);
            mark.name = "Mark";
            Destroy(mark.GetComponent<Collider>());
            mark.transform.SetParent(root.transform, false);
            MeshRenderer markRenderer = mark.GetComponent<MeshRenderer>();
            markRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            markRenderer.receiveShadows = false;

            Material markMaterial = MaterialFactory.Textured(null, AccentColor, "M_CardMark");
            markRenderer.sharedMaterial = markMaterial;
            mark.SetActive(false);

            CardEntry entry = new CardEntry
            {
                Type = type,
                Key = type.Key,
                Kind = kind,
                Root = root.transform,
                Plane = face.transform,
                Material = material,
                Mark = mark.transform,
                MarkMaterial = markMaterial,
                HasArt = art != null,
            };

            entry.Count = CreateCountText(root.transform);
            if (art == null) entry.Label = CreateFallbackLabel(root.transform, LabelOf(type));

            return entry;
        }

        /// <summary>
        /// 张数文字：TextMesh 直接躺在卡面上（本地轴和卡面同向），所以卡被拿起来时会跟着一起转正。
        /// 字号交给 characterSize（fontSize = 0），世界单位直接可控。
        /// </summary>
        TextMesh CreateCountText(Transform parent)
        {
            GameObject go = new GameObject("Count");
            go.transform.SetParent(parent, false);

            TextMesh mesh = go.AddComponent<TextMesh>();
            mesh.font = font;
            mesh.fontSize = 0;
            mesh.characterSize = CountTextSize;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = CountTextColor;

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return mesh;
        }

        /// <summary>没有卡面美术时的退路：一块类型色的板 + 名字，至少能玩。</summary>
        Transform CreateFallbackLabel(Transform parent, string label)
        {
            GameObject go = new GameObject("Label");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0f, -FaceLift);

            TextMesh mesh = go.AddComponent<TextMesh>();
            mesh.text = label;
            mesh.font = font;
            mesh.fontSize = 0;
            mesh.characterSize = 0.16f;
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = TextColor;

            MeshRenderer renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = font.material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        // ---------------------------------------------------------------- 排版

        /// <summary>每 0.25 秒重算一次落点：开场机位推近、窗口拉伸、切分辨率都靠它自己收敛回来。</summary>
        void UpdateLayoutIfNeeded()
        {
            bool resized = Screen.width != lastScreenWidth || Screen.height != lastScreenHeight;
            if (!resized && Time.unscaledTime < nextLayoutTime) return;

            lastScreenWidth = Screen.width;
            lastScreenHeight = Screen.height;
            nextLayoutTime = Time.unscaledTime + LayoutRefresh;
            RefreshLayout();
        }

        /// <summary>
        /// 重算这一排卡落在地面上的位置和大小。
        /// 宽度预算是「屏幕左边距 → HUD 控制条左边缘」：卡片是能点的东西，
        /// 被控制条压掉一角就等于点不到，所以宁可整排按比例缩小，也不让它铺到控制条底下。
        /// 横向落点以网格中心为准（看起来才是"摆在这块板子前面"），再夹进这段可用区间。
        /// </summary>
        void RefreshLayout()
        {
            if (cards.Count == 0 || mainCamera == null) return;

            float pxScale = PixelScale();

            float bandLeft = BandMarginPx * pxScale;
            float bandRight = Screen.width - bandLeft;
            Rect bar = BitFlowHud.BottomBarScreenRect;
            if (bar.width > 1f) bandRight = Mathf.Min(bandRight, bar.xMin - BandGapPx * pxScale);
            if (bandRight - bandLeft < 120f * pxScale) bandRight = bandLeft + 120f * pxScale; // 极端窄屏兜底

            // 每世界单位多少像素：就在卡排那个深度上量一段 1 格的水平距离
            float rowZ = RowFrontZ - CardWidth * CardLengthFactor * 0.5f;
            Vector3 probe = new Vector3(0f, RowY, rowZ);
            float pxPerUnit = Mathf.Abs(
                mainCamera.WorldToScreenPoint(probe + Vector3.right).x -
                mainCamera.WorldToScreenPoint(probe).x);
            if (pxPerUnit < 0.01f) return;

            int count = cards.Count;
            float width = count * CardWidth + (count - 1) * CardGap;
            float available = bandRight - bandLeft;
            float shrink = width * pxPerUnit > available ? available / (width * pxPerUnit) : 1f;

            // 只在放不下时缩；其余情况保持"一格一张卡"的固定比例，牌和格子才看得出大小关系
            shrink = Mathf.Clamp(shrink, 0.45f, 1f);

            float cardWidth = CardWidth * shrink;
            float gap = CardGap * shrink;
            width = count * cardWidth + (count - 1) * gap;

            // 横向落点对准网格中心。工坊没有 LevelSO，宽度以草稿为准（草稿是唯一权威）。
            int gridWidth = draft != null ? draft.Width : (level != null ? level.width : 1);
            float gridCenterX = (gridWidth - 1) * 0.5f;
            float gridCenterPx = mainCamera.WorldToScreenPoint(new Vector3(gridCenterX, RowY, rowZ)).x;
            float halfPx = width * pxPerUnit * 0.5f;
            float centerPx = Mathf.Clamp(gridCenterPx, bandLeft + halfPx, bandRight - halfPx);
            float centerX = gridCenterX + (centerPx - gridCenterPx) / pxPerUnit;

            for (int i = 0; i < count; i++)
            {
                CardEntry entry = cards[i];
                float length = cardWidth * CardLengthFactor;

                entry.SlotWidth = cardWidth;
                entry.SlotLength = length;
                entry.SlotPosition = new Vector3(
                    centerX + (i - (count - 1) * 0.5f) * (cardWidth + gap),
                    RowY,
                    RowFrontZ - length * 0.5f);

                // 躺着的那张立刻落到新位置；已经拿起来的交给 UpdateCardPoses 平滑过渡
                if (entry.Root != null && entry.Lift < 0.001f) ApplyFlatPose(entry);
            }
        }

        float PixelScale()
        {
            if (mainCamera != null && mainCamera.pixelHeight > 0) return mainCamera.pixelHeight / ReferenceHeight;
            return Screen.height / ReferenceHeight;
        }

        void ApplyFlatPose(CardEntry entry)
        {
            entry.Root.position = entry.SlotPosition;
            entry.Root.rotation = FlatRotation;
            entry.Plane.localScale = new Vector3(entry.SlotWidth, entry.SlotLength, 1f);
            PoseDecor(entry);
        }

        /// <summary>
        /// 张数、选中条、退路文字都跟着卡面尺寸走。
        /// 卡面不只在"拉长矩形 ↔ 正方形"之间变：拿起来时它还要缩到镜头前的真实尺寸
        /// （0.95m → 0.19m 左右），所以这里不能再用槽宽算，得读卡面当前的实际缩放 ——
        /// 位置和大小同源，才不会出现"卡缩到镜头前、文字却还是原来那么大"的穿帮。
        /// </summary>
        void PoseDecor(CardEntry entry)
        {
            // 卡面的 localScale 已经包含了"槽宽 → 拿起尺寸"的全部插值结果，直接拿它当准。
            Vector3 face = entry.Plane != null
                ? entry.Plane.localScale
                : new Vector3(entry.SlotWidth, entry.SlotLength, 1f);

            float width = face.x;
            float height = face.y;

            // 相对"满尺寸卡面"的缩放比：字号和选中条厚度按它等比缩，否则卡一小文字就顶出卡面
            float k = CardWidth > 0.0001f ? width / CardWidth : 1f;

            if (entry.Count != null)
            {
                // 文字中心对准卡面那条浅灰槽的中心（见 CountSlotCenter），卡面拉长/收成正方形都跟着走
                entry.Count.transform.localPosition = new Vector3(0f, height * CountSlotCenter, -FaceLift);
                entry.Count.transform.localScale = Vector3.one * k;
            }

            if (entry.Label != null) entry.Label.localScale = Vector3.one * k;

            if (entry.Mark != null)
            {
                entry.Mark.localScale = new Vector3(width, 0.05f * k, 1f);
                entry.Mark.localPosition = new Vector3(0f, -(height * 0.5f) - 0.05f * k, -FaceLift * 1.5f);
            }
        }

        // ---------------------------------------------------------------- 拿起 / 悬停

        /// <summary>
        /// 每帧把每张卡从"平躺落点"插值到"拿在镜头前"，或者反过来。
        /// 两种"拿起来"姿势：鼠标靠近只想读说明 → <see cref="LiftPosition"/>（315px 读得清）；
        /// 已经选中要落子 → <see cref="HeldPosition"/>（224px 沉在屏幕下沿）。
        /// 目标值切换后仍用 SmoothDamp 过渡，避免闪烁。
        /// </summary>
        void UpdateCardPoses()
        {
            if (mainCamera == null) return;

            foreach (CardEntry entry in cards)
            {
                if (entry.Root == null) continue;

                // 手里已经有一张时，别的卡不再弹起来读说明 —— 否则会同时有两张飞在镜头前
                bool held = armedType != null && entry.Type == armedType;
                bool lifted = held || (armedType == null && entry == hovered);

                entry.Lift = Mathf.SmoothDamp(entry.Lift, lifted ? 1f : 0f, ref entry.LiftVelocity, LiftTime);

                float eased = entry.Lift * entry.Lift * (3f - 2f * entry.Lift);

                // 鼠标横向偏移归一化到 -1..1，只用来推"手里那张"左右摆 —— 卡不再贴着鼠标横扫整屏。
                // 平滑一下让摆动带点惯性；没拿在手里时目标归零，牌自己慢慢收回正中。
                float swayTarget = held
                    ? Mathf.Clamp(Input.mousePosition.x / Mathf.Max(1f, mainCamera.pixelWidth) * 2f - 1f, -1f, 1f)
                    : 0f;
                entry.Sway = Mathf.SmoothDamp(entry.Sway, swayTarget, ref entry.SwayVelocity, HeldSwayTime);

                Vector3 flatPosition = entry.SlotPosition;
                Vector3 flatScale = new Vector3(entry.SlotWidth, entry.SlotLength, 1f);

                // 卡面尺寸不再直接等于槽宽：它由"离相机多远 + 屏幕上占多少像素"反推出来，
                // 所以两种姿势各有各的尺寸，和落点一起由下面两个函数给出。
                // 位置和大小必须成对取值 —— 分开算就会出现"屏幕上看着一样大、实际在世界另一头"。
                float faceSize;
                Vector3 liftPosition = held ? HeldPosition(entry.Sway, out faceSize) : LiftPosition(entry, out faceSize);

                // 拿起时卡面变成正方形（贴图是 1:1 的），屏幕上才不会左右被压窄
                Vector3 liftScale = new Vector3(faceSize, faceSize, 1f);

                // 牌面微转：绕视轴歪一点（平面卡上最看得出来），再朝鼠标那侧转一点点补侧面透视。
                // 乘 eased 是让卡在"飞过来"的半路上不带歪，落定之后手感才慢慢显出来；
                // 和位置共用同一个 Sway，所以"挪多远"和"歪多少"永远同步。
                Quaternion liftRotation = mainCamera.transform.rotation;
                if (held)
                {
                    float swayDeg = entry.Sway * eased;
                    liftRotation *= Quaternion.Euler(0f, swayDeg * HeldSwayYaw, -swayDeg * HeldSwayRoll);
                }

                Vector3 position = Vector3.Lerp(flatPosition, liftPosition, eased);
                // 中途再抬一点：看起来像被手指捏起来，而不是从地上弹到眼前
                position.y += Mathf.Sin(entry.Lift * Mathf.PI) * LiftArc;

                entry.Root.position = position;
                entry.Root.rotation = Quaternion.Slerp(FlatRotation, liftRotation, eased);
                entry.Plane.localScale = Vector3.Lerp(flatScale, liftScale, eased);

                // 演算期间再叠一层"整排滑出画面"。放在最后：它只做位移和缩放，
                // 不影响上面算好的落点 —— 收起半路上牌不会先拐个弯再出画
                ApplyHide(entry);

                PoseDecor(entry);
            }
        }

        /// <summary>
        /// 把手牌往画面下沿之外推。位移量按<b>屏幕像素</b>算（<see cref="HideSlidePx"/>），
        /// 所以不管卡此刻是躺着、被拿起来贴着镜头，还是画面是什么分辨率，
        /// 都确定出得了画；用的还是卡当前的深度，滑出去的路上大小不变。
        /// </summary>
        void ApplyHide(CardEntry entry)
        {
            if (hide <= 0f || entry.Plane == null) return;

            Vector3 position = entry.Root.position;

            // 前半段轻轻起手、后半段一把抽走：hide² 让"开始收"和"收到位"都不突兀
            float k = hide * hide;

            float distance = Vector3.Dot(position - mainCamera.transform.position, mainCamera.transform.forward);
            Vector2 screen = mainCamera.WorldToScreenPoint(position);
            Vector3 away = CardPlanePoint(new Vector2(screen.x, -HideSlidePx * PixelScale()), Mathf.Max(0.5f, distance));

            entry.Root.position = Vector3.LerpUnclamped(position, away, k);

            // 顺手缩一点：看着像被抽走，而不是被硬推出画外
            float shrink = 1f - 0.3f * k;
            entry.Plane.localScale = new Vector3(
                entry.Plane.localScale.x * shrink,
                entry.Plane.localScale.y * shrink,
                1f);
        }

        /// <summary>
        /// 悬停读说明的落点：屏幕上的像素位置和大小是定死的（<see cref="LiftSizePx"/> 见方、压在底边），
        /// 卡就贴在 <see cref="LiftDistance"/> 那个深度上 —— 这样任何分辨率、任何画幅比例下，
        /// 拿起来的卡都一样大、一样位置，也不会因为卡有几格宽就跑到不同的距离去。
        /// </summary>
        Vector3 LiftPosition(CardEntry entry, out float worldSize)
        {
            float pxScale = PixelScale();
            float sizePx = LiftSizePx * pxScale;

            // 横向贴着卡躺在待排区时的位置（左右不跳），只把它夹进屏幕内
            float pxX = mainCamera.WorldToScreenPoint(entry.SlotPosition).x;
            pxX = Mathf.Clamp(pxX, sizePx * 0.5f + 16f * pxScale, mainCamera.pixelWidth - sizePx * 0.5f - 16f * pxScale);
            float pxY = LiftCenterPx * pxScale;

            worldSize = WorldSizeAt(LiftDistance, LiftSizePx);
            return CardPlanePoint(new Vector2(pxX, pxY), LiftDistance);
        }

        /// <summary>
        /// 已经选中（拿在手里）的落点：缩到 <see cref="HeldSizePx"/> 沉在屏幕下沿。
        /// 卡面按 <see cref="CardFaceScale"/> 放大 1.5 倍后（224px），它会压住网格最前面一排约 40px：
        /// 屏幕底部本来只有 188px 高，224px 的卡物理上塞不进去 —— 换来的是一眼看得清卡面。
        /// "手里是哪张"和"会落在哪"都没有丢：前者看这张卡，后者看格子上的幽灵预览。
        ///
        /// 横向不再贴着鼠标（那样卡会跟着光标横扫整屏，看着像在场景里漂）：
        /// 卡停在屏幕底部正中，鼠标只按 <see cref="HeldSwayGain"/> 推它左右小幅摆。
        /// 距离钉在 <see cref="HeldDistance"/>，鼠标划满全屏卡在世界里也只走一米出头 —— 就是"手举着牌左右看"。
        /// </summary>
        /// <param name="sway">-1 = 摆到最左，1 = 摆到最右（见 <see cref="CardEntry.Sway"/>）。</param>
        Vector3 HeldPosition(float sway, out float worldSize)
        {
            float pxScale = PixelScale();
            float sizePx = HeldSizePx * pxScale;
            float margin = sizePx * 0.5f + BandMarginPx * pxScale;

            float centerX = mainCamera.pixelWidth * 0.5f;
            float pxX = Mathf.Clamp(centerX + sway * centerX * HeldSwayGain, margin, mainCamera.pixelWidth - margin);

            // 右下角那条 HUD 控制条（播放/暂停/撤销）不能被盖住：卡最多顶到它左边
            Rect bar = BitFlowHud.BottomBarScreenRect;
            if (bar.width > 1f)
            {
                float limit = bar.xMin - sizePx * 0.5f - BandGapPx * pxScale;
                if (limit > margin) pxX = Mathf.Min(pxX, limit);
            }

            worldSize = WorldSizeAt(HeldDistance, HeldSizePx);
            return CardPlanePoint(new Vector2(pxX, HeldCenterPx * pxScale), HeldDistance);
        }

        /// <summary>
        /// 相机前方 distance 米处、屏幕上正好占 sizePx（1080 基准像素）的世界边长。
        /// 屏幕占多少像素只取决于"世界边长 / 距离"这个比值，和分辨率无关：
        /// sizePx 乘了 pxScale、画面像素高度也乘了 pxScale，正好约掉。
        /// </summary>
        float WorldSizeAt(float distance, float sizePx)
        {
            float tanHalf = Mathf.Tan(mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
            return sizePx * (2f * tanHalf * distance / ReferenceHeight);
        }

        /// <summary>
        /// 把"屏幕像素点 + 一个固定距离"换成相机前方那个世界坐标。
        /// 距离由调用方钉死（<see cref="LiftDistance"/> / <see cref="HeldDistance"/>），
        /// 鼠标只决定卡在这个"手持平面"上的落点 —— 屏幕位置照样跟手，但世界里的行程只有几十厘米。
        /// </summary>
        Vector3 CardPlanePoint(Vector2 pixel, float distance)
        {
            float tanHalf = Mathf.Tan(mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);

            float ndcX = pixel.x / (mainCamera.pixelWidth * 0.5f) - 1f;
            float ndcY = pixel.y / (mainCamera.pixelHeight * 0.5f) - 1f;
            float tanHalfH = tanHalf * mainCamera.aspect;

            Transform cameraTransform = mainCamera.transform;
            return cameraTransform.position
                + cameraTransform.forward * distance
                + cameraTransform.right * (ndcX * tanHalfH * distance)
                + cameraTransform.up * (ndcY * tanHalf * distance);
        }

        /// <summary>已经拿起来的那张卡在屏幕上的矩形。鼠标落在里面就维持 hover 状态。</summary>
        Rect ScreenRectOf(CardEntry entry)
        {
            float size = LiftSizePx * PixelScale();
            Vector3 screen = mainCamera.WorldToScreenPoint(entry.Root.position);
            return new Rect(screen.x - size * 0.5f, screen.y - size * 0.5f, size, size);
        }

        void UpdateHover()
        {
            hovered = null;
            if (cards.Count == 0 || mainCamera == null) return;
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            Vector2 mouse = Input.mousePosition;

            // 已经拿起来的那张优先：鼠标落在它"拿起来以后"的矩形里就继续算它。
            // 否则卡飞起来离开原地，hover 会立刻断掉，卡就在原地和镜头前反复弹。
            foreach (CardEntry entry in cards)
            {
                // 拿在手里的那张除外：它已经沉到屏幕底带，而 ScreenRectOf 是按"悬停姿势"
                // （210px、压在底边）算的，位置早就对不上，用它判定会白吞掉一大片格子上的点击。
                if (armedType != null && entry.Type == armedType) continue;

                if (entry.Lift > 0.35f && ScreenRectOf(entry).Contains(mouse))
                {
                    hovered = entry;
                    return;
                }
            }

            if (!TryRayToRowPlane(mouse, out Vector3 point)) return;

            float nearest = float.MaxValue;
            foreach (CardEntry entry in cards)
            {
                Vector3 delta = point - entry.SlotPosition;
                if (Mathf.Abs(delta.x) > entry.SlotWidth * 0.5f + HoverMargin) continue;
                if (Mathf.Abs(delta.z) > entry.SlotLength * 0.5f + HoverMargin) continue;

                if (Mathf.Abs(delta.z) < nearest)
                {
                    nearest = Mathf.Abs(delta.z);
                    hovered = entry;
                }
            }
        }

        bool TryRayToRowPlane(Vector2 mouse, out Vector3 point)
        {
            point = Vector3.zero;
            if (mainCamera == null) return false;

            Ray ray = mainCamera.ScreenPointToRay(mouse);
            Plane plane = new Plane(Vector3.up, new Vector3(0f, RowY, 0f));
            if (!plane.Raycast(ray, out float distance)) return false;

            point = ray.GetPoint(distance);
            return true;
        }

        // ---------------------------------------------------------------- 鼠标

        void UpdateCursor()
        {
            if (cursor == null) CreateCursor();

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            // 手里有牌时，格子由半透明的元件副本去标记（能放=类型色、不能放=红），
            // 这层普通高亮方块让位 —— 两层东西叠在同一格上只会互相糊掉。
            // 鼠标在卡上时也不画：这时候玩家在"看牌"，不是在选位置。
            // 工具卡是例外：它没有元件幽灵可画（见 UpdateGhost），落点提示只能靠这层中性高亮。
            bool aimingTool = armedType != null && armedType.IsTool;

            if ((armedType != null && !aimingTool) || grabCell.HasValue || overUi || hovered != null || !TryPickCell(out GridPoint cell))
            {
                if (cursor.gameObject.activeSelf) cursor.gameObject.SetActive(false);
                return;
            }

            cursor.gameObject.SetActive(true);
            cursor.position = GridMetrics.CellToWorld(cell) + Vector3.up * CursorHeight;
        }

        /// <summary>
        /// 手里有牌时，在鼠标悬停的那一格上放一个半透明的元件副本（能放=类型色，不能放=红）。
        /// 这是"选中不挡屏幕"的配套：卡缩进底带以后，落点和占格全靠这个预览告诉玩家。
        /// </summary>
        void UpdateGhost()
        {
            if (ghost == null) return;

            bool overUi = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();
            bool carrying = grabCell.HasValue;

            // 工具卡不是"往格子里放东西"：幽灵预览会在鼠标下画一堵墙，玩家会以为要盖墙。
            // 落点提示交给 UpdateCursor 的中性高亮，成没成功由状态行回答。
            if (!carrying && armedType != null && armedType.IsTool)
            {
                ghost.Hide();
                return;
            }

            // 手里没牌 / 没提着东西 / 在 UI 上 / 在待排区选牌 —— 都没有"要放到哪一格"这回事
            if (draft == null || (!carrying && armedType == null) || mainCamera == null || overUi ||
                hovered != null || !TryPickCell(out GridPoint cell))
            {
                ghost.Hide();
                return;
            }

            // 搬动：原件还在板上，预览是一份跟着鼠标走的临时副本
            if (carrying)
            {
                if (ghostPreview == null || !ghostGrab || ghostType != null || !ghostCell.Equals(cell))
                {
                    ghostPreview = grabElement != null ? grabElement.Clone() : null;
                    if (ghostPreview != null) ghostPreview.Coord = cell;

                    ghostGrab = true;
                    ghostType = null;
                    ghostRotation = -1;
                    ghostCell = cell;
                }

                if (ghostPreview == null)
                {
                    ghost.Hide();
                    return;
                }

                ghost.Show(ghostPreview, draft.CanMove(grabCell.Value, cell));
                return;
            }

            // 手牌：只在"换了格子/换了牌/转了向"时重新生成草稿：同一格上晃鼠标不该每帧产生垃圾
            if (ghostPreview == null || ghostGrab || ghostType != armedType || ghostRotation != armedRotation ||
                !ghostCell.Equals(cell))
            {
                ghostPreview = BuildArmedElement(cell);
                ghostGrab = false;
                ghostType = armedType;
                ghostRotation = armedRotation;
                ghostCell = cell;
            }

            if (ghostPreview == null)
            {
                ghost.Hide();
                return;
            }

            // 能不能放，走和真正放下时同一条判定（CanPlace 只读：不扣牌、不压撤销栈）
            ghost.Show(ghostPreview, draft.CanPlace(ghostPreview));
        }

        void HandleMouse()
        {
            // 鼠标在待排区某张卡上：左键 = 拿在手里，右键 = 放下手里那张
            if (hovered != null)
            {
                if (Input.GetMouseButtonDown(0))
                {
                    Arm(hovered.Type);
                }
                else if (Input.GetMouseButtonDown(1) && armedType != null)
                {
                    armedType = null;
                    armedPayload = null;
                    statusMessage = "CARD RELEASED";
                    RefreshAll();
                }
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;

            // 手上提着板上的一件时，左键点格子 = 放下（空格是同一个动作，
            // 两种输入都通才像"抓娃娃机"：一只手能抓能放）
            if (grabCell.HasValue)
            {
                if (Input.GetMouseButtonDown(0)) DropGrabbed();
                return;
            }

            if (!TryPickCell(out GridPoint cell)) return;

            if (Input.GetMouseButtonDown(1))
            {
                PickBack(cell);
                return;
            }

            if (!Input.GetMouseButtonDown(0)) return;

            if (armedType != null)
            {
                // 工具卡（BUFFER）不是往格子里放东西，而是对那一格做一件事。
                // 两者共用同一个输入（手里有牌 + 左键点格子），所以分流点必须放在这里。
                if (armedType.IsTool) TryBuffer(cell);
                else TryPlaceArmed(cell);
                return;
            }

            // 手里没牌：
            // - 工坊 → 左键点已有元件 = 选中并打开参数小窗（作者要改序列/次数，不能一点就被搬走）；
            // - 正式关 → 还是老规矩，左键点自己的元件 = 拿起来（方案 A：可自由搬动）。
            if (workshopMode)
            {
                if (draft.GetAt(cell) != null) SelectCell(cell);
                else ClearSelection();
                return;
            }

            PickBack(cell);
        }

        /// <summary>选中一格（工坊参数编辑的入口）。空格 = 清掉选中。</summary>
        void SelectCell(GridPoint cell)
        {
            if (draft == null) return;

            EditableElement element = draft.GetAt(cell);
            if (element == null)
            {
                ClearSelection();
                return;
            }

            selectedCell = cell;
            statusMessage = "SELECTED " + element.Kind + " " + cell;

            // RefreshAll 末尾会重新播一次 CellSelected，参数小窗跟着刷新
            RefreshAll();
        }

        /// <summary>取消选中。选中的格子被拾回 / 退出工坊时都要走这里，否则小窗会显示一个已经不在板上的件。</summary>
        void ClearSelection()
        {
            selectedCell = null;
            CellSelected?.Invoke(null);
        }

        /// <summary>
        /// 把"当前选中的到底还是不是原来那个件"重新播一遍。
        /// 撤销会把格子里的元件换成新克隆，参数小窗必须跟着换引用，否则改的是已经作废的那份。
        /// </summary>
        void NotifySelection()
        {
            if (!workshopMode) return;
            if (!selectedCell.HasValue) return;

            EditableElement element = SelectedElement;
            if (element == null) selectedCell = null;
            CellSelected?.Invoke(element);
        }

        /// <summary>
        /// 这一格对<b>当前这个人</b>是不是固定的。
        ///
        /// 锁是「给玩家的标记」：玩家局里它硬得很（搬不动、删不掉、转不了），
        /// 但工坊里作者正在做这一关，他必须能搬自己标成 FIXED 的件 —— 否则标完就再也动不了了。
        /// </summary>
        bool LockedForMe(GridPoint cell)
        {
            return !workshopMode && draft.IsLocked(cell);
        }

        bool TryPickCell(out GridPoint cell)
        {
            cell = default;

            if (mainCamera == null) mainCamera = Camera.main;
            if (mainCamera == null || draft == null) return false;

            Ray ray = mainCamera.ScreenPointToRay(Input.mousePosition);
            Plane ground = new Plane(Vector3.up, Vector3.zero);
            if (!ground.Raycast(ray, out float distance)) return false;

            GridPoint candidate = GridMetrics.WorldToCell(ray.GetPoint(distance));
            if (!draft.InBounds(candidate)) return false;

            cell = candidate;
            return true;
        }

        // ---------------------------------------------------------------- 放置 / 拾回

        // ---------------------------------------------------------------- 空格抓取（搬动元件）

        /// <summary>空格：手上没东西就抓起鼠标下那件，有东西就把它放到鼠标下的格子。</summary>
        void ToggleGrab()
        {
            if (grabCell.HasValue) DropGrabbed();
            else TryGrab();
        }

        /// <summary>抓起鼠标下的元件。预设件 / 空格都会被挡下来，并给一句说人话的反馈。</summary>
        void TryGrab()
        {
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (hovered != null) return;

            if (!TryPickCell(out GridPoint cell))
            {
                Fail("NO CELL");
                return;
            }

            if (LockedForMe(cell))
            {
                Fail("FIXED - NO MOVE");
                return;
            }

            EditableElement element = draft.GetAt(cell);
            if (element == null)
            {
                Fail("EMPTY CELL");
                return;
            }

            // 手上还捏着一张牌时先松手：两种"拿着东西"的状态同时存在，落点该听谁的说不清
            armedType = null;
            armedPayload = null;
            armedRotation = 0;

            grabCell = cell;
            grabElement = element;

            // 让幽灵下一帧按"搬动"重建一份副本（缓存键换了模式，旧的不能再复用）
            ghostGrab = false;
            ghostPreview = null;

            statusMessage = "HOLDING " + element.Kind + " - SPACE TO DROP";
            RefreshAll();
        }

        /// <summary>把手上那件放到鼠标指的格子；放不下就继续提着（挪到别的格子再按一次空格就行）。</summary>
        void DropGrabbed()
        {
            if (!grabCell.HasValue || grabElement == null) return;

            GridPoint from = grabCell.Value;

            // 鼠标不在网格上（比如已经移到了面板 / 卡排上）：原地放下，等于取消
            GridPoint to = TryPickCell(out GridPoint cell) ? cell : from;

            PlaceResult check = draft.CanMove(from, to);
            if (check != PlaceResult.Ok)
            {
                if (check == PlaceResult.Locked) Fail("FIXED CELL");
                else if (check == PlaceResult.Occupied) Fail("OCCUPIED");
                else Fail("OUTSIDE GRID");
                return;
            }

            ReleaseGrab();

            if (!draft.TryMove(from, to))
            {
                Fail("CANNOT MOVE");
                return;
            }

            // 选中框跟着走：搬动的多半正是不久前刚选中的那一件
            if (selectedCell.HasValue && selectedCell.Value.Equals(from)) selectedCell = to;

            Apply("MOVED");
            PlayDropInAt(to);
        }

        /// <summary>
        /// 放弃这次搬动。草稿里那一件从来没离开过原位，所以这里什么都不用改 ——
        /// 这也正是"抓起来"只记状态、不动数据的最大好处。
        /// </summary>
        void ReleaseGrab()
        {
            grabCell = null;
            grabElement = null;
        }

        /// <summary>
        /// 把「手里那张牌」变成一件具体可放的元件。
        ///
        /// 两条来源：从卡面拿的（按类型默认值现造）、从板上拾回的（<see cref="armedPayload"/> 的副本）。
        /// 后者必须复制而不是直接交出去 —— 复制出来的副本归草稿所有，原对象继续跟着手里的牌走，
        /// 玩家转一次朝向、放一次又拾回来，参数都还在。
        /// </summary>
        EditableElement BuildArmedElement(GridPoint cell)
        {
            if (armedPayload != null)
            {
                EditableElement element = armedPayload.Clone();
                element.Coord = cell;

                int steps = ((armedRotation % 4) + 4) % 4;
                for (int i = 0; i < steps; i++) element.Rotate();
                return element;
            }

            return armedType != null ? armedType.CreateEditable(cell, armedRotation) : null;
        }

        void TryPlaceArmed(GridPoint cell)
        {
            EditableElement element = BuildArmedElement(cell);
            if (element == null)
            {
                Fail(LabelOf(armedType) + " CANNOT PLACE");
                return;
            }

            PlaceResult result = draft.TryPlace(element);

            switch (result)
            {
                case PlaceResult.Ok:
                    // 只有真的耗尽（== 0）才松开手牌；工坊不限量时 Remaining 是 -1，必须继续握着
                    if (armedType == null || draft.Remaining(armedType.Key) == 0)
                    {
                        armedType = null;
                        armedPayload = null;
                    }

                    // 刚放下的那件立刻选中：作者摆完就想改参数，别逼他再点一次
                    if (workshopMode) selectedCell = cell;

                    Apply("PLACED");
                    // 必须排在 Apply 之后：Apply 会重建整个舞台，旧的 ElementView 已经没了
                    PlayDropInAt(cell);
                    break;

                case PlaceResult.Locked:
                    Fail("FIXED CELL");
                    break;

                case PlaceResult.Occupied:
                    Fail("OCCUPIED");
                    break;

                case PlaceResult.OutOfStock:
                    Fail(LabelOf(armedType) + " LEFT");
                    break;

                default:
                    Fail("OUTSIDE GRID");
                    break;
            }
        }

        /// <summary>
        /// BUFFER 卡落地：把鼠标指着的**锁定件**收进手牌，并立刻把它拿在手里继续放。
        ///
        /// 成功之后必须重排卡排：收回来的件很可能是这一关 supplies 里根本没有的类型
        /// （wallMap 画的墙就是），那一摞牌还没画出来 —— 不重排，玩家会看不见自己刚换到的那张牌。
        /// </summary>
        void TryBuffer(GridPoint cell)
        {
            if (draft == null) return;

            RetractResult result = draft.TryRetract(
                cell, armedType != null ? armedType.Key : string.Empty, out EditableElement picked);

            switch (result)
            {
                case RetractResult.Ok:
                    // 收回的是正选中的那一格：小窗要收起来（那件已经不在板上）
                    if (selectedCell.HasValue && selectedCell.Value.Equals(cell)) selectedCell = null;

                    // 换到手里的是「这一件」：参数跟着走，放回去还是原样
                    armedType = FindSupplyType(picked.TypeKey);
                    armedPayload = armedType != null ? picked : null;
                    armedRotation = 0;
                    ReleaseGrab();

                    Apply(armedType != null ? "BUFFERED " + LabelOf(armedType) : "BUFFERED " + picked.Kind);
                    RebuildCards();
                    RefreshCards();
                    break;

                case RetractResult.NotLocked:
                    // 自己的件用右键就能拾回，别浪费一张工具牌
                    Fail("NOT FIXED - RIGHT CLICK");
                    break;

                case RetractResult.NotBufferable:
                    // 发射器 / 接收器定义了这道题：收走它等于玩家自己改题，这里刻意不扣牌
                    Fail("CANNOT BUFFER");
                    break;

                case RetractResult.EmptyCell:
                    Fail("EMPTY CELL");
                    break;

                case RetractResult.OutOfStock:
                    Fail(LabelOf(armedType) + " LEFT");
                    break;

                default:
                    Fail("OUTSIDE GRID");
                    break;
            }
        }

        void PickBack(GridPoint cell)
        {
            if (LockedForMe(cell))
            {
                Fail("FIXED - NO PICKUP");
                return;
            }

            if (!draft.TryPick(cell, out EditableElement picked))
            {
                Fail("EMPTY CELL");
                return;
            }

            // 拾回的是正选中的那一格：小窗必须收起来（那个件已经回到手里，不再在板上）
            if (selectedCell.HasValue && selectedCell.Value.Equals(cell)) selectedCell = null;

            // 拾回的牌直接拿在手里，玩家可以接着投到别处 —— 不用再去点卡面。
            // 拾回的是"这一件本身"，不是"这种元件"，所以必须把参数一起带走：
            // 玩家改好序列的发射器被搬一下就打回默认值，是这个界面最不该有的 bug。
            armedType = FindSupplyType(picked.TypeKey);
            armedPayload = armedType != null ? picked : null;
            armedRotation = 0;

            // 拾回也算一次"手里有东西"：之前提着的那一件作废（草稿里它没动过，放下即可）
            ReleaseGrab();

            Apply(armedType != null
                ? "PICKED UP " + LabelOf(armedType)
                : "REMOVED " + picked.Kind);
        }

        ElementTypeSO FindSupplyType(string typeKey)
        {
            if (string.IsNullOrEmpty(typeKey)) return null;

            // 工坊的"手牌"就是那一排无限卡：拾回一个件时要能拿回同款继续放
            if (workshopMode)
            {
                if (workshopPalette == null) return null;

                foreach (ElementTypeSO type in workshopPalette)
                {
                    if (type != null && type.Key == typeKey) return type;
                }
                return null;
            }

            if (level == null || level.supplies == null) return null;

            foreach (ElementSupply supply in level.supplies)
            {
                if (supply == null || supply.type == null) continue;
                if (supply.type.Key == typeKey) return supply.type;
            }

            // 兜底：BUFFER 收回来的件（比如 wallMap 画的墙）根本没写进本关 supplies，
            // 只能问类型注册表。问不到就返回 null —— 玩家还是会拿到牌（payload 那一路），只是没卡面。
            LevelTypeRegistrySO registry = LevelTypeRegistrySO.Load();
            return registry != null ? registry.Find(typeKey) : null;
        }

        static string LabelOf(ElementTypeSO type)
        {
            if (type == null) return "ELEMENT";
            return string.IsNullOrEmpty(type.label) ? type.Key : type.label;
        }

        /// <summary>
        /// 这张牌对应哪种元件（决定卡面图）。交给 <see cref="ElementTypeSO"/> 自己判定，
        /// 手牌盘不重复一套"渲染器类型 → CellKind"的映射，免得以后加元件时两边对不上。
        /// </summary>
        static CellKind KindOf(ElementTypeSO type)
        {
            EditableElement preview = type.CreateEditable(new GridPoint(0, 0));
            return preview != null ? preview.Kind : CellKind.Wall;
        }

        void Arm(ElementTypeSO type)
        {
            if (type == null || draft == null) return;

            // 手上还提着板上的一件时先放下：两张不同来源的"手里东西"同时存在，
            // 点一下格子到底该落哪个，玩家自己也猜不到。
            if (grabCell.HasValue)
            {
                Fail("PUT IT DOWN FIRST");
                return;
            }

            // == 0 才是真没牌；-1 是不限量（工坊 / 作者模式），不该拦住选卡
            if (draft.Remaining(type.Key) == 0)
            {
                Fail(LabelOf(type) + " LEFT");
                return;
            }

            armedType = armedType == type ? null : type; // 再点一次同一张卡 = 取消
            armedPayload = null;                          // 从卡面拿的牌用类型默认值，不继承上一次拾回的那件
            armedRotation = 0;
            if (armedType != null)
            {
                statusMessage = "AIM " + LabelOf(armedType) + " - CLICK A CELL";
            }
            else
            {
                statusMessage = "CARD RELEASED";
            }

            RefreshAll();
        }

        // ---------------------------------------------------------------- 落地 / 刷新

        /// <summary>草稿 → 运行时关卡 → 重建场景（Runner 会顺手重置到 TICK 00 并暂停）。</summary>
        void Apply(string message)
        {
            statusMessage = message;
            if (draft != null) runner.ShowDefinition(draft.ToDefinition());
            RefreshAll();
            DraftChanged?.Invoke();
        }

        void Fail(string message)
        {
            statusMessage = message;
            RefreshAll();
        }

        void RefreshAll()
        {
            RefreshStatus();
            RefreshCards();
            UpdateGhost();
            NotifySelection();
        }

        void RefreshCards()
        {
            foreach (CardEntry entry in cards)
            {
                if (entry.Root == null) continue;

                int left = draft != null ? draft.Remaining(entry.Key) : 0;

                // -1 = 不限量（工坊模式 / 作者模式）：卡面留空，而且**不能压暗** ——
                // 压暗是"这摞用完了"的语义，无限量永远用不完。
                bool unlimited = left < 0;
                bool selected = armedType == entry.Type;
                bool empty = !unlimited && left <= 0;

                // 选中态走那根强调条：卡面是不透明美术图，改底色会把它一起染掉
                if (entry.Mark != null) entry.Mark.gameObject.SetActive(selected);

                // 用完的整张压暗（还看得出是哪张），其余保持原色 —— 卡面就是设计稿，别动它的颜色
                MaterialFactory.SetBaseColor(entry.Material, empty ? EmptyTint : Color.white);

                if (entry.Count != null)
                {
                    entry.Count.text = unlimited ? string.Empty : "x" + Mathf.Max(0, left);
                    entry.Count.color = empty ? CountTextEmptyColor : (entry.HasArt ? CountTextColor : TextColor);
                }
            }
        }

        // ---------------------------------------------------------------- 状态行文案

        /// <summary>
        /// 这一关没发手牌时，左下角状态行显示的那句话。
        ///
        /// <para>两种情况共用同一句——「压根没有手牌草稿」和「有草稿但这关的 supplies 没发牌」，
        /// 对玩家来说都是「这关没有牌可用」，分成两句只会多一份要同步维护的文案。</para>
        ///
        /// <para>只用 ASCII 大写：屏幕字体是位图字体，没有中文字形（见 <see cref="PixelFont"/>）。</para>
        /// </summary>
        const string NoHandStatus = "NO CARDS THIS LEVEL";

        void RefreshStatus()
        {
            if (statusText == null) return;

            // 工坊：没有手牌张数这回事，改成报「已经摆了几件」—— 那是玩家唯一关心的数字；
            // 发了牌的话再补一个总张数，作者一眼能看出「这关到底给了几张」
            if (workshopMode)
            {
                string parts = "PIECES " + (draft != null ? draft.Count : 0);
                if (draft != null && draft.StockEnabled) parts += "  ·  HAND " + draft.StockTotal;

                statusText.text = string.IsNullOrEmpty(statusMessage) ? parts : parts + "  /  " + statusMessage;
                return;
            }

            if (draft == null || !draft.StockEnabled)
            {
                statusText.text = NoHandStatus;
            }
            else
            {
                // 只留"手牌用了几张 / 一共几张" + 最后一条反馈；
                // 手里拿着哪张由卡片的强调条表达，不再写一行文字
                string summary = "HAND " + HandUsed() + "/" + HandTotal;
                statusText.text = string.IsNullOrEmpty(statusMessage) ? summary : summary + "  /  " + statusMessage;
            }
        }

        /// <summary>手牌已经用出去几张 = 总数 - 各摞剩余之和（预设件不算，它们不是玩家放的）。</summary>
        int HandUsed()
        {
            if (draft == null || !draft.StockEnabled) return 0;

            int left = 0;
            foreach (KeyValuePair<string, int> pair in draft.Stock) left += Mathf.Max(0, pair.Value);
            return Mathf.Max(0, HandTotal - left);
        }

        /// <summary>本关手牌总数：按 supplies 逐条累加（同一类型写两条就是"再多给几张"，和草稿记账规则一致）。</summary>
        static int CountSupply(LevelSO source)
        {
            if (source == null || source.supplies == null) return 0;

            int total = 0;
            foreach (ElementSupply supply in source.supplies)
            {
                if (supply == null || supply.type == null) continue;
                total += Mathf.Max(0, supply.count);
            }
            return total;
        }

        /// <summary>
        /// 普通高亮的格子方块：只在"手里没牌"的悬停时出现。
        /// 手里有牌时格子交给 <see cref="PlacementGhost"/> 的幽灵预览（能放=类型色、不能放=红），
        /// 所以这里恒定是中性灰 —— 不再需要跟着"待放元件是什么"变色。
        /// </summary>
        void CreateCursor()
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = "TrayCursor";
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.rotation = FlatRotation;
            go.transform.localScale = new Vector3(0.9f, 0.9f, 1f);

            cursorMaterial = new Material(MaterialFactory.LitShader) { name = "M_TrayCursor" };
            MaterialFactory.SetBaseColor(cursorMaterial, MutedColor);
            MaterialFactory.SetEmission(cursorMaterial, MutedColor, 1.4f);
            go.GetComponent<Renderer>().sharedMaterial = cursorMaterial;

            cursor = go.transform;
            go.SetActive(false);
        }

        // ---------------------------------------------------------------- 控件工厂（与 HUD 同一套写法）

        Text CreateText(string name, Transform parent, string content, int fontSize, TextAnchor anchor, Color color)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            Text text = go.GetComponent<Text>();
            text.font = font;
            text.text = content;
            text.fontSize = fontSize;
            text.alignment = anchor;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }
    }
}
