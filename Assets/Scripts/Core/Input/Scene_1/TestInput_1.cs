using UnityEngine;
using TMPro;
using System.Collections;

public class TestInput : MonoBehaviour
{
    [Header("UI 显示 - 请在Inspector中拖拽赋值")]
    public TextMeshProUGUI tiltText;
    public TextMeshProUGUI touchText;
    public TextMeshProUGUI statusText;

    [Header("测试物体 - 请在Inspector中拖拽赋值")]
    public GameObject brushCursor; // 用于平金涂抹测试的笔刷
    public float brushSpeed = 5f;  // 笔刷移动速度

    private bool isInitialized = false;

    void Start()
    {
        Debug.Log("TestInput 开始初始化...");
        StartCoroutine(InitializeWithDelay());
    }

    IEnumerator InitializeWithDelay()
    {
        // 等待一帧，确保InputManager等单例已创建
        yield return null;

        if (InputManager.Instance == null)
        {
            Debug.LogError("初始化失败：未找到 InputManager 实例。");
            yield break;
        }

        // 订阅输入事件（使用C#事件语法）
        InputManager.Instance.OnTiltChanged += OnTiltChanged;
        InputManager.Instance.OnTouchBegan += OnTouchBegan;
        InputManager.Instance.OnTouchMoved += OnTouchMoved;
        InputManager.Instance.OnTouchEnded += OnTouchEnded;

        isInitialized = true;
        Debug.Log("TestInput 初始化完成，事件订阅成功。");
    }

    void Update()
    {
        // 如果未初始化，不执行任何更新
        if (!isInitialized) return;

        // === 1. 更新状态文本 ===
        if (statusText != null)
        {
            // 调用InputManager的方法获取当前状态字符串
            statusText.text = "状态：" + InputManager.Instance.GetInputStatus();
        }

        // === 2. 根据陀螺仪倾斜移动笔刷 ===
        if (brushCursor != null)
        {
            // 获取当前倾斜数据
            Vector2 tilt = InputManager.Instance.CurrentTilt;

            // 根据倾斜方向和速度计算新位置
            Vector3 moveDelta = new Vector3(tilt.x, tilt.y, 0) * brushSpeed * Time.deltaTime;
            Vector3 newPosition = brushCursor.transform.position + moveDelta;

            // 将新位置限制在屏幕范围内（世界坐标）
            Camera mainCam = Camera.main;
            if (mainCam != null)
            {
                // 将世界坐标转换为屏幕坐标以便进行边界检查
                Vector3 screenPos = mainCam.WorldToScreenPoint(newPosition);
                // 限制在屏幕像素范围内
                screenPos.x = Mathf.Clamp(screenPos.x, 0, Screen.width);
                screenPos.y = Mathf.Clamp(screenPos.y, 0, Screen.height);
                // 将限制后的屏幕坐标转回世界坐标
                newPosition = mainCam.ScreenToWorldPoint(screenPos);
            }

            // 应用新位置
            brushCursor.transform.position = newPosition;
        }
    }

    // ===== 输入事件回调函数 =====
    void OnTiltChanged(Vector2 tilt)
    {
        if (tiltText != null)
        {
            tiltText.text = $"陀螺仪: X={tilt.x:F2}, Y={tilt.y:F2}";
        }
    }

    void OnTouchBegan(Vector2 position, int fingerId)
    {
        if (touchText != null)
        {
            touchText.text = $"触摸开始: {position.x:F0}, {position.y:F0}";
        }
    }

    void OnTouchMoved(Vector2 position, int fingerId)
    {
        if (touchText != null)
        {
            touchText.text = $"触摸移动: {position.x:F0}, {position.y:F0}";
        }
    }

    void OnTouchEnded(Vector2 position, int fingerId)
    {
        if (touchText != null)
        {
            touchText.text = $"触摸结束: {position.x:F0}, {position.y:F0}";
        }
    }

    // ===== UI 控件回调函数 (必须为 public) =====
    public void CalibrateGyro()
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.CalibrateTilt();
            Debug.Log("UI按钮：已请求校准陀螺仪。");
        }
    }

    public void ChangeSensitivity(float value)
    {
        if (InputManager.Instance != null)
        {
            InputManager.Instance.SetTiltSensitivity(value);
            Debug.Log($"UI滑块：灵敏度已改为 {value:F2}");
        }
    }

    void OnDestroy()
    {
        // 退出时取消订阅事件，防止内存泄漏
        if (InputManager.Instance != null)
        {
            InputManager.Instance.OnTiltChanged -= OnTiltChanged;
            InputManager.Instance.OnTouchBegan -= OnTouchBegan;
            InputManager.Instance.OnTouchMoved -= OnTouchMoved;
            InputManager.Instance.OnTouchEnded -= OnTouchEnded;
        }
    }
}