using UnityEngine;
using ShiftGo.BitFlow.Core;

namespace ShiftGo.BitFlow.View
{
    /// <summary>
    /// 固定机位：开场约 2 秒从高位俯瞰推近到最终机位，然后彻底锁死（不接受任何输入）。
    /// 关卡尺寸变化时自动重新计算机位，保证 9×8 与其它尺寸都能完整入画。
    /// </summary>
    public sealed class CameraRig : MonoBehaviour
    {
        [Header("推近")]
        [Tooltip("开场推近时长（秒）")]
        [SerializeField, Min(0f)] float introDuration = 2f;

        [Header("机位")]
        [Tooltip("最终机位相对网格中心的偏移")]
        [SerializeField] Vector3 finalOffset = new Vector3(0f, 9.5f, -12.5f);

        [Tooltip("开场机位相对网格中心的偏移（更远更高）")]
        [SerializeField] Vector3 overviewOffset = new Vector3(0f, 18f, -22f);

        [Tooltip("视线目标相对网格中心的抬高量")]
        [SerializeField] float lookHeight = 1.1f;

        BitFlowRunner runner;
        bool configured;
        float elapsed;

        Vector3 fromPosition;
        Quaternion fromRotation;
        Vector3 toPosition;
        Quaternion toRotation;

        void Start()
        {
            runner = FindObjectOfType<BitFlowRunner>();
        }

        void LateUpdate()
        {
            if (!configured)
            {
                if (runner == null || runner.Definition == null) return;
                Configure(runner.Definition);
            }

            if (elapsed >= introDuration)
            {
                // 锁定：之后不再动摄像机
                enabled = false;
                return;
            }

            elapsed += Time.deltaTime;
            float k = Mathf.Clamp01(introDuration <= 0f ? 1f : elapsed / introDuration);
            float eased = k * k * (3f - 2f * k);

            transform.position = Vector3.Lerp(fromPosition, toPosition, eased);
            transform.rotation = Quaternion.Slerp(fromRotation, toRotation, eased);
        }

        void Configure(LevelDefinition level)
        {
            Vector3 center = GridMetrics.GridCenter(level.Width, level.Height, lookHeight);

            toPosition = center + finalOffset;
            toRotation = Quaternion.LookRotation(center - toPosition, Vector3.up);

            fromPosition = center + overviewOffset;
            fromRotation = Quaternion.LookRotation(center - fromPosition, Vector3.up);

            transform.position = fromPosition;
            transform.rotation = fromRotation;
            configured = true;
        }
    }
}
