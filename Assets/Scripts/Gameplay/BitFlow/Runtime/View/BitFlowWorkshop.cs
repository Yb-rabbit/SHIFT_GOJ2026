using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using ShiftGo.BitFlow.Core;
using ShiftGo.BitFlow.Data;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 玩家关卡工坊：在游戏里把元件摆成一台会自己跑通的机器，存成一份能进选关列表的关卡。
    ///
    /// 它<b>不</b>新建一套编辑逻辑 —— 编辑能力全在 <see cref="BitFlowCardTray"/> 里（拾卡、幽灵预览、
    /// 放置、拾回、旋转、撤销）。工坊只做三件手牌盘不该管的事：
    ///
    /// 1. <b>换一份没有主人的草稿</b>：<see cref="LevelDraft"/> 通常由 <see cref="LevelSO.CreatePlayerDraft"/>
    ///    造出来（带 supplies = 有限手牌）。工坊直接 <c>new LevelDraft(w, h)</c>：没有任何 supplies，
    ///    于是 <see cref="LevelDraft.StockEnabled"/> 为 false、<see cref="LevelDraft.Remaining"/> 恒为 -1，
    ///    卡排上的数字自然显示 INF，玩家想摆多少摆多少。
    /// 2. <b>把底部四颗按钮换成 DONE / TEST / CLEAR / HOME</b>：见 <see cref="BitFlowHud.EnterWorkshopBar"/>。
    /// 3. <b>把关卡写进选关目录</b>：走和手工编写 JSON 完全相同的格式
    ///    （<see cref="LevelDocumentWriter"/> → <c>JsonUtility</c>），所以
    ///    <see cref="LevelJsonLibrary.Scan"/> / <see cref="LevelFlow.LoadJson"/> 一行都不用改就能认出它。
    ///
    /// 为什么 DONE 要求「能自己跑通」：这份布局就是玩家进关后看到的那台机器。
    /// 机器自己跑不通（发射器吐的位永远对不上接收器），对玩家来说就是必输关 —— 在这里拦下来，
    /// 比让他存进去再自己发现要好。
    ///
    /// 关卡里要不要发手牌由作者决定（参数小窗的 HAND 页）：发了就是「有限元件解谜」，
    /// 存档同时写出 <c>supplies</c> 和 <c>allowBuild</c>；不发就是纯展示用的机器。
    /// </summary>
    public sealed class BitFlowWorkshop : MonoBehaviour
    {
        /// <summary>可选网格尺寸（默认 9x8 = 主线的标准板）。</summary>
        public static readonly Vector2Int[] SizePresets =
        {
            new Vector2Int(9, 8),
            new Vector2Int(11, 8),
            new Vector2Int(7, 6),
            new Vector2Int(13, 9),
        };

        /// <summary>存档文件名前缀。显示名 "MY LEVEL 07" ↔ 文件名 "MY_LEVEL_07.json"。</summary>
        public const string FilePrefix = "MY_LEVEL_";

        /// <summary>最多能存多少关。到顶以后 DONE 会拒绝并提示，不会去覆盖旧关卡。</summary>
        public const int MaxLevels = 30;

        /// <summary>「再点一次确认」的有效时长（秒）。过了就当作放弃。</summary>
        const float ConfirmSeconds = 3f;

        BitFlowRunner runner;
        BitFlowCardTray tray;
        BitFlowHud hud;
        LevelSelectPanel panel;
        LevelFlow flow;
        BitFlowSetupPanel setup;

        LevelDraft draft;

        /// <summary>
        /// 正在编辑的已有 JSON 路径；新建关卡时为 null。
        /// DONE 写回这里，而不是按名字另存一份。
        /// </summary>
        string editingPath;
        int width = 9;
        int height = 8;

        bool open;
        bool testing;

        bool clearArmed;
        bool homeArmed;
        float armedAt;

        public bool IsOpen => open;
        public LevelDraft Draft => draft;
        public bool IsEditingExisting => !string.IsNullOrEmpty(editingPath);

        /// <summary>
        /// 取工坊（没有就现建一个）。场景里不一定挂了这个组件 —— 老场景是
        /// <c>BitFlowPlayerSetup</c> 生成好存下来的，改生成脚本不会回溯到已存在的场景，
        /// 所以选关面板这条入口必须能自己把自己装起来。
        /// </summary>
        public static BitFlowWorkshop Ensure()
        {
            BitFlowWorkshop existing = FindFirstObjectByType<BitFlowWorkshop>();
            if (existing != null) return existing;

            GameObject root = new GameObject("BitFlowWorkshop");
            return root.AddComponent<BitFlowWorkshop>();
        }

        void Awake()
        {
            Resolve();

            // 试跑自然结束时要把输入锁还回去，不能等玩家想起来再点一次 STOP
            if (runner != null) runner.Finished += OnRunFinished;
        }

        void Resolve()
        {
            if (runner == null) runner = FindFirstObjectByType<BitFlowRunner>();
            if (tray == null) tray = FindFirstObjectByType<BitFlowCardTray>();
            if (hud == null) hud = FindFirstObjectByType<BitFlowHud>();
            if (panel == null) panel = FindFirstObjectByType<LevelSelectPanel>();
            if (flow == null) flow = FindFirstObjectByType<LevelFlow>();
            if (setup == null) setup = FindFirstObjectByType<BitFlowSetupPanel>();
        }

        void OnDestroy()
        {
            // 试跑途中被销毁（切场景 / 关组件）：输入锁必须还回去，否则整把手牌盘永远不动
            if (testing)
            {
                testing = false;
                InputGate.Release();
            }

            if (runner != null) runner.Finished -= OnRunFinished;
            if (tray != null) tray.DraftChanged -= OnDraftChanged;
        }

        /// <summary>
        /// 试跑跑到终点：还回输入锁、把按钮改回 TEST，并报一句结论。
        /// 这里<b>不</b>重置舞台 —— 停在终点让玩家看一眼结果，比立刻清屏更有说服力。
        /// </summary>
        void OnRunFinished(LevelResult result)
        {
            if (!open || !testing) return;

            testing = false;
            InputGate.Release();
            if (hud != null) hud.SetWorkshopTesting(false);

            SetHint(result != null && result.IsSuccess ? "SOLVED  -  DONE TO SAVE" : "NOT SOLVED");
        }

        // ---------------------------------------------------------------- 开 / 关

        /// <summary>
        /// 开一张 <paramref name="width"/>×<paramref name="height"/> 的空板。
        /// 名字先按「下一个空闲编号」填入 MY LEVEL 01，作者可在参数小窗的 NAME 页改；
        /// DONE 用这个名字落盘（不再重算，免得把作者输入的名字悄悄换掉）。
        /// </summary>
        public void Open(int width, int height)
        {
            if (open) return;

            Resolve();

            this.width = Mathf.Max(3, width);
            this.height = Mathf.Max(3, height);

            string name = NextFreeName();
            if (name == null)
            {
                Debug.LogWarning("[BitFlow] 工坊：已存满 " + MaxLevels + " 关，先去选关面板整理一下再开新的");
                if (panel != null) panel.Open();
                return;
            }

            LevelDraft created = new LevelDraft(this.width, this.height) { DisplayName = name };
            Begin(created, null);
        }

        /// <summary>
        /// 打开一份已经存在的 JSON 关卡继续编辑。
        ///
        /// 和 <see cref="Open(int, int)"/> 的区别只有两点：草稿从文件读出来，以及记住
        /// <c>editingPath</c>。DONE 时写回这一份，不会在列表里多出一关 <c>_02</c>。
        ///
        /// 只接受正式关卡目录里的文件：收件箱 / 下载目录里的 json 应该先 IMPORT，
        /// 否则「编辑」会写到一个面板列表之外的地方，玩家改完却找不到。
        /// </summary>
        public bool OpenExisting(string path)
        {
            if (open) return false;

            Resolve();

            if (!LevelJsonLibrary.IsInsideLibrary(path))
            {
                Debug.LogWarning("[BitFlow] 工坊只能编辑正式关卡目录里的 JSON：" + path);
                return false;
            }

            if (!LevelJsonLibrary.TryLoad(path, out LevelSO level, out List<string> problems))
            {
                Debug.LogWarning("[BitFlow] 工坊打不开关卡 JSON：" + path + " → " + string.Join("；", problems));
                return false;
            }

            LevelDefinition definition = level.ToDefinition();
            LevelJsonLibrary.DestroyLevel(level);

            LevelDraft loaded = LevelDraft.FromDefinition(definition);
            loaded.AuthorMode = true;

            Begin(loaded, path);
            return true;
        }

        /// <summary>
        /// 两条入口（新建 / 编辑已有）共用的开场：接卡排、接参数小窗、换底部动作条。
        /// <paramref name="existingPath"/> 为 null 表示这是新关卡。
        /// </summary>
        void Begin(LevelDraft target, string existingPath)
        {
            draft = target;
            editingPath = existingPath;
            width = draft.Width;
            height = draft.Height;

            // 作者模式：锁定标记只写给玩家看，不挡作者自己（他正在做这一关）
            draft.AuthorMode = true;

            open = true;
            testing = false;
            clearArmed = false;
            homeArmed = false;

            PushDraftToRunner();

            if (tray != null)
            {
                tray.DraftChanged -= OnDraftChanged;   // 防御：上一次若没解绑，这里不该叠第二份
                tray.DraftChanged += OnDraftChanged;
                tray.EnterWorkshop(draft, Palette());
                tray.SetStatus(string.Empty);
            }

            // 参数小窗：改序列 / 次数 / 接收条件，以及发手牌。没有它，工坊只能摆形状不能调参数。
            setup = BitFlowSetupPanel.Ensure(tray, Palette(), draft);
            setup.Open();

            if (hud != null) hud.EnterWorkshopBar(RequestSave, RequestTest, RequestClear, RequestHome);
        }

        /// <summary>退出工坊：还回动作条、收起卡排、把原来那一关装回来，然后回到选关面板。</summary>
        public void Close()
        {
            if (!open) return;
            open = false;

            if (testing)
            {
                testing = false;
                InputGate.Release();
            }

            clearArmed = false;
            homeArmed = false;
            draft = null;
            editingPath = null;

            if (tray != null)
            {
                tray.DraftChanged -= OnDraftChanged;
                tray.ExitWorkshop();
            }

            if (setup != null) setup.Close();

            if (hud != null) hud.ExitWorkshopBar();

            // 工坊期间 runner 跑的一直是草稿内容。回面板之前先把真正的当前关装回来，
            // 否则玩家关掉面板看到的还是自己那张草稿（而且已经通关 / 卡住）。
            if (flow != null) flow.Retry();

            if (panel != null) panel.Open();
        }

        // ---------------------------------------------------------------- 四个动作

        /// <summary>DONE：自检通过就落盘，然后退出。</summary>
        public void RequestSave()
        {
            if (!open || draft == null) return;

            if (testing)
            {
                SetHint("STOP THE TEST FIRST");
                return;
            }

            if (draft.Count == 0)
            {
                SetHint("PLACE SOMETHING FIRST");
                return;
            }

            // 名字以 NAME 页里作者填的为准；不再每次重算 NextFreeName，
            // 否则输入框里改的名字会在按 DONE 的一瞬间被悄悄换掉。
            string name = SanitizeDisplayName(draft.DisplayName);
            if (string.IsNullOrEmpty(name))
            {
                SetHint("NAME REQUIRED  -  OPEN NAME TAB");
                return;
            }

            // 新建关卡仍受 MaxLevels 限制；编辑已有文件不占新槽位。
            // 这里只借 NextFreeName 判「还有没有空号」，不会拿它改写作者填的名字。
            if (editingPath == null && NextFreeName() == null)
            {
                SetHint("SAVE SLOTS FULL");
                return;
            }

            LevelDefinition definition = draft.ToDefinition();
            definition.DisplayName = name;

            // 存档前的必要条件自检（规则见 LevelCheck.RunForSave）：
            // 结构齐不齐、手牌是不是摆设、空位够不够放下所有牌。
            List<string> problems = LevelCheck.RunForSave(definition);
            if (problems.Count > 0)
            {
                // 中文全文进 Console（那里的字形是完整的），面板上只放得下 ASCII 结论
                Debug.Log("[BitFlow] 工坊自检未通过：" + name + "\n  " + string.Join("\n  ", problems));
                SetHint(HintFor(problems));
                return;
            }

            if (editingPath != null)
            {
                if (!TryWriteAt(editingPath, definition, out string editError))
                {
                    SetHint(editError);
                    return;
                }

                Debug.Log("[BitFlow] 工坊更新关卡：" + name + " → " + editingPath);
                // 题目改过了，旧的通关勾不再成立；否则选关面板会一直显示 OK，
                // 玩家改完一关却看不出自己还没验证过新版本。
                SaveStore.ClearJsonCleared(editingPath);
                Close();
                return;
            }

            if (!TryWrite(definition, name, out string error))
            {
                SetHint(error);
                return;
            }

            Debug.Log("[BitFlow] 工坊保存关卡：" + name + " → "
                + Path.Combine(LevelJsonLibrary.Folder, FileNameOf(name)));

            Close();
        }

        /// <summary>TEST：把草稿丢给模拟器跑一遍（再点一次 = 停下来回编辑）。</summary>
        public void RequestTest()
        {
            if (!open || draft == null) return;

            if (testing)
            {
                StopTest();
                SetHint("EDITING");
                return;
            }

            if (draft.Count == 0)
            {
                SetHint("PLACE SOMETHING FIRST");
                return;
            }

            // 拷一份最新草稿重开：上一次试跑可能停在终点，不重置就变成「接着跑」
            PushDraftToRunner();
            testing = true;

            InputGate.Acquire();   // 试跑期间手牌盘不抢点击（底部按钮是 UI，照样能点）
            if (hud != null) hud.SetWorkshopTesting(true);
            SetHint("RUNNING  -  TEST AGAIN TO STOP");

            if (runner != null) runner.Play();
        }

        /// <summary>CLEAR：清空整块板（第二次点击才真的清）。</summary>
        public void RequestClear()
        {
            if (!open || draft == null) return;

            if (draft.Count > 0 && !Confirm(ref clearArmed, "CLEAR ALL? PRESS AGAIN")) return;

            string name = draft.DisplayName;

            StopTest();
            draft = new LevelDraft(width, height) { DisplayName = name };
            draft.AuthorMode = true;   // 同上：清空之后还是作者的板

            PushDraftToRunner();
            if (tray != null) tray.EnterWorkshop(draft, Palette());
            if (setup != null) setup.SetDraft(draft);
            SetHint("CLEARED");
        }

        /// <summary>HOME：离开工坊回选关面板（板上还有东西时第二次点击才走）。</summary>
        public void RequestHome()
        {
            if (!open) return;

            if (draft != null && draft.Count > 0 && !Confirm(ref homeArmed, "LEAVE UNSAVED? PRESS AGAIN")) return;

            Close();
        }

        // ---------------------------------------------------------------- 内部

        void OnDraftChanged()
        {
            // 板子一变，「再点一次确认」的前提就不成立了（例如 CLEAR 已确认一半，玩家又摆了一件）
            clearArmed = false;
            homeArmed = false;

            // 参数小窗上的读数（张数 / 匹配结论）也跟着变；撤销会改手牌，不能只在点格子时刷新
            if (setup != null) setup.Refresh();
        }

        /// <summary>把草稿推给 runner：重建整台舞台并停在 TICK 00。手牌盘的 Apply 走的也是这一条。</summary>
        void PushDraftToRunner()
        {
            if (runner == null || draft == null) return;
            runner.ShowDefinition(draft.ToDefinition());
        }

        void StopTest()
        {
            if (!testing) return;

            testing = false;
            InputGate.Release();
            if (hud != null) hud.SetWorkshopTesting(false);

            PushDraftToRunner();   // 停下来的样子 = 回到 TICK 00 暂停，和开始试跑前一模一样
        }

        /// <summary>工坊卡排的来源：类型注册表里的全部类型（元件 + 墙）。</summary>
        static IReadOnlyList<ElementTypeSO> Palette()
        {
            LevelTypeRegistrySO registry = LevelTypeRegistrySO.Load();
            if (registry != null && registry.Types != null) return registry.Types;

            Debug.LogWarning("[BitFlow] 工坊找不到类型注册表，卡排会是空的");
            return new List<ElementTypeSO>();
        }

        void SetHint(string message)
        {
            if (tray != null) tray.SetStatus(message);
        }

        /// <summary>「再点一次确认」的两段式：第一次只记状态并提示，第二次（3 秒内）才算数。</summary>
        bool Confirm(ref bool armed, string message)
        {
            if (armed && Time.unscaledTime - armedAt <= ConfirmSeconds)
            {
                armed = false;
                return true;
            }

            // 两个确认共用一块计时：先点 CLEAR 又去点 HOME，应该只剩后点的那个在等确认
            clearArmed = false;
            homeArmed = false;
            armed = true;
            armedAt = Time.unscaledTime;

            SetHint(message);
            return false;
        }

        // ---------------------------------------------------------------- 命名 / 落盘

        /// <summary>下一个没被占用的编号（MY LEVEL 01 起）。已存满返回 null，绝不回头覆盖旧关卡。</summary>
        public static string NextFreeName()
        {
            LevelJsonLibrary.EnsureFolder();

            try
            {
                for (int number = 1; number <= MaxLevels; number++)
                {
                    string path = Path.Combine(LevelJsonLibrary.Folder, FileNameOf(DisplayNameOf(number)));
                    if (!LevelJsonLibrary.FileExists(path)) return DisplayNameOf(number);
                }
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 工坊列不了关卡目录：" + exception.Message);
                return DisplayNameOf(1);
            }

            return null;
        }

        /// <summary>"MY LEVEL 07"（面板上显示的名字）。</summary>
        public static string DisplayNameOf(int number)
        {
            return "MY LEVEL " + Mathf.Clamp(number, 1, MaxLevels).ToString("00");
        }

        /// <summary>"MY LEVEL 07" → "MY_LEVEL_07.json"（文件名里不留空格，方便玩家手打路径）。</summary>
        /// <remarks>
        /// 规则本体在 <see cref="LevelNaming"/>，这里只留转发：外部编辑器（<c>Tools/LevelStudio</c>）
        /// 编进去的是同一个文件，两边不可能各写一份。以前各写一份，编辑器存出
        /// <c>MY_LEVEL_02_01.json</c>、游戏里这关却叫 <c>111</c> —— 同一关两个名字。
        /// </remarks>
        public static string FileNameOf(string displayName)
        {
            return LevelNaming.FileNameOf(displayName);
        }

        /// <summary>
        /// 关卡名输入的统一规则：只留 ASCII 的 <c>A-Z 0-9 - _</c> 与空格，自动大写、折叠连续空格、
        /// 最长 <see cref="LevelNaming.MaxLength"/> 字符。像素字体只有 ASCII 字形，中文名存进去在面板上会是一排方块；
        /// 文件名也只由这份结果生成，所以这里同时挡掉路径分隔符与平台非法字符。
        /// </summary>
        public static string SanitizeDisplayName(string raw)
        {
            return LevelNaming.Sanitize(raw);
        }

        /// <summary>输入框逐字清理：同样只留 ASCII / 大写 / 20 字，但不折叠空格（光标不跳来跳去）。</summary>
        public static string SanitizeTypedDisplayName(string raw)
        {
            return LevelNaming.SanitizeTyped(raw);
        }

        /// <summary>
        /// 校验并改掉草稿名。重名检查覆盖三类地方：
        /// 内置目录（<see cref="SaveStore"/> 按 displayName 记通关）、正式目录里的 JSON、
        /// 以及文件名本身 —— 任何一处撞上都不让存，免得两关在列表里看着一模一样。
        /// </summary>
        public bool TryRenameDraft(string raw, out string error)
        {
            error = null;

            if (!open || draft == null)
            {
                error = "WORKSHOP IS CLOSED";
                return false;
            }

            string name = SanitizeDisplayName(raw);
            if (string.IsNullOrEmpty(name))
            {
                error = "NAME REQUIRED";
                return false;
            }

            if (string.Equals(name, draft.DisplayName, StringComparison.OrdinalIgnoreCase))
            {
                draft.DisplayName = name;   // 规范化（大小写 / 连续空格）也算一次修改
                return true;
            }

            if (NameTaken(name))
            {
                error = "NAME TAKEN  -  PICK ANOTHER";
                return false;
            }

            draft.DisplayName = name;
            if (setup != null) setup.Refresh();
            return true;
        }

        bool NameTaken(string candidate)
        {
            string candidatePath = Path.Combine(LevelJsonLibrary.Folder, FileNameOf(candidate));

            // 自己正在编辑的那份文件不算重名：作者可以只改 displayName，不必改文件名。
            if (!SamePath(candidatePath, editingPath) && LevelJsonLibrary.FileExists(candidatePath)) return true;

            if (flow != null && flow.Catalog != null)
            {
                for (int i = 0; i < flow.Catalog.Count; i++)
                {
                    LevelSO level = flow.Catalog.Get(i);
                    if (level == null || string.IsNullOrEmpty(level.displayName)) continue;
                    if (string.Equals(level.displayName, candidate, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }

            List<LevelJsonEntry> entries = LevelJsonLibrary.Scan(out List<string> problems);
            foreach (string problem in problems) Debug.LogWarning("[BitFlow] 关卡 JSON：" + problem);

            foreach (LevelJsonEntry entry in entries)
            {
                if (SamePath(entry.Path, editingPath)) continue;
                if (string.Equals(entry.DisplayName, candidate, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        static bool SamePath(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;

            // 浏览器里没有「当前目录」：Path.GetFullPath 给出的结果和别处不一致，
            // 而两边路径都是 LevelJsonLibrary.Folder + 文件名 拼出来的，直接按字符串比更可靠。
            if (BrowserFileSystem.IsActive)
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }

            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// 所以 <see cref="LevelJsonLibrary.Scan"/> 直接就能读，不存在「工坊存档要另写一套解析」的事。
        /// </summary>
        static bool TryWrite(LevelDefinition definition, string displayName, out string error)
        {
            error = null;

            try
            {
                LevelJsonLibrary.EnsureFolder();

                string path = Path.Combine(LevelJsonLibrary.Folder, FileNameOf(displayName));
                if (LevelJsonLibrary.FileExists(path))
                {
                    error = "NAME TAKEN  -  PICK ANOTHER";
                    return false;
                }

                return WriteDocument(definition, path, out error);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 工坊保存失败：" + exception.Message);
                error = "SAVE FAILED - SEE LOG";
                return false;
            }
        }

        /// <summary>编辑已有 JSON 时写回原路径。只允许正式关卡目录，避免把文件写到列表之外。</summary>
        static bool TryWriteAt(string path, LevelDefinition definition, out string error)
        {
            error = null;

            if (!LevelJsonLibrary.IsInsideLibrary(path))
            {
                error = "NOT A MY LEVELS FILE";
                return false;
            }

            try
            {
                LevelJsonLibrary.EnsureFolder();
                return WriteDocument(definition, path, out error);
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 工坊保存失败：" + exception.Message);
                error = "SAVE FAILED - SEE LOG";
                return false;
            }
        }

        static bool WriteDocument(LevelDefinition definition, string path, out string error)
        {
            error = null;

            List<string> problems = new List<string>();
            LevelDocument document = LevelDocumentWriter.FromDefinition(definition, problems);

            foreach (string problem in problems)
            {
                Debug.LogWarning("[BitFlow] 工坊写关卡时跳过：" + problem);
            }

            // wallRows 为 null 时 JsonUtility 会写成 "wallRows": null，
            // 读回来照样走「没有墙层」那条分支，不用特意补一个空数组。
            // 走 LevelJsonLibrary 的包装：桌面写文件，WebGL 写浏览器存储。
            LevelJsonLibrary.WriteAllText(path, JsonUtility.ToJson(document, true));
            return true;
        }

        // ---------------------------------------------------------------- 自检失败说人话

        /// <summary>
        /// 自检问题的关键词 → 面板上的一句话结论（<b>ASCII</b>）。
        ///
        /// 中文全文已经打进 Console 了，而 UI 是像素字体（没有中文字形，打包后全是方块），
        /// 所以这里只挑结论。顺序 = 严重程度：先报"缺件"这种一眼能补的，
        /// 再报"预设布局自己就通了"这种要重新想题目的。
        /// </summary>
        static string HintFor(List<string> problems)
        {
            string joined = string.Join("；", problems);

            if (joined.Contains("没有接收器")) return "ADD A RECEIVER";
            if (joined.Contains("没有发射器")) return "ADD AN EMITTER";
            if (joined.Contains("手牌是摆设")) return "PRESET ALREADY SOLVES IT";
            if (joined.Contains("手牌里有")) return "NO EMITTER / RECEIVER CARDS";
            if (joined.Contains("空位")) return "TOO MANY CARDS FOR THE BOARD";

            // 「跑到 N tick 上限还没结算」是唯一一种「不是算错、是断路」的失败
            if (joined.Contains("上限")) return "STUCK - CHECK THE ROUTE";

            return "NOT SOLVED - CHECK THE ROUTE";
        }
    }
}
