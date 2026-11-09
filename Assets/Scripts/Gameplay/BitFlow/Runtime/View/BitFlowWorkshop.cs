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
    /// 为什么 DONE 要求「能自己跑通」：工坊存出来的关卡里<b>没有 supplies</b>
    /// （<see cref="LevelDocumentWriter"/> 只写 placements），玩家进关按 PLAY 就是看这台机器跑。
    /// 所以一份自己跑不通的布局，对玩家来说就是必输关 —— 在这里拦下来，比让他存进去再自己发现要好。
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

        LevelDraft draft;
        int width = 9;
        int height = 8;

        bool open;
        bool testing;

        bool clearArmed;
        bool homeArmed;
        float armedAt;

        public bool IsOpen => open;
        public LevelDraft Draft => draft;

        /// <summary>
        /// 取工坊（没有就现建一个）。场景里不一定挂了这个组件 —— 老场景是
        /// <c>BitFlowPlayerSetup</c> 生成好存下来的，改生成脚本不会回溯到已存在的场景，
        /// 所以选关面板这条入口必须能自己把自己装起来。
        /// </summary>
        public static BitFlowWorkshop Ensure()
        {
            BitFlowWorkshop existing = FindObjectOfType<BitFlowWorkshop>();
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
            if (runner == null) runner = FindObjectOfType<BitFlowRunner>();
            if (tray == null) tray = FindObjectOfType<BitFlowCardTray>();
            if (hud == null) hud = FindObjectOfType<BitFlowHud>();
            if (panel == null) panel = FindObjectOfType<LevelSelectPanel>();
            if (flow == null) flow = FindObjectOfType<LevelFlow>();
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
        /// 名字此时就定下来（MY LEVEL 01），存盘时会再算一次以防期间别处多了文件。
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

            draft = new LevelDraft(this.width, this.height) { DisplayName = name };

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

            if (tray != null)
            {
                tray.DraftChanged -= OnDraftChanged;
                tray.ExitWorkshop();
            }

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

            // 名字每次重算：工坊开着的这段时间里，别处可能又存了一关
            string name = NextFreeName();
            if (name == null)
            {
                SetHint("SAVE SLOTS FULL");
                return;
            }

            LevelDefinition definition = draft.ToDefinition();
            definition.DisplayName = name;

            string report = LevelCheck.Run(definition, out bool success);
            if (!success)
            {
                // 详细报告是中文的（给作者看 Console 用），面板上是像素字体，只放得下 ASCII 结论
                Debug.Log("[BitFlow] 工坊自检未通过：" + name + "\n" + report);
                SetHint(HintFor(definition, report));
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

            PushDraftToRunner();
            if (tray != null) tray.EnterWorkshop(draft, Palette());
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
                    if (!File.Exists(path)) return DisplayNameOf(number);
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
        public static string FileNameOf(string displayName)
        {
            return (displayName ?? string.Empty).Replace(' ', '_') + ".json";
        }

        /// <summary>
        /// 写进选关目录。格式和作者手写的 JSON 完全一致（同一个 <see cref="LevelDocumentWriter"/>），
        /// 所以 <see cref="LevelJsonLibrary.Scan"/> 直接就能读，不存在「工坊存档要另写一套解析」的事。
        /// </summary>
        static bool TryWrite(LevelDefinition definition, string displayName, out string error)
        {
            error = null;

            try
            {
                LevelJsonLibrary.EnsureFolder();

                List<string> problems = new List<string>();
                LevelDocument document = LevelDocumentWriter.FromDefinition(definition, problems);

                foreach (string problem in problems)
                {
                    Debug.LogWarning("[BitFlow] 工坊写关卡时跳过：" + problem);
                }

                // wallRows 为 null 时 JsonUtility 会写成 "wallRows": null，
                // 读回来照样走「没有墙层」那条分支，不用特意补一个空数组。
                string path = Path.Combine(LevelJsonLibrary.Folder, FileNameOf(displayName));
                File.WriteAllText(path, JsonUtility.ToJson(document, true));
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogWarning("[BitFlow] 工坊保存失败：" + exception.Message);
                error = "SAVE FAILED - SEE LOG";
                return false;
            }
        }

        // ---------------------------------------------------------------- 自检失败说人话

        /// <summary>
        /// <see cref="LevelCheck.Run"/> 的报告是中文 + emoji（给作者看 Console 用的），
        /// 而 UI 是像素字体（<b>没有中文字形</b>，打包后全是方块），所以这里只挑一条 ASCII 结论。
        /// </summary>
        static string HintFor(LevelDefinition definition, string report)
        {
            int receivers = 0;
            int emitters = 0;

            foreach (ElementDefinition element in definition.Elements)
            {
                if (element is ReceiverDefinition) receivers++;
                else if (element is EmitterDefinition) emitters++;
            }

            if (receivers == 0) return "ADD A RECEIVER";
            if (emitters == 0) return "ADD AN EMITTER";

            // 「跑到 N tick 上限还没结算，可能有个位卡住了」是唯一一种「不是算错、是断路」的失败
            if (!string.IsNullOrEmpty(report) && report.Contains("上限")) return "STUCK - CHECK THE ROUTE";

            return "NOT SOLVED - CHECK THE ROUTE";
        }
    }
}
