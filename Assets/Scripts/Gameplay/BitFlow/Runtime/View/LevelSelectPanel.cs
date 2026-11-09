using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 选关面板：把所有关卡列成一列按钮，通关的打勾，当前这关高亮。
    ///
    /// 三个约束：
    /// 1. 面板自己一套 Canvas（sortingOrder 40），盖在黑幕（30）和 HUD（0）之上 ——
    ///    结算面板里的 LEVELS 按钮就是在"HUD 之上再盖一层"，不能和 HUD 共用 Canvas。
    /// 2. 打开期间用 <see cref="InputGate"/> 挡住手牌盘：玩家在挑关卡，不该同时在摆元件。
    /// 3. 切关顺序是<b>先盖黑幕、再放自己的锁、最后才 Load</b>。
    ///    锁是引用计数，18 帧里任何一刻都不会出现"两道锁都放了、黑幕还没盖住"的缝。
    /// </summary>
    public sealed class LevelSelectPanel : MonoBehaviour
    {
        static readonly Color DimColor = new Color(0f, 0f, 0f, 0.62f);
        static readonly Color PanelColor = new Color(0.05f, 0.07f, 0.09f, 0.97f);
        static readonly Color TitleColor = new Color(0.92f, 0.98f, 1f, 1f);
        static readonly Color MutedColor = new Color(0.62f, 0.72f, 0.80f, 1f);
        static readonly Color RowColor = new Color(0.10f, 0.14f, 0.18f, 1f);
        static readonly Color RowCurrentColor = new Color(0.14f, 0.26f, 0.32f, 1f);
        static readonly Color ClearedColor = new Color(0.45f, 0.86f, 0.62f, 1f);

        const float RowHeight = 56f;
        const float RowGap = 10f;
        const float CaptionHeight = 26f;
        const float HintHeight = 22f;
        const float ActionHeight = 34f;

        /// <summary>列表区高度 = 面板 620 - 标题占的 96 - 底部（结果行 48 + CLOSE 占的 76）。</summary>
        const float ListHeight = 396f;

        LevelFlow flow;
        IntroCurtain curtain;
        Font font;

        GameObject canvasObject;
        RectTransform listRoot;
        ScrollRect listScroll;
        Text emptyText;

        /// <summary>
        /// 动作结果行（<c>IMPORTED  IMPORT_01</c>）。挂在列表下沿和 CLOSE 之间、<b>不进滚动区</b> ——
        /// 挂在列表末尾的话，玩家在列表底部点完按钮，回话可能正好掉到裁剪线以下，等于没回话。
        /// </summary>
        Text statusText;

        bool open;

        /// <summary>列表当前显示的是关卡列表，还是「新建关卡选尺寸」那一屏。</summary>
        bool sizePickMode;

        /// <summary>列表当前显示的是不是「把当前关卡的 JSON 原文摊开看」那一屏。</summary>
        bool jsonViewMode;

        /// <summary>
        /// DELETE 的两段式确认（和工坊的 CLEAR / HOME 同一套路）：第一次只记状态，
        /// 几秒内再点一次才真删。文件删了没有回收站，一个误触不该毁掉别人做的关。
        /// </summary>
        bool deleteArmed;
        float deleteArmedAt;

        /// <summary>「再点一次确认」的有效时长。</summary>
        const float ConfirmSeconds = 3f;

        /// <summary>
        /// 导入/导出这类「按一下就得有个回话」的动作留下的结果行（<c>IMPORTED  IMPORT_01</c>）。
        /// 一次性：<see cref="Rebuild"/> 读走就清空，不会留到下一次开面板。
        /// </summary>
        string flash;

        // 每次 Open 重扫一遍目录：玩家/AI 直接改 JSON 文件，重开面板就能看到最新内容
        readonly List<LevelJsonEntry> jsonEntries = new List<LevelJsonEntry>();

        void Awake()
        {
            BuildUi();
            canvasObject.SetActive(false);
        }

        void Start()
        {
            flow = FindObjectOfType<LevelFlow>();
            curtain = FindObjectOfType<IntroCurtain>();

            if (flow == null) Debug.LogWarning("[BitFlow] 没找到 LevelFlow，选关面板不会列出任何关卡");
        }

        void Update()
        {
            // 只处理 ESC 关面板。其余按键全部交给 InputGate 挡掉的那套逻辑。
            if (open && Input.GetKeyDown(KeyCode.Escape)) Close();
        }

        void OnDestroy()
        {
            if (open) InputGate.Release();
        }

        /// <summary>打开面板。重复打开是空操作（避免引用计数被叠两次）。</summary>
        public void Open()
        {
            if (open) return;

            open = true;
            sizePickMode = false;
            jsonViewMode = false;
            InputGate.Acquire();
            canvasObject.SetActive(true);
            Rebuild();
        }

        public void Close()
        {
            if (!open) return;

            open = false;
            canvasObject.SetActive(false);
            InputGate.Release();
        }

        /// <summary>通关后 HUD 会调它，让打勾立刻反映出来（面板没开就什么都不用做）。</summary>
        public void Refresh()
        {
            if (open) Rebuild();
        }

        // ---------------------------------------------------------------- 列表

        void Rebuild()
        {
            // 滚动位置要跨重建保留：结算后 Refresh、按下 IMPORT 都会重建列表，
            // 每次跳回顶部的话，玩家刚滚到底找到了 FILES，一按就又被弹回关卡头部。
            float previousScroll = listScroll != null ? listScroll.verticalNormalizedPosition : 1f;

            for (int i = listRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(listRoot.GetChild(i).gameObject);
            }

            // 结果行是一次性的：谁触发 Rebuild 谁负责在这之前把它写进去
            string status = flash;
            flash = null;
            SetStatus(status);

            // 「新建关卡」是同一块列表的第二屏：点 + NEW LEVEL 进来挑尺寸，点 BACK 回关卡列表。
            // 不另开一层面板 —— 尺寸只有四档，多一层 Canvas 反而要多处理一次输入锁的引用计数。
            if (sizePickMode)
            {
                RebuildSizePick();
                SetScroll(1f);      // 换屏了，位置没意义，从上往下读
                return;
            }

            // JSON 原文是第三屏，同理不另开 Canvas
            if (jsonViewMode)
            {
                RebuildJsonView();
                SetScroll(1f);      // 原文从第一行开始看，接着上一屏的滚动位置会停在莫名其妙的地方
                return;
            }

            jsonEntries.Clear();

            LevelCatalogSO catalog = flow != null ? flow.Catalog : null;
            int catalogCount = catalog != null ? catalog.Count : 0;

            // 场上没有 LevelFlow 就不去扫目录：这种面板点了也进不了关
            List<string> jsonProblems = null;
            if (flow != null)
            {
                // 先建目录再扫：首次运行目录还不存在时 Scan 会把「还没有关卡目录」记成一条 problem，
                // 面板上就会莫名其妙挂个 FILE ERROR。建完再扫，空目录 = 干净的 0 条。
                LevelJsonLibrary.EnsureFolder();
                jsonEntries.AddRange(LevelJsonLibrary.Scan(out jsonProblems));
                foreach (string problem in jsonProblems) Debug.LogWarning("[BitFlow] 关卡 JSON：" + problem);
            }

            bool hasJsonSection = jsonEntries.Count > 0 || (jsonProblems != null && jsonProblems.Count > 0);
            if (catalogCount == 0 && !hasJsonSection && flow == null)
            {
                emptyText.gameObject.SetActive(true);
                listRoot.gameObject.SetActive(false);
                return;
            }

            emptyText.gameObject.SetActive(false);
            listRoot.gameObject.SetActive(true);

            string currentName = flow != null && flow.Current != null ? flow.Current.displayName : null;
            string currentJsonPath = flow != null ? flow.CurrentJsonPath : null;

            float y = 0f;
            int row = 0;

            for (int i = 0; i < catalogCount; i++)
            {
                LevelSO level = catalog.Get(i);
                if (level == null) continue;

                bool isCurrent = string.IsNullOrEmpty(currentJsonPath) && !string.IsNullOrEmpty(currentName)
                    && level.displayName == currentName;

                int captured = i;
                CreateRow(row++, level, null, isCurrent, () => Select(captured), ref y);
            }

            // MY LEVELS 一整段：标题 → + NEW LEVEL → 存档行 → 报错提示。
            // 新建入口排在存档列表前面：这是玩家唯一「从零做一关」的路，不该被二十条存档压到屏幕外。
            if (hasJsonSection || flow != null)
            {
                CreateCaption("MY LEVELS", ref y);

                if (flow != null)
                {
                    CreateAction("+  NEW LEVEL", BeginSizePick, ref y);
                    if (jsonEntries.Count == 0) CreateHint("NO LEVELS YET  ·  PRESS + NEW LEVEL", ref y);
                }

                foreach (LevelJsonEntry entry in jsonEntries)
                {
                    LevelJsonEntry captured = entry;
                    bool isCurrent = currentJsonPath == entry.Path;
                    CreateRow(row++, null, captured, isCurrent, () => SelectJson(captured), ref y);
                }

                if (jsonProblems != null && jsonProblems.Count > 0)
                {
                    // 像素字体没有中文字形：面板上只说「有错、看日志」，细节都在 Console
                    CreateHint("FILE ERROR  ·  SEE CONSOLE", ref y);
                }
            }

            // 自己的关卡放哪、刷新一下看到新文件 —— 少了这两个按钮，玩家得自己去翻
            // AppData/LocalLow 里的隐藏目录，等于没有这个功能。
            // 导入/导出也归这一组：都是「关卡文件进出这个目录」的事，排在存档列表下面，
            // 不该把 + NEW LEVEL 和已有的关卡挤到屏幕外。
            if (flow != null)
            {
                CreateCaption("FILES", ref y);
                CreateAction("IMPORT  ·  FROM CLIPBOARD", ImportFromClipboard, ref y);
                CreateAction("EXPORT  ·  CURRENT LEVEL", ExportCurrent, ref y);
                CreateAction("VIEW JSON  ·  CURRENT LEVEL", BeginJsonView, ref y);
                CreateAction("DELETE  ·  CURRENT LEVEL", DeleteCurrentJson, ref y);
                CreateAction("SHARE FOLDER", OpenJsonFolder, ref y);
                CreateAction("REFRESH", RefreshList, ref y);
            }

            // 刚按完的动作结果贴在按钮正下方：和手指/鼠标最后停留的位置一致
            // listRoot 自上而下排：y 一路走成负数，高度取它的绝对值
            listRoot.sizeDelta = new Vector2(listRoot.sizeDelta.x, Mathf.Max(0f, -y));

            SetScroll(previousScroll);
        }

        /// <summary>把结果行写进标题下的那一行（空字符串 = 隐藏）。</summary>
        void SetStatus(string status)
        {
            if (statusText == null) return;

            bool visible = !string.IsNullOrEmpty(status);
            statusText.gameObject.SetActive(visible);
            if (visible) statusText.text = status;
        }

        /// <summary>设列表滚动位置。<c>1</c> = 顶部，<c>0</c> = 底部。</summary>
        void SetScroll(float normalized)
        {
            if (listScroll == null) return;

            listScroll.verticalNormalizedPosition = Mathf.Clamp01(normalized);
        }

        /// <summary>
        /// 「新建关卡」第二屏：四档网格尺寸，点一个就开工坊。
        /// 为什么不做输入框：工程里没有 TextMeshPro / InputField，自由输入尺寸要另搭一套中文输入处理，
        /// 而四档尺寸已经覆盖了「小谜题 → 大地图」的全部需要。
        /// </summary>
        void RebuildSizePick()
        {
            emptyText.gameObject.SetActive(false);
            listRoot.gameObject.SetActive(true);

            float y = 0f;
            CreateCaption("NEW LEVEL  ·  PICK A GRID", ref y);

            Vector2Int standard = BitFlowWorkshop.SizePresets.Length > 0
                ? BitFlowWorkshop.SizePresets[0]
                : new Vector2Int(9, 8);

            foreach (Vector2Int size in BitFlowWorkshop.SizePresets)
            {
                Vector2Int captured = size;
                string label = size.x + " x " + size.y + (size == standard ? "   STANDARD" : string.Empty);
                CreateAction(label, () => BeginWorkshop(captured), ref y);
            }

            CreateAction("BACK", BackToLevels, ref y);

            listRoot.sizeDelta = new Vector2(listRoot.sizeDelta.x, Mathf.Max(0f, -y));
        }

        void BeginSizePick()
        {
            sizePickMode = true;
            jsonViewMode = false;
            Rebuild();
        }

        void BackToLevels()
        {
            sizePickMode = false;
            jsonViewMode = false;
            Rebuild();
        }

        void RefreshList()
        {
            sizePickMode = false;
            jsonViewMode = false;
            Rebuild();
        }

        // ---------------------------------------------------------------- 文件进出

        /// <summary>
        /// 剪贴板 → 关卡目录。这就是玩家侧的「导入」：别人把 JSON 发过来，
        /// <b>复制 → 点这里 → 列表里就多一关</b>，不用去翻 AppData 里的隐藏目录，也不用文件对话框。
        ///
        /// 顺序刻意是「先验、再写」：<see cref="LevelJsonLibrary.TryParseJson"/> 的判定标准和进关时
        /// 完全一致，读不通的东西一个字节都不落盘 —— 免得列表里多出一关点进去是黑的。
        /// 落盘用的是剪贴板原文（不重新序列化），作者写的字段顺序/缩进都留着。
        /// </summary>
        void ImportFromClipboard()
        {
            string json = ReadClipboard();
            if (string.IsNullOrEmpty(json))
            {
                Flash("CLIPBOARD IS EMPTY");
                return;
            }

            if (!LevelJsonLibrary.TryParseJson(json, out LevelSO probe, out List<string> problems))
            {
                Debug.LogWarning("[BitFlow] 剪贴板里的关卡 JSON 读不通：" + string.Join("；", problems));
                Flash("NOT A LEVEL  ·  SEE CONSOLE");
                return;
            }

            // 只是验货，验完就扔（真正落盘的是剪贴板原文）
            LevelJsonLibrary.DestroyLevel(probe);

            if (!LevelJsonLibrary.TryWriteNew(LevelJsonLibrary.ImportPrefix, json, out string path, out string error))
            {
                Debug.LogWarning("[BitFlow] 关卡 JSON 写不进目录：" + error);
                Flash("IMPORT FAILED  ·  SEE CONSOLE");
                return;
            }

            Debug.Log("[BitFlow] 已导入关卡：" + path);
            Flash("IMPORTED  " + System.IO.Path.GetFileNameWithoutExtension(path));
        }

        /// <summary>
        /// 当前关卡 → 关卡目录 + 剪贴板。两件事一起做才叫「导出」：
        /// 目录里那份能在列表里直接玩、直接改；剪贴板那份能直接发给别人（对方 IMPORT 就得到同一关）。
        ///
        /// 写的是可手写格式（<see cref="LevelJsonSerializer.ToJson"/>），和仓库里的关卡文件同一种：
        /// 内置关卡也能这样导出来当模板，玩家不必从空白板开始。
        /// </summary>
        void ExportCurrent()
        {
            LevelSO level = flow != null ? flow.Current : null;
            if (level == null)
            {
                Flash("NO LEVEL LOADED");
                return;
            }

            string json = LevelJsonSerializer.ToJson(level);
            if (string.IsNullOrEmpty(json))
            {
                Flash("NOTHING TO EXPORT");
                return;
            }

            if (!LevelJsonLibrary.TryWriteNew(LevelJsonLibrary.ExportPrefix, json, out string path, out string error))
            {
                Debug.LogWarning("[BitFlow] 导出失败：" + error);
                Flash("EXPORT FAILED  ·  SEE CONSOLE");
                return;
            }

            WriteClipboard(json);
            Debug.Log("[BitFlow] 已导出关卡：" + path);
            Flash("EXPORTED  " + System.IO.Path.GetFileNameWithoutExtension(path) + "  ·  COPIED");
        }

        /// <summary>把结果行留给下一次 <see cref="Rebuild"/> 显示（顺带刷一遍列表）。</summary>
        void Flash(string message)
        {
            flash = message;
            Rebuild();
        }

        /// <summary>
        /// 删掉「当前这一关」的 JSON 文件。
        ///
        /// 只对来自文件的关卡有效：内置目录（catalog）里的关卡是打包资源，没有可删的文件。
        /// 删完刷新列表 —— 玩家要亲眼看到那一行消失，而不是怀疑按钮没生效。
        ///
        /// 两段式确认：第一次点只是"举手"，3 秒内再点才真删。文件删除不可逆，
        /// 而这个按钮就挨着 SHARE FOLDER，误触的代价太大。
        /// </summary>
        void DeleteCurrentJson()
        {
            if (flow == null) return;

            string path = flow.CurrentJsonPath;
            if (string.IsNullOrEmpty(path))
            {
                deleteArmed = false;
                Flash("ONLY MY LEVELS CAN BE DELETED");
                return;
            }

            if (!deleteArmed || Time.unscaledTime - deleteArmedAt > ConfirmSeconds)
            {
                deleteArmed = true;
                deleteArmedAt = Time.unscaledTime;
                Flash("PRESS AGAIN TO DELETE");
                return;
            }

            deleteArmed = false;

            if (!LevelJsonLibrary.TryDelete(path, out string error))
            {
                Debug.LogWarning("[BitFlow] 删不掉关卡 JSON：" + path + " → " + error);
                Flash("DELETE FAILED  ·  SEE CONSOLE");
                return;
            }

            Debug.Log("[BitFlow] 已删除关卡 JSON：" + path);
            Flash("DELETED  " + System.IO.Path.GetFileNameWithoutExtension(path));
        }

        static string ReadClipboard()
        {
            try
            {
                return GUIUtility.systemCopyBuffer;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[BitFlow] 读不了系统剪贴板：" + exception.Message);
                return null;
            }
        }

        static void WriteClipboard(string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            try
            {
                GUIUtility.systemCopyBuffer = text;
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[BitFlow] 写不了系统剪贴板：" + exception.Message);
            }
        }

        // ---------------------------------------------------------------- JSON 原文屏

        /// <summary>原文屏里每行最多几个字符。16 号像素字体每字符 10px，54×10 = 540 &lt; 面板 600。</summary>
        const int JsonLineChars = 54;

        /// <summary>原文屏行高的兜底估算（字体没就绪时用），真实高度取自 <c>Text.preferredHeight</c>。</summary>
        const float JsonLineHeight = PixelFont.Small + 4f;

        void BeginJsonView()
        {
            sizePickMode = false;
            jsonViewMode = true;
            Rebuild();
        }

        /// <summary>
        /// 「看一眼这关的 JSON」。只读：这块文本就是 EXPORT 会写进文件的那份字节，
        /// 玩家想改就 COPY 出去粘到编辑器里 —— 面板自己不提供输入框（工程里没有 InputField）。
        /// </summary>
        void RebuildJsonView()
        {
            emptyText.gameObject.SetActive(false);
            listRoot.gameObject.SetActive(true);

            float y = 0f;

            LevelSO level = flow != null ? flow.Current : null;
            string json = level != null ? LevelJsonSerializer.ToJson(level) : string.Empty;
            string name = level != null && !string.IsNullOrEmpty(level.displayName) ? level.displayName : "UNTITLED";

            CreateCaption("VIEW JSON  ·  " + name, ref y);

            if (string.IsNullOrEmpty(json))
            {
                CreateHint("NO LEVEL LOADED", ref y);
                CreateAction("BACK", BackToLevels, ref y);
                listRoot.sizeDelta = new Vector2(listRoot.sizeDelta.x, Mathf.Max(0f, -y));
                return;
            }

            // 按钮排在原文<b>上面</b>：原文动辄三四屏高，按钮放末尾的话得先滚到底才看得见，
            // 玩家会以为这一屏只有一堆字、退不出去。
            string captured = json;
            CreateAction("COPY TO CLIPBOARD", () => CopyJson(captured), ref y);
            CreateAction("BACK", BackToLevels, ref y);

            int lineCount;
            string wrapped = WrapForPanel(json, JsonLineChars, out lineCount);

            Text body = CreateText("Json", listRoot, wrapped, PixelFont.Small, TextAnchor.UpperLeft, MutedColor);
            body.horizontalOverflow = HorizontalWrapMode.Overflow;   // 已经折好了，别再折一次

            // 高度直接问 Text 要。硬编码「行数 × 行高」得赌字体的真实行高：
            // 赌大了底下多出几行空白、滚到底是一片空的，赌小了最后一行被裁掉。
            // 折行仍然自己做 —— Text 自己 Wrap 的话行数由它决定，滚动范围就对不上了。
            float height = body.preferredHeight;
            if (height <= 0f) height = lineCount * JsonLineHeight;   // 字体没就绪时的兜底估算

            SetRect(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, height), new Vector2(0f, y));
            y -= height + RowGap;   // 末尾留一行空隙，滚到底时最后一行不贴着裁切边

            listRoot.sizeDelta = new Vector2(listRoot.sizeDelta.x, Mathf.Max(0f, -y));
        }

        void CopyJson(string json)
        {
            WriteClipboard(json);
            Flash("COPIED  ·  PASTE IT ANYWHERE");
        }

        /// <summary>
        /// 把原文按每行 <paramref name="lineChars"/> 个字符硬折行（<b>不</b>在空格处折 ——
        /// JSON 的长行都是紧凑的对象，按字符折反而每行都对得齐）。
        /// 折出来的行数回填给调用方算高度。
        /// </summary>
        static string WrapForPanel(string text, int lineChars, out int lineCount)
        {
            if (lineChars < 8) lineChars = 8;

            string[] source = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            System.Text.StringBuilder builder = new System.Text.StringBuilder(text.Length + text.Length / 8);
            int lines = 0;

            foreach (string line in source)
            {
                if (line.Length <= lineChars)
                {
                    builder.Append(line).Append('\n');
                    lines++;
                    continue;
                }

                for (int i = 0; i < line.Length; i += lineChars)
                {
                    int count = Mathf.Min(lineChars, line.Length - i);
                    builder.Append(line, i, count).Append('\n');
                    lines++;
                }
            }

            lineCount = lines;
            return builder.ToString();
        }

        /// <summary>
        /// 开工坊。顺序：<b>先关面板放锁、再开工坊</b> —— 工坊期间手牌盘必须能收到点击，
        /// 所以这一路不能留着选关面板那把输入锁。
        /// </summary>
        void BeginWorkshop(Vector2Int size)
        {
            sizePickMode = false;
            Close();

            BitFlowWorkshop.Ensure().Open(size.x, size.y);
        }

        /// <summary>
        /// 画一行可点的关卡。level 与 entry 二选一：
        /// 前者是关卡表里的资产，后者是运行时从 JSON 读进来的关卡。
        /// </summary>
        void CreateRow(int row, LevelSO level, LevelJsonEntry entry, bool isCurrent, UnityAction onClick, ref float y)
        {
            GameObject rowObject = new GameObject("Row" + row, typeof(RectTransform), typeof(Image), typeof(Button));
            rowObject.transform.SetParent(listRoot, false);

            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            SetRect(rowRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, RowHeight), new Vector2(0f, y));
            y -= RowHeight + RowGap;

            Image background = rowObject.GetComponent<Image>();
            background.color = isCurrent ? RowCurrentColor : RowColor;

            Button button = rowObject.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(onClick);

            string displayName = level != null ? level.displayName : entry.DisplayName;
            string subtitle = entry != null ? "JSON  ·  " + entry.Subtitle : string.Empty;

            string label = string.IsNullOrEmpty(displayName)
                ? "LEVEL " + (row + 1).ToString("00")
                : displayName;

            Text title = CreateText("Title", rowRect, label, PixelFont.Body,
                TextAnchor.MiddleLeft, isCurrent ? TitleColor : MutedColor);

            if (string.IsNullOrEmpty(subtitle))
            {
                SetRect(title.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0f, 0.5f),
                    new Vector2(430f, RowHeight - 8f), new Vector2(22f, 0f));
            }
            else
            {
                // 两行：上面是关卡名，下面是「从哪个文件读来的」，作者一眼知道该改哪份
                SetRect(title.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(430f, 24f), new Vector2(22f, -8f));

                Text detail = CreateText("Detail", rowRect, subtitle, PixelFont.Small,
                    TextAnchor.MiddleLeft, MutedColor);
                SetRect(detail.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                    new Vector2(430f, 18f), new Vector2(22f, -32f));
            }

            // 打勾用 OK 两个字母：像素字体没有 ✓ 字形（打包后会变方块）
            if (SaveStore.IsCleared(displayName))
            {
                Text cleared = CreateText("Cleared", rowRect, "OK", PixelFont.Small, TextAnchor.MiddleCenter, ClearedColor);
                SetRect(cleared.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(40f, RowHeight - 8f), new Vector2(-22f, 0f));
            }

            if (isCurrent)
            {
                Text now = CreateText("Now", rowRect, "NOW  ", PixelFont.Small, TextAnchor.MiddleRight, MutedColor);
                SetRect(now.rectTransform, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
                    new Vector2(80f, RowHeight - 8f), new Vector2(-60f, 0f));
            }
        }

        /// <summary>分区小标题（不可点）。</summary>
        void CreateCaption(string content, ref float y)
        {
            Text caption = CreateText("Caption", listRoot, content, PixelFont.Small, TextAnchor.MiddleLeft, MutedColor);
            SetRect(caption.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, CaptionHeight), new Vector2(0f, y));
            y -= CaptionHeight;
        }

        /// <summary>灰色提示行（不可点），用来报「有错、看 Console」这类信息。</summary>
        void CreateHint(string content, ref float y)
        {
            Text hint = CreateText("Hint", listRoot, content, PixelFont.Small, TextAnchor.MiddleLeft, MutedColor);
            SetRect(hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, HintHeight), new Vector2(0f, y));
            y -= HintHeight;
        }

        /// <summary>可点的一行动作（打开目录 / 刷新列表）。标签只用 ASCII：像素字体没有中文字形。</summary>
        void CreateAction(string label, UnityAction onClick, ref float y)
        {
            GameObject rowObject = new GameObject("Action", typeof(RectTransform), typeof(Image), typeof(Button));
            rowObject.transform.SetParent(listRoot, false);

            RectTransform rowRect = rowObject.GetComponent<RectTransform>();
            SetRect(rowRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, ActionHeight), new Vector2(0f, y));
            y -= ActionHeight + RowGap;

            Image background = rowObject.GetComponent<Image>();
            background.color = RowColor;

            Button button = rowObject.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(onClick);

            Text title = CreateText("Title", rowRect, label, PixelFont.Small, TextAnchor.MiddleCenter, MutedColor);
            SetRect(title.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
        }

        /// <summary>
        /// 在资源管理器里打开「玩家自己放关卡」的目录（<c>Application.persistentDataPath/LevelJson</c>）。
        ///
        /// 为什么不做系统文件对话框：那要引 System.Windows.Forms 或写一堆平台分支，
        /// 而玩家真正需要的只有「把 json 丢进去」这一件事 —— 打开目录就够了。
        /// 移动端 / 主机没有这个概念，直接跳过（路径仍会写进 Console）。
        /// </summary>
        void OpenJsonFolder()
        {
            LevelJsonLibrary.EnsureFolder();
            string folder = LevelJsonLibrary.Folder;
            Debug.Log("[BitFlow] 关卡 JSON 目录：" + folder);

            RuntimePlatform platform = Application.platform;
            bool windows = platform == RuntimePlatform.WindowsPlayer || platform == RuntimePlatform.WindowsEditor;
            bool mac = platform == RuntimePlatform.OSXPlayer || platform == RuntimePlatform.OSXEditor;
            bool linux = platform == RuntimePlatform.LinuxPlayer || platform == RuntimePlatform.LinuxEditor;

            if (!windows && !mac && !linux) return;

            try
            {
                if (windows) System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\"");
                else if (mac) System.Diagnostics.Process.Start("open", "\"" + folder + "\"");
                else System.Diagnostics.Process.Start("xdg-open", "\"" + folder + "\"");
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning("[BitFlow] 打不开关卡目录：" + exception.Message + "（路径见上一条日志）");
            }
        }

        /// <summary>
        /// 选中某一关。顺序很讲究：<b>先</b>让黑幕盖住并拿到输入锁，<b>再</b>关掉面板放锁 ——
        /// 引用计数从 2 降到 1，全程都是锁着的，不会闪一帧让玩家点到正在拆的关卡。
        /// </summary>
        void Select(int index)
        {
            if (flow == null) return;

            LevelCatalogSO catalog = flow.Catalog;
            if (catalog == null || index < 0 || index >= catalog.Count) return;

            if (curtain != null) curtain.CoverImmediately();
            Close();

            flow.Load(index);
        }

        /// <summary>同 <see cref="Select"/>，只是关卡来自运行时 JSON 文件。</summary>
        void SelectJson(LevelJsonEntry entry)
        {
            if (flow == null || entry == null) return;

            // 先确认这份 JSON 真能读通再盖黑幕：读不通就留在面板上，
            // 否则黑幕盖住却等不到关卡，玩家会盯着一片黑。
            if (!LevelJsonLibrary.TryLoad(entry.Path, out LevelSO probe, out List<string> problems))
            {
                Debug.LogWarning("[BitFlow] 关卡 JSON 读不进来：" + entry.Path + " → " + string.Join("；", problems));
                return;
            }
            LevelJsonLibrary.DestroyLevel(probe);

            if (curtain != null) curtain.CoverImmediately();
            Close();

            flow.LoadJson(entry);
        }

        // ---------------------------------------------------------------- 搭建

        void BuildUi()
        {
            font = PixelFont.Get();

            canvasObject = new GameObject("LevelSelectCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 40;      // 压住黑幕（30）与 HUD（0）

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();

            // 遮罩本身可点：点面板外面一下就当取消（这一层也顺手把世界点击挡住）
            GameObject dimObject = new GameObject("Dim", typeof(RectTransform), typeof(Image), typeof(Button));
            dimObject.transform.SetParent(canvasRect, false);

            RectTransform dimRect = dimObject.GetComponent<RectTransform>();
            SetRect(dimRect, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);

            Image dimImage = dimObject.GetComponent<Image>();
            dimImage.color = DimColor;

            Button dimButton = dimObject.GetComponent<Button>();
            dimButton.targetGraphic = dimImage;
            dimButton.transition = Selectable.Transition.None;
            dimButton.onClick.AddListener(Close);

            GameObject panelObject = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(canvasRect, false);

            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            SetRect(panelRect, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(700f, 620f), Vector2.zero);

            Image panelImage = panelObject.GetComponent<Image>();
            panelImage.color = PanelColor;

            Text title = CreateText("Title", panelRect, "SELECT LEVEL", 32, TextAnchor.MiddleCenter, TitleColor);
            SetRect(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, 44f), new Vector2(0f, -46f));

            // 列表区裁剪 + 竖向滚动：玩家最多能存 30 关，一行 56+10，不裁剪就会画到面板外面 ——
            // 而且溢出面板但仍在屏幕内的按钮照样能点到，看起来像「面板外面也有关卡」。
            GameObject clipObject = new GameObject("ListClip", typeof(RectTransform), typeof(RectMask2D),
                typeof(ScrollRect));
            clipObject.transform.SetParent(panelRect, false);

            RectTransform clipRect = clipObject.GetComponent<RectTransform>();
            SetRect(clipRect, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, ListHeight), new Vector2(0f, -96f));

            GameObject listObject = new GameObject("List", typeof(RectTransform));
            listObject.transform.SetParent(clipRect, false);
            listRoot = listObject.GetComponent<RectTransform>();
            SetRect(listRoot, new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(600f, 0f), Vector2.zero);

            ScrollRect scroll = clipObject.GetComponent<ScrollRect>();
            scroll.viewport = clipRect;
            scroll.content = listRoot;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 40f;
            scroll.inertia = false;   // 像素 UI 带惯性会滑过头，读起来像卡了一下
            listScroll = scroll;

            // 透明的可点背景：让滚轮在行与行之间的空白处也生效，
            // 顺带把「点列表空白 = 关面板」这个误触挡掉（遮罩层还是能点）。
            Image clipImage = clipObject.AddComponent<Image>();
            clipImage.color = Color.clear;
            clipImage.raycastTarget = true;

            emptyText = CreateText("Empty", panelRect, "NO LEVELS", PixelFont.Body,
                TextAnchor.MiddleCenter, MutedColor);
            SetRect(emptyText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(600f, 40f), Vector2.zero);

            // 结果行固定贴在列表下沿和 CLOSE 之间的空档里：不进滚动区，滚到哪儿都看得见。
            statusText = CreateText("Status", panelRect, string.Empty, PixelFont.Small,
                TextAnchor.MiddleCenter, TitleColor);
            SetRect(statusText.rectTransform, new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(600f, HintHeight), new Vector2(0f, 100f));
            statusText.gameObject.SetActive(false);

            Button close = CreateButton("Close", panelRect, "CLOSE", new Vector2(160f, 44f), new Vector2(0f, 30f), Close);
            SetRect(close.GetComponent<RectTransform>(), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(160f, 44f), new Vector2(0f, 32f));
        }

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
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        Button CreateButton(string name, Transform parent, string label, Vector2 size, Vector2 position,
            UnityAction onClick)
        {
            GameObject go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            SetRect(go.GetComponent<RectTransform>(), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), size, position);

            Image image = go.GetComponent<Image>();
            image.color = new Color(0.12f, 0.20f, 0.28f, 0.95f);

            Button button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            Text text = CreateText("Label", go.transform, label, PixelFont.Body, TextAnchor.MiddleCenter, TitleColor);
            SetRect(text.rectTransform, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            return button;
        }

        static void SetRect(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 size, Vector2 position)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }
    }
}
