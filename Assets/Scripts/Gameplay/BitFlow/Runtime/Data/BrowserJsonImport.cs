using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ShiftGo.BitFlow.Data
{
    /// <summary>
    /// 浏览器（WebGL）里的「导入一份 json / 导出一份 json」。
    ///
    /// 桌面端完全不参与：<see cref="IsSupported"/> 为 false，调用方继续走
    /// <see cref="FilePicker"/> 和分享目录，行为一个字节都不变。
    ///
    /// 浏览器里既没有文件夹也没有系统文件对话框，唯一合规的入口是网页自己的控件：
    /// 进 = <c>&lt;input type="file"&gt;</c>，出 = <c>a[download]</c>。
    /// 两者都实现在 <c>Assets/Plugins/WebGL/BitFlowBrowserIo.jslib</c>，
    /// 这个类只负责「叫它」和「接住回传的文本」。
    ///
    /// 回传是异步的（FileReader 读完才发），所以 <see cref="PickJson"/> 收的是回调而不是返回值：
    /// 调用方必须接受「点了之后过几帧才出结果」。
    /// </summary>
    public sealed class BrowserJsonImport : MonoBehaviour
    {
        /// <summary>jslib 里 SendMessage 的目标对象名；改这里必须同步改 jslib。</summary>
        const string BridgeName = "BitFlowWebGLBridge";

        /// <summary>jslib 用这个控制字符把「文件名」和「文件正文」拼成一个参数（JSON 里不会出现它）。</summary>
        const char FieldSeparator = '\u0001';

        /// <summary>浏览器构建才为 true；编辑器里即使目标是 WebGL 也走桌面那套。</summary>
        public static bool IsSupported
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        static BrowserJsonImport instance;

        /// <summary>等着接收下一次回传的回调：<c>(fileName, json)</c>；取消时两者都是 null。</summary>
        static Action<string, string> pending;

        /// <summary>上一次浏览器读文件失败的原因（给面板决定显示哪条文案）。</summary>
        public static string LastError { get; private set; }

        /// <summary>保证场景里有那个固定名字的桥对象：没有就新建，跨场景常驻。</summary>
        static BrowserJsonImport Ensure()
        {
            if (instance != null) return instance;

            GameObject host = new GameObject(BridgeName);
            DontDestroyOnLoad(host);
            instance = host.AddComponent<BrowserJsonImport>();
            return instance;
        }

        /// <summary>
        /// 弹浏览器的文件选择器，读完把 (文件名, 文本) 交给 <paramref name="onPicked"/>。
        /// 玩家取消时回调收到 <c>(null, null)</c> —— 调用方据此说「已取消」而不是「导入失败」。
        /// </summary>
        public static bool PickJson(Action<string, string> onPicked)
        {
            if (!IsSupported) return false;

            Ensure();
            pending = onPicked;
            LastError = null;

#if UNITY_WEBGL && !UNITY_EDITOR
            BitFlow_PickJsonFile();
#endif
            return true;
        }

        /// <summary>把一段文本变成浏览器的下载（文件名建议 <c>EXPORT_01.json</c>）。</summary>
        public static bool DownloadJson(string fileName, string json)
        {
            if (!IsSupported || string.IsNullOrEmpty(json)) return false;

            if (string.IsNullOrEmpty(fileName)) fileName = "level.json";
            if (!fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) fileName += ".json";

#if UNITY_WEBGL && !UNITY_EDITOR
            BitFlow_DownloadText(fileName, json);
            return true;
#else
            return false;
#endif
        }

        // ---------------------------------------------------------------- jslib 回传

        /// <summary>jslib 读完文件后调这里（SendMessage，参数不能为空）。</summary>
        public void OnJsonPicked(string payload)
        {
            Action<string, string> callback = pending;
            pending = null;

            if (payload == null) payload = string.Empty;

            int split = payload.IndexOf(FieldSeparator);
            string fileName = split >= 0 ? payload.Substring(0, split) : string.Empty;
            string json = split >= 0 ? payload.Substring(split + 1) : payload;

            if (callback != null) callback(fileName, json);
        }

        /// <summary>玩家在文件选择器里点了取消。</summary>
        public void OnJsonPickCancelled(string _)
        {
            Action<string, string> callback = pending;
            pending = null;
            if (callback != null) callback(null, null);
        }

        /// <summary>浏览器读文件失败（权限 / 编码 / 文件中途被挪走）。</summary>
        public void OnJsonPickFailed(string fileName)
        {
            LastError = "读不了这个文件：" + fileName;
            Debug.LogWarning("[BitFlow] 浏览器读文件失败：" + fileName);

            Action<string, string> callback = pending;
            pending = null;
            if (callback != null) callback(null, null);
        }

#if UNITY_WEBGL && !UNITY_EDITOR
        [DllImport("__Internal")]
        static extern void BitFlow_PickJsonFile();

        [DllImport("__Internal")]
        static extern void BitFlow_DownloadText(string fileName, string text);
#endif
    }
}