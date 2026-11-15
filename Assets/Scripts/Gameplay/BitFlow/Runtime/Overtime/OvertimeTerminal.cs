using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;
using ShiftGo.BitFlow.Data;
using ShiftGo.BitFlow.View;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// OVERTIME 的唯一交互入口。它不挂在选关面板上，也不修改选关面板：
    /// 由 bootstrap 自动创建，打开时先调用 LevelSelectPanel.Close()，再用自己的 Canvas 接管输入。
    /// </summary>
    public sealed class OvertimeTerminal : MonoBehaviour
    {
        enum Phase
        {
            Closed,
            Intro,
            Idle,
            Generating,
            Briefing,
            Error,
        }

        static readonly Color AlarmColor = TerminalPalette.AlarmColor;
        static readonly Color AccentColor = TerminalPalette.EmitColor;

        bool open;
        Phase phase = Phase.Closed;
        OvertimeIdleView view;
        OvertimeState state;
        string pendingPath;

        public bool IsOpen => open;

        public static OvertimeTerminal Ensure()
        {
            OvertimeTerminal existing = FindFirstObjectByType<OvertimeTerminal>();
            if (existing != null) return existing;

            Transform root = OvertimeBootstrap.GetOrCreateRoot();
            GameObject go = new GameObject("OvertimeTerminal");
            go.transform.SetParent(root, false);
            return go.AddComponent<OvertimeTerminal>();
        }

        void Awake()
        {
            DontDestroyOnLoad(gameObject);
        }

        void OnDestroy()
        {
            if (open) InputGate.Release();
        }

        public void Open()
        {
            if (open) return;

            LevelSelectPanel panel = FindFirstObjectByType<LevelSelectPanel>();
            if (panel != null) panel.Close();

            open = true;
            phase = Phase.Idle;
            InputGate.Acquire();

            state = OvertimeStateStore.LoadOrRebuild(out _);

            view = OvertimeIdleView.Create(transform);
            view.Show();

            if (!state.introSeen)
            {
                state.introSeen = true;
                OvertimeStateStore.Save(state);
                ShowIntro();
            }
            else
            {
                ShowIdle();
            }
        }

        public void Close()
        {
            if (!open) return;

            StopAllCoroutines();
            open = false;
            phase = Phase.Closed;
            pendingPath = null;

            if (view != null)
            {
                Destroy(view.gameObject);
                view = null;
            }

            InputGate.Release();
        }

        void Update()
        {
            if (!open) return;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                EscapeInput.Consume();
                Close();
                return;
            }

            bool confirm = Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.Space);

            if (!confirm) return;

            switch (phase)
            {
                case Phase.Intro:
                    ShowIdle();
                    break;

                case Phase.Idle:
                    if (state != null && state.CanGenerate) StartCoroutine(GenerateRoutine());
                    break;

                case Phase.Briefing:
                    PlayPending();
                    break;

                case Phase.Error:
                    ShowIdle();
                    break;
            }
        }
        void ShowIntro()
        {
            if (view == null) return;

            phase = Phase.Intro;
            view.SetContent(
                OvertimeTerminalScripts.IntroTitle,
                OvertimeTerminalScripts.IntroBody(),
                "ENTER  CONNECT     ESC  DISCONNECT",
                AccentColor,
                TerminalPalette.MutedColor);
        }

        void ShowIdle()
        {
            if (view == null) return;

            phase = Phase.Idle;
            pendingPath = null;

            bool canGenerate = state != null && state.CanGenerate;
            string body = OvertimeTerminalScripts.IdleBody(state);

            view.SetContent(
                OvertimeTerminalScripts.IdleTitle,
                body,
                canGenerate ? "ENTER  CONSTRUCT     ESC  DISCONNECT" : "NO PROBLEMS LEFT     ESC  DISCONNECT",
                TerminalPalette.BodyColor,
                canGenerate ? TerminalPalette.MutedColor : AlarmColor);
        }

        IEnumerator GenerateRoutine()
        {
            phase = Phase.Generating;
            int index = state.nextProblem;

            view.SetContent(
                "OVERTIME // CONSTRUCTING",
                OvertimeTerminalScripts.ConstructingBody(index),
                "ESC  ABORT",
                AccentColor,
                TerminalPalette.MutedColor);

            yield return null;

            Stopwatch watch = Stopwatch.StartNew();
            bool generated = OvertimeFactory.TryGenerate(index, state.baseSeed, state.minTicks,
                out OvertimeGenerationResult result, out string error);
            if (!generated)
            {
                ShowError(error);
                yield break;
            }

            if (!OvertimeLevelWriter.TrySave(result.Level, index, out string path, out error))
            {
                ShowError(error);
                yield break;
            }

            state.problems.Add(new OvertimeProblemRecord
            {
                index = index,
                displayName = OvertimeConfig.DisplayName(index),
                fileName = OvertimeConfig.FileName(index),
                width = result.Level.Width,
                height = result.Level.Height,
                seed = result.Profile.Seed,
                targetSteps = result.Layout.StepCount,
                ticks = result.Ticks,
                createdAt = DateTime.UtcNow.ToString("o"),
            });
            state.Normalize();
            OvertimeStateStore.Save(state);

            pendingPath = path;
            phase = Phase.Briefing;

            view.SetContent(
                OvertimeTerminalScripts.ReadyTitle,
                OvertimeTerminalScripts.BriefingBody(result, watch.ElapsedMilliseconds),
                "ENTER  PLAY     ESC  DISCONNECT",
                TerminalPalette.RecvColor,
                TerminalPalette.MutedColor);
        }

        void ShowError(string message)
        {
            phase = Phase.Error;
            pendingPath = null;

            view.SetContent(
                OvertimeTerminalScripts.ErrorTitle,
                OvertimeTerminalScripts.ErrorBody(message),
                "ENTER  RETURN     ESC  DISCONNECT",
                AlarmColor,
                AlarmColor);
        }

        void PlayPending()
        {
            if (string.IsNullOrEmpty(pendingPath)) return;

            string path = pendingPath;
            LevelFlow flow = FindFirstObjectByType<LevelFlow>();
            Close();

            if (flow == null)
            {
                UnityEngine.Debug.LogWarning("[BitFlow] OVERTIME 找不到 LevelFlow，无法进入关卡");
                return;
            }

            if (!flow.LoadJson(path))
                UnityEngine.Debug.LogWarning("[BitFlow] OVERTIME 关卡加载失败：" + path);
        }

    }
}
