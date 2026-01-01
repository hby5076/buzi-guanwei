using UnityEngine;
using System.Collections.Generic;
using Solo.MOST_IN_ONE;
using NUnit.Framework; // 引用震动插件

public class PanjinTracingManager : MonoBehaviour
{
    [Header("引用")]
    public PolygonCollider2D outlineCollider; // 拖入瑞兽的Collider
    public Transform needlePoint;             // 拖入你的光点物体

    [Header("判定参数")]
    public float detectThreshold = 0.3f;      // 判定半径（世界单位）
    
    [Header("进度状态")]
    public int nextPointIndex = 0;           // 下一个待达成的目标点索引
    private Vector3[] _worldPathPoints;      // 存储转换后的世界坐标点
    private bool _isCompleted = false;

    [Header("轮廓起始点语义")]
    public Transform startAnchor; // 拖一个空物体到瑞兽头部


    public Transform targetGlowObj;
    public SpriteRenderer ruishouRenderer;
    public Color startColor = new Color(0.2f, 0.2f, 0.2f, 1f);

    [UnityEngine.Range(0f, 1f)]
    public float startAlpha = 0.35f;
    [UnityEngine.Range(0f, 1f)]
    public float endAlpha = 1f;

    void Start()
    {
        // 1. 提取并转换轮廓点
        if (outlineCollider == null) outlineCollider = GetComponent<PolygonCollider2D>();
        ExtractWorldPoints();
        ReorderPathPointsByAnchor();

        // 2. 订阅 InputManager 事件进行逻辑判定
        if (InputManager_Scene1.Instance != null)
        {
            // 每次光点移动时，我们检查它是否触碰了当前目标点
            InputManager_Scene1.Instance.OnNeedlePositionMoved += CheckTracingProgress;
        }

        InitRuishouVisual();
        UpdateTargetGlow();
    }

    private void InitRuishouVisual()
    {
        if(ruishouRenderer == null) return;

        Color c = startColor;
        c.a = startAlpha;
        ruishouRenderer.color = c;
    }

    void ExtractWorldPoints()
    {
        Vector2[] localPoints = outlineCollider.points;
        _worldPathPoints = new Vector3[localPoints.Length];

        for (int i = 0; i < localPoints.Length; i++)
        {
            // 将 Collider 的本地坐标转为世界坐标，确保缩放和位移后依然准确
            _worldPathPoints[i] = transform.TransformPoint(localPoints[i]);
            // 统一 Z 轴深度，确保与光点判定在同一平面
            _worldPathPoints[i].z = needlePoint.position.z; 
        }
    }

    private void ReorderPathPointsByAnchor()
    {
        if (startAnchor == null || _worldPathPoints == null) return;

        int closestIndex = 0;
        float minDistance = float.MaxValue;

        // 1. 找到离“头部锚点”最近的轮廓点
        for (int i = 0; i < _worldPathPoints.Length; i++)
        {
            float dist = Vector3.Distance(
                startAnchor.position,
                _worldPathPoints[i]
            );

            if (dist < minDistance)
            {
                minDistance = dist;
                closestIndex = i;
            }
        }

        // 2. 以该点为起点，重排点序列
        Vector3[] reordered = new Vector3[_worldPathPoints.Length];
        int index = 0;

        for (int i = closestIndex; i < _worldPathPoints.Length; i++)
        {
            reordered[index++] = _worldPathPoints[i];
        }

        for (int i = 0; i < closestIndex; i++)
        {
            reordered[index++] = _worldPathPoints[i];
        }

        _worldPathPoints = reordered;
    }


    private void CheckTracingProgress(Vector2 screenPos)
    {
        if (_isCompleted || _worldPathPoints == null) return;

        // 获取光点当前的世界位置
        Vector3 currentNeedlePos = needlePoint.position;

        // 检查与“下一个目标点”的距离
        float distance = Vector3.Distance(currentNeedlePos, _worldPathPoints[nextPointIndex]);

        if (distance < detectThreshold)
        {
            OnPointReached();
        }
    }

    private void OnPointReached()
    {
        // 1. 反馈：震动（模拟穿针的阻力感）
        MOST_HapticFeedback.Generate(MOST_HapticFeedback.HapticTypes.Selection);
        
        // 2. 反馈：声音（你可以在这里添加“哒”的一声）
        // AudioSource.PlayClipAtPoint(daClip, transform.position);

        Debug.Log($"<color=gold>成功通过第 {nextPointIndex} 个轮廓点</color>");

        // 3. 推进索引
        nextPointIndex++;

        // 4. 检查是否完成全部
        if (nextPointIndex >= _worldPathPoints.Length)
        {
            CompleteTracing();
        }

        UpdateTargetGlow();

        UpdateRuishouVisual();
    }

    private void CompleteTracing()
    {
        _isCompleted = true;
        MOST_HapticFeedback.Generate(MOST_HapticFeedback.HapticTypes.Success);
        
        Debug.Log("<color=green>瑞兽激活！勾勒完成。</color>");
        
        // 这里执行激活瑞兽的逻辑（如 Live2D 启动、透明度变亮等）
    }

    // 辅助功能：在 Scene 视图画出路径，方便调试顺序
    private void OnDrawGizmos()
    {
        if (_worldPathPoints == null || _worldPathPoints.Length == 0) return;

        for (int i = 0; i < _worldPathPoints.Length; i++)
        {
            // 已经通过的变绿，当前的变红，未到达的变灰
            if (i < nextPointIndex) Gizmos.color = Color.green;
            else if (i == nextPointIndex) Gizmos.color = Color.red;
            else Gizmos.color = Color.gray;

            Gizmos.DrawSphere(_worldPathPoints[i], 0.1f);

            // 画出连线显示顺序
            if (i < _worldPathPoints.Length - 1)
            {
                Gizmos.DrawLine(_worldPathPoints[i], _worldPathPoints[i + 1]);
            }
        }
    }

    private void UpdateTargetGlow()
    {
        if (targetGlowObj == null || _worldPathPoints == null) return;

        if (nextPointIndex < _worldPathPoints.Length)
        {
            targetGlowObj.gameObject.SetActive(true);
            // 将红点移动到下一个目标点的位置
            // 注意：Z轴偏移 -0.1 确保它显示在最前面
            Vector3 pos = _worldPathPoints[nextPointIndex];
            Debug.Log($"下一个轮廓点的坐标为{pos}");
            targetGlowObj.position = new Vector3(pos.x, pos.y, pos.z - 0.1f);
        }
        else
        {
            // 全部完成后隐藏
            targetGlowObj.gameObject.SetActive(false);
        }
    }

    private void UpdateRuishouVisual()
    {
        if(ruishouRenderer == null || _worldPathPoints == null) return;

        float progress = Mathf.Clamp01(
            nextPointIndex / (float)_worldPathPoints.Length
        );

        Color targetColor = Color.Lerp(
            startColor,
            Color.white,
            progress
        );

        float alpha = Mathf.Lerp(
            startAlpha,
            endAlpha,
            progress
        );

        targetColor.a = alpha;
        ruishouRenderer.color = targetColor;
    }
}