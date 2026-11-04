using System.Collections;
using UnityEngine;

/// <summary>
/// 象棋式单步移动控制：一次按键走一格。
/// 规则：
/// 1. 只允许上下左右四个方向，位置始终吸附在整数格点上；
/// 2. 目标格是墙 → 不动；
/// 3. 目标格是箱子（Pushable）→ 箱子前方为空则人箱各前进一格，否则整体不动；
/// 4. 移动过程中忽略新输入；不使用 Rigidbody，判定完全依赖 OverlapBox 网格查询。
/// </summary>
[DisallowMultipleComponent]
public class GridStepper : MonoBehaviour
{
    [Header("网格")]
    [Tooltip("单格边长（世界单位）")]
    [SerializeField] float cellSize = 1f;

    [Tooltip("格子占用检测的立方体半径，必须小于半格")]
    [SerializeField, Range(0.1f, 0.49f)] float probeRadius = 0.4f;

    [Header("步进表现")]
    [Tooltip("从一格滑到下一格的时间（秒），0 表示瞬间跳格")]
    [SerializeField, Range(0f, 0.5f)] float stepDuration = 0.12f;

    [Header("按键")]
    [SerializeField] KeyCode forwardKey = KeyCode.W;
    [SerializeField] KeyCode backwardKey = KeyCode.S;
    [SerializeField] KeyCode leftKey = KeyCode.A;
    [SerializeField] KeyCode rightKey = KeyCode.D;
    [Tooltip("是否同时支持方向键")]
    [SerializeField] bool alsoUseArrowKeys = true;

    bool moving;
    Vector3Int cell;

    /// <summary>当前所在的逻辑格子（y 恒为 0，只关心 x/z）。</summary>
    public Vector3Int CurrentCell => cell;

    /// <summary>是否正处在一格的滑动动画中。</summary>
    public bool IsMoving => moving;

    void Awake()
    {
        if (cellSize <= 0f) cellSize = 1f;
        SnapToGrid();
    }

    void Update()
    {
        if (moving) return;

        if (ReadDirection(out Vector3Int direction))
        {
            TryStep(direction);
        }
    }

    /// <summary>朝相邻格子走一步。返回本次指令是否被接受（被墙/箱子挡住时返回 false）。</summary>
    public bool TryStep(Vector3Int direction)
    {
        // 一次只能走一格，且只允许四方向之一
        if (moving || Mathf.Abs(direction.x) + Mathf.Abs(direction.z) != 1) return false;

        // 上一步是直接改 transform.position 完成的，必须同步一次物理变换，
        // 否则本帧的 OverlapBox 读到的还是旧位置的碰撞体（autoSyncTransforms 默认关闭）。
        Physics.SyncTransforms();

        Vector3Int next = cell + direction;

        // 情况一：目标格有箱子 → 尝试把箱子顶向更前面一格
        Transform box = FindPushable(next);
        if (box != null)
        {
            Vector3Int boxNext = next + direction;
            if (IsBlocked(boxNext)) return false;   // 箱子后面是墙或另一个箱子：人箱都不动

            cell = next;
            StartCoroutine(Animate(next, box, boxNext));
            return true;
        }

        // 情况二：目标格被墙占据 → 不动
        if (IsBlocked(next)) return false;

        cell = next;
        StartCoroutine(Animate(next, null, default));
        return true;
    }

    /// <summary>线性插值：玩家与（可选的）箱子同时滑向目标格。</summary>
    IEnumerator Animate(Vector3Int playerCell, Transform box, Vector3Int boxCell)
    {
        moving = true;

        Vector3 playerFrom = transform.position;
        Vector3 playerTo = CellToWorld(playerCell);

        Vector3 boxFrom = box ? box.position : Vector3.zero;
        Vector3 boxTo = box
            ? new Vector3(boxCell.x * cellSize, box.position.y, boxCell.z * cellSize)
            : Vector3.zero;

        float duration = Mathf.Max(0f, stepDuration);
        for (float time = 0f; time < duration; time += Time.deltaTime)
        {
            float k = time / duration;
            transform.position = Vector3.Lerp(playerFrom, playerTo, k);
            if (box) box.position = Vector3.Lerp(boxFrom, boxTo, k);
            yield return null;
        }

        // 末端强制吸附，避免浮点误差随步数累积
        transform.position = playerTo;
        if (box) box.position = boxTo;

        moving = false;
    }

    /// <summary>把逻辑格子换算成世界坐标（只替换 x/z，保持自己当前高度）。</summary>
    Vector3 CellToWorld(Vector3Int c)
    {
        return new Vector3(c.x * cellSize, transform.position.y, c.z * cellSize);
    }

    /// <summary>查找指定格子里的可推物体。</summary>
    Transform FindPushable(Vector3Int c)
    {
        Collider[] hits = Physics.OverlapBox(CellToWorld(c), Vector3.one * probeRadius, Quaternion.identity);
        foreach (Collider hit in hits)
        {
            Pushable pushable = hit.GetComponentInParent<Pushable>();
            if (pushable != null) return pushable.transform;
        }
        return null;
    }

    /// <summary>指定格子里是否有任何障碍（墙、箱子）。</summary>
    bool IsBlocked(Vector3Int c)
    {
        return Physics.OverlapBox(CellToWorld(c), Vector3.one * probeRadius, Quaternion.identity).Length > 0;
    }

    /// <summary>读取一次单步方向输入（WASD + 方向键）。</summary>
    bool ReadDirection(out Vector3Int direction)
    {
        if (Input.GetKeyDown(forwardKey) || (alsoUseArrowKeys && Input.GetKeyDown(KeyCode.UpArrow)))
        {
            direction = Vector3Int.forward;
            return true;
        }
        if (Input.GetKeyDown(backwardKey) || (alsoUseArrowKeys && Input.GetKeyDown(KeyCode.DownArrow)))
        {
            direction = Vector3Int.back;
            return true;
        }
        if (Input.GetKeyDown(leftKey) || (alsoUseArrowKeys && Input.GetKeyDown(KeyCode.LeftArrow)))
        {
            direction = Vector3Int.left;
            return true;
        }
        if (Input.GetKeyDown(rightKey) || (alsoUseArrowKeys && Input.GetKeyDown(KeyCode.RightArrow)))
        {
            direction = Vector3Int.right;
            return true;
        }

        direction = Vector3Int.zero;
        return false;
    }

    /// <summary>把初始位置吸附到最近的格点。</summary>
    void SnapToGrid()
    {
        cell = new Vector3Int(
            Mathf.RoundToInt(transform.position.x / cellSize),
            0,
            Mathf.RoundToInt(transform.position.z / cellSize));
        transform.position = CellToWorld(cell);
    }
}
