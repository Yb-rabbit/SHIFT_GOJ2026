using UnityEngine;

namespace ShiftGo.BitFlow.Overtime
{
    /// <summary>
    /// 零外部挂载的启动器：场景加载后自动建 OvertimeRoot，再挂上入口按钮。
    /// 不修改场景，也不改 LevelSelectPanel；入口只在选关面板自己可见时出现。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class OvertimeBootstrap : MonoBehaviour
    {
        static OvertimeBootstrap instance;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void CreateOnLoad()
        {
            if (FindFirstObjectByType<OvertimeBootstrap>() != null) return;
            GetOrCreateRoot();
        }

        public static Transform GetOrCreateRoot()
        {
            if (instance != null) return instance.transform;

            OvertimeBootstrap found = FindFirstObjectByType<OvertimeBootstrap>();
            if (found != null)
            {
                instance = found;
                return found.transform;
            }

            GameObject root = new GameObject("OvertimeRoot");
            DontDestroyOnLoad(root);
            instance = root.AddComponent<OvertimeBootstrap>();
            return root.transform;
        }

        void Awake()
        {
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);
            gameObject.AddComponent<OvertimeEntryOverlay>();
        }
    }
}
