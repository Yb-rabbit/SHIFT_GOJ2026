using UnityEngine;

/// <summary>
/// 可推物体标记。GridStepper 依靠这个组件区分「箱子」和「墙」。
/// 只有挂了这个组件的碰撞体才会被推动。
/// </summary>
[DisallowMultipleComponent]
public class Pushable : MonoBehaviour
{
}
