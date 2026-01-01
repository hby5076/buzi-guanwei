using UnityEngine;

public class NeedleFollower : MonoBehaviour
{
    // 如果摄像机在 Z=-10，瑞兽在 Z=0，这里填 10
    public float zDepth = 10f; 
    private bool _isPressed = false;

    void Start()
    {
        if (InputManager_Scene1.Instance != null)
        {
            // 必须手动调用一次切换模式，否则 ActionMap 是关闭的
            InputManager_Scene1.Instance.SetMode("Panjin"); 

            InputManager_Scene1.Instance.OnNeedlePositionMoved += MoveNeedle;
            InputManager_Scene1.Instance.OnTraceContactStarted += () => _isPressed = true;
            InputManager_Scene1.Instance.OnTraceContactCanceled += () => _isPressed = false;
        }
    }

    void MoveNeedle(Vector2 screenPos)
    {
        // 只有按住时才移动（或者根据需求移除此判断）
        // if (!_isPressed) return;

        // 关键：将屏幕像素坐标转为世界坐标
        Vector3 worldPos = Camera.main.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, zDepth));
        
        // 锁定 Z 轴在背景前方
        transform.position = new Vector3(worldPos.x, worldPos.y, 989f);
    }
}