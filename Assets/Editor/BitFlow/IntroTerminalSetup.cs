using ShiftGo.BitFlow.View;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShiftGo.EditorTools
{
    /// <summary>
    /// 把开场终端演出挂进场景的一键菜单。
    ///
    /// 为什么要菜单而不是让作者自己拖组件：<see cref="IntroTerminal"/> 必须和
    /// <see cref="IntroCurtain"/> 同时存在于<b>同一个场景</b>（它靠 WaitForCue 抢在黑幕前面演），
    /// 而执行顺序、Canvas 层级这些坑都不会在 Inspector 里报错 —— 只表现为"演出被黑幕挡住看不见"。
    /// 菜单一步挂好并当场校验场景里有没有可接管的黑幕。
    /// </summary>
    public static class IntroTerminalSetup
    {
        [MenuItem("SHIFT GO/Setup/把开场终端挂到当前场景", false, 92)]
        internal static void AttachToActiveScene()
        {
            Scene scene = SceneManager.GetActiveScene();

            // FindFirstObjectByType 默认只找激活对象（FindObjectsInactive.Exclude），
            // 和旧的 FindObjectOfType 行为一致；后者在 2023.2 已标记过时。
            IntroTerminal existing = Object.FindFirstObjectByType<IntroTerminal>();
            if (existing != null)
            {
                Selection.activeGameObject = existing.gameObject;
                Debug.Log("[BitFlow] 当前场景里已经有 IntroTerminal（" + existing.name + "），没有重复添加。", existing);
                return;
            }

            GameObject host = new GameObject("IntroTerminal");
            Undo.RegisterCreatedObjectUndo(host, "挂开场终端");

            IntroTerminal terminal = host.AddComponent<IntroTerminal>();
            Undo.RegisterCompleteObjectUndo(terminal, "挂开场终端");

            Selection.activeGameObject = host;

            if (!scene.isDirty) EditorSceneManager.MarkSceneDirty(scene);

            if (Object.FindFirstObjectByType<IntroCurtain>() == null)
            {
                Debug.LogWarning("[BitFlow] 已挂上 IntroTerminal，但当前场景里没有 IntroCurtain —— " +
                    "终端演完之后没人接场，会直接放开输入进关卡。想要「终端 → 关卡黑幕」的完整衔接，" +
                    "请到 Test_3_BitFlowPlayer 这类已经挂了 IntroCurtain 的场景里用这个菜单。", host);
                return;
            }

            Debug.Log("[BitFlow] 已挂上 IntroTerminal：Play 即可看开场演出。" +
                "记得 Ctrl+S 保存场景，否则关闭后这次挂载会丢。", host);
        }

        [MenuItem("SHIFT GO/Setup/把开场终端挂到当前场景", true, 92)]
        static bool AttachToActiveSceneValidate()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }
    }
}
