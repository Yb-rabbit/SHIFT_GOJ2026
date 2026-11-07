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
    /// 交互（方案 A 不变）：左键 = 拿卡/放卡/拾回 ｜ 右键 = 拾回 或 放下手里的卡 ｜
    /// R = 转待放朝向或转鼠标下的件 ｜ Ctrl+Z = 撤销 ｜ ESC = 放下手里的卡。
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

        ElementTypeSO armedType;
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
        float nextLayoutTime;
        int lastScreenWidth;
        int lastScreenHeight;

        /// <summary>平躺姿态：Quad 的法线朝 -Z，绕 X 转 90° 就朝上躺平，贴图上方朝 +Z（远端）。</summary>
        static readonly Quaternion FlatRotation = Quaternion.Euler(90f, 0f, 0f);

        /// <summary>卡面比地面高一点点，避免和地板抢深度。</summary>
        const float FaceLift = 0.004f;

        /// <summary>鼠标预览格子的四边形离地高度。</summary>
        const float CursorHeight = 0.05f;

        // ---------------------------------------------------------------- 生命周期

        void Start()
        {
            runner = FindObjectOfType<BitFlowRunner>();
            flow = FindObjectOfType<LevelFlow>();
            mainCamera = Camera.main;
            font = LoadFont();

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

        static Font LoadFont()
        {
            Font loaded = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (loaded == null) loaded = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return loaded;
        }

        static void EnsureEventSystem()
        {
            if (FindObjectOfType<EventSystem>() != null) return;
            GameObject go = new GameObject("EventSystem");
            go.AddComponent<EventSystem>();
            go.AddComponent<StandaloneInputModule>();
        }

        /// <summary>换关：按新关卡重新生成草稿与卡排（预设件、手牌、撤销栈全部重来）。</summary>
        void OnLevelLoaded(LevelSO next)
        {
            level = next;
            armedType = null;
            armedRotation = 0;
            statusMessage = string.Empty;
            hovered = null;

            // 新关卡的手牌 / 元件朝向可能整套都变了，旧的预览缓存直接扔掉
            if (ghost != null) ghost.Clear();

            draft = level != null ? level.CreatePlayerDraft() : null;

            if (cardRoot != null) cardRoot.SetActive(draft != null && level.HasSupplies);
            if (draft != null) RebuildCards();

            RefreshStatus();
            RefreshCards();
        }

        void Update()
        {
            if (draft == null) return;
            if (runner == null || runner.IsBuildModeEnabled)
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

        void HandleHotkeys()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && armedType != null)
            {
                armedType = null;
                statusMessage = "已放下手里的卡";
                RefreshAll();
                return;
            }

            if (Input.GetKeyDown(KeyCode.Z) &&
                (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl) ||
                 Input.GetKey(KeyCode.LeftCommand) || Input.GetKey(KeyCode.RightCommand)))
            {
                if (draft.Undo()) Apply("已撤销一步");
                else
                {
                    statusMessage = "没有可撤销的操作了";
                    RefreshAll();
                }
                return;
            }

            if (!Input.GetKeyDown(KeyCode.R)) return;

            // 手里有牌时 R 转「待放朝向」，否则转鼠标下的元件
            if (armedType != null)
            {
                armedRotation = (armedRotation + 1) % 4;
                statusMessage = "待放朝向：" + RotationName(armedRotation);
                RefreshAll();
                return;
            }

            if (TryPickCell(out GridPoint cell))
            {
                if (draft.IsLocked(cell))
                {
                    statusMessage = "这是关卡给定的元件，不能旋转";
                    RefreshAll();
                }
                else if (draft.Rotate(cell)) Apply("已旋转 " + cell);
            }
        }

        static string RotationName(int steps)
        {
            switch (((steps % 4) + 4) % 4)
            {
                case 1: return "顺时针 90°";
                case 2: return "180°";
                case 3: return "顺时针 270°";
                default: return "原始朝向";
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

            if (level == null || level.supplies == null || cardRoot == null) return;

            // 库存按类型 Key 记账，同一个类型写两条 = 一摞牌里多几张。
            // 这里必须按 Key 去重：不然会画出两张一模一样的卡，还共用同一个数字（看着像计数坏了）。
            HashSet<string> drawn = new HashSet<string>();

            foreach (ElementSupply supply in level.supplies)
            {
                if (supply == null || supply.type == null) continue;
                if (!drawn.Add(supply.type.Key)) continue;

                cards.Add(CreateCard(supply.type));
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
            Sprite art = CardArtLibrary.Icon(kind);

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
            if (cards.Count == 0 || mainCamera == null || level == null) return;

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

            float gridCenterX = (level.width - 1) * 0.5f;
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

                PoseDecor(entry);
            }
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
            if (armedType != null || overUi || hovered != null || !TryPickCell(out GridPoint cell))
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

            // 手里没牌 / 在 UI 上 / 在待排区选牌 —— 都没有"要放到哪一格"这回事
            if (draft == null || armedType == null || mainCamera == null || overUi ||
                hovered != null || !TryPickCell(out GridPoint cell))
            {
                ghost.Hide();
                return;
            }

            // 只在"换了格子/换了牌/转了向"时重新生成草稿：同一格上晃鼠标不该每帧产生垃圾
            if (ghostPreview == null || ghostType != armedType || ghostRotation != armedRotation || !ghostCell.Equals(cell))
            {
                ghostPreview = armedType.CreateEditable(cell, armedRotation);
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
                    statusMessage = "已放下手里的卡";
                    RefreshAll();
                }
                return;
            }

            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return;
            if (!TryPickCell(out GridPoint cell)) return;

            if (Input.GetMouseButtonDown(1))
            {
                PickBack(cell);
                return;
            }

            if (!Input.GetMouseButtonDown(0)) return;

            if (armedType != null) TryPlaceArmed(cell);
            else PickBack(cell); // 手里没牌：左键点自己的元件 = 拿起来（方案 A：可自由搬动）
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

        void TryPlaceArmed(GridPoint cell)
        {
            EditableElement element = armedType.CreateEditable(cell, armedRotation);
            if (element == null)
            {
                Fail(LabelOf(armedType) + " 没有可用的配置，放不下去");
                return;
            }

            PlaceResult result = draft.TryPlace(element);

            switch (result)
            {
                case PlaceResult.Ok:
                    string placed = LabelOf(armedType) + " " + cell;
                    if (draft.Remaining(armedType.Key) <= 0) armedType = null; // 这摞用完了
                    Apply("已放下 " + placed);
                    break;

                case PlaceResult.Locked:
                    Fail("这格是关卡给定的元件，不能覆盖");
                    break;

                case PlaceResult.Occupied:
                    Fail("这格已经有元件了（先左键点它拾回）");
                    break;

                case PlaceResult.OutOfStock:
                    Fail(LabelOf(armedType) + " 已经用完了");
                    break;

                default:
                    Fail("点到网格外面了");
                    break;
            }
        }

        void PickBack(GridPoint cell)
        {
            if (draft.IsLocked(cell))
            {
                Fail("这是关卡给定的元件，不能拾回");
                return;
            }

            if (!draft.TryPick(cell, out EditableElement picked))
            {
                Fail("这格是空的");
                return;
            }

            // 拾回的牌直接拿在手里，玩家可以接着投到别处 —— 不用再去点卡面
            armedType = FindSupplyType(picked.TypeKey);
            armedRotation = 0;

            Apply(armedType != null
                ? "已拾回 " + LabelOf(armedType)
                : "已移除 " + picked.Kind + "（它不属于任何一排牌）");
        }

        ElementTypeSO FindSupplyType(string typeKey)
        {
            if (level == null || level.supplies == null || string.IsNullOrEmpty(typeKey)) return null;

            foreach (ElementSupply supply in level.supplies)
            {
                if (supply == null || supply.type == null) continue;
                if (supply.type.Key == typeKey) return supply.type;
            }
            return null;
        }

        static string LabelOf(ElementTypeSO type)
        {
            if (type == null) return "元件";
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

            if (draft.Remaining(type.Key) <= 0)
            {
                Fail(LabelOf(type) + " 已经用完了");
                return;
            }

            armedType = armedType == type ? null : type; // 再点一次同一张卡 = 取消
            armedRotation = 0;
            if (armedType != null)
            {
                statusMessage = "已选中 " + LabelOf(armedType) + "：移动鼠标挑格子，点一下放下";
            }
            else
            {
                statusMessage = "已放下手里的卡";
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
        }

        void RefreshCards()
        {
            foreach (CardEntry entry in cards)
            {
                if (entry.Root == null) continue;

                int left = draft != null ? draft.Remaining(entry.Key) : 0;
                bool selected = armedType == entry.Type;
                bool empty = left <= 0;

                // 选中态走那根强调条：卡面是不透明美术图，改底色会把它一起染掉
                if (entry.Mark != null) entry.Mark.gameObject.SetActive(selected);

                // 用完的整张压暗（还看得出是哪张），其余保持原色 —— 卡面就是设计稿，别动它的颜色
                MaterialFactory.SetBaseColor(entry.Material, empty ? EmptyTint : Color.white);

                if (entry.Count != null)
                {
                    entry.Count.text = "×" + Mathf.Max(0, left);
                    entry.Count.color = empty ? CountTextEmptyColor : (entry.HasArt ? CountTextColor : TextColor);
                }
            }
        }

        void RefreshStatus()
        {
            if (statusText == null) return;

            if (draft == null)
            {
                statusText.text = "本关没有给玩家手牌";
            }
            else if (!draft.StockEnabled)
            {
                statusText.text = "演示关：没有手牌";
            }
            else
            {
                // 只留"已放几件" + 最后一条反馈；手里拿着哪张由卡片的强调条表达，不再写一行文字
                string summary = "已放 " + draft.Count + " 件";
                statusText.text = string.IsNullOrEmpty(statusMessage) ? summary : summary + " ｜ " + statusMessage;
            }
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
