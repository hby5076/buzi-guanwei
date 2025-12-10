using UnityEngine;
using UnityEngine.UI;
using TMPro;

public class InputTester : MonoBehaviour
{
    [Header("UI显示组件")]
    public TextMeshProUGUI statusText; // 用于显示整体状态
    public TextMeshProUGUI touch1Text; // 显示主触摸
    public TextMeshProUGUI touch2Text; // 显示次触摸/点击
    public TextMeshProUGUI gyroText;   // 显示陀螺仪数据
    public TextMeshProUGUI pinchText;  // 显示捏合数据
    public TextMeshProUGUI eventLogText; // 显示事件触发日志

    [Header("触摸点视觉表示")]
    public RectTransform touch1Indicator; // 主触摸点UI指示器
    public RectTransform touch2Indicator; // 次触摸点UI指示器

    // 用于记录事件
    private string logContent = "";
    private const int MAX_LOG_LINES = 5;

    void Start()
    {
        // 确保有UI组件
        if (statusText == null) Debug.LogWarning("请在Inspector中为InputTester绑定UI Text组件。");

        // 订阅InputManager的所有事件
        if (InputManager_Scene1.Instance != null)
        {
            InputManager_Scene1.Instance.OnPrimaryTouchMoved += UpdatePrimaryTouch;
            InputManager_Scene1.Instance.OnSecondaryTapPerformed += UpdateSecondaryTap;
            InputManager_Scene1.Instance.OnDeviceTiltChanged += UpdateGyroData;
            InputManager_Scene1.Instance.OnPinchDeltaChanged += UpdatePinchData;

            LogEvent("InputTester: 已成功订阅所有输入事件。");
        }
        else
        {
            LogEvent("错误: InputManager 实例未找到！");
        }
    }

    void Update()
    {
        // 更新整体状态信息（每帧）
        UpdateStatusDisplay();

        // 如果没有触发移动事件，则尝试从InputManager直接读取次触摸位置（例如长按时）
        UpdateSecondaryTouchPosition();
    }

    void UpdateStatusDisplay()
    {
        if (statusText != null)
        {
            string envInfo = InputManager_Scene1.Instance?.GetInputEnvironmentInfo() ?? "InputManager为空";
            statusText.text = $"输入系统测试器\n" +
                              $"================\n" +
                              $"{envInfo}\n" +
                              $"当前输入映射: {GetCurrentActionMapName()}\n" +
                              $"帧率: {Mathf.Round(1.0f / Time.deltaTime)} FPS";
        }
    }

    void UpdatePrimaryTouch(Vector2 screenPos)
    {
        // 更新主触摸文本
        if (touch1Text != null)
        {
            Vector2 localPos;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                touch1Text.rectTransform.parent as RectTransform,
                screenPos, null, out localPos
            );
            touch1Text.text = $"主触摸 (引导指):\n" +
                              $"屏幕坐标: {screenPos:F0}\n" +
                              $"本地坐标: {localPos:F0}";
        }

        // 更新主触摸点视觉指示器
        if (touch1Indicator != null)
        {
            touch1Indicator.anchoredPosition = screenPos;
            touch1Indicator.gameObject.SetActive(true);
        }

        LogEvent($"主触摸移动: {screenPos}");
    }

    void UpdateSecondaryTap(Vector2 screenPos)
    {
        // 更新次触摸文本
        if (touch2Text != null)
        {
            touch2Text.text = $"次触摸点击 (钉固):\n" +
                              $"屏幕坐标: {screenPos:F0}\n" +
                              $"操作: 钉固触发";
        }

        // 更新次触摸点视觉指示器（短暂高亮）
        if (touch2Indicator != null)
        {
            touch2Indicator.anchoredPosition = screenPos;
            touch2Indicator.gameObject.SetActive(true);
            // 可以在这里触发一个缩放或颜色变化的动画来表示点击
        }

        LogEvent($"次触摸点击 (钉固) 于: {screenPos}");
    }

    void UpdateSecondaryTouchPosition()
    {
        // 这个函数持续更新次触摸位置（即使没有点击事件），对于按住Alt模拟第二指很有用
        if (InputManager.Instance != null && touch2Text != null)
        {
            Vector2 secondaryPos = InputManager_Scene1.Instance.GetSecondaryTouchPosition();
            if (secondaryPos != Vector2.zero)
            {
                touch2Text.text = $"次触摸 (操作指):\n" +
                                  $"屏幕坐标: {secondaryPos:F0}\n" +
                                  $"状态: 正在模拟/触摸";
                if (touch2Indicator != null)
                {
                    touch2Indicator.anchoredPosition = secondaryPos;
                    touch2Indicator.gameObject.SetActive(true);
                }
            }
            else if (!touch2Indicator.gameObject.activeSelf)
            {
                // 如果没有次触摸数据，且指示器当前不是因点击而显示，则清除文本的部分信息
                touch2Text.text = $"次触摸 (操作指):\n" +
                                  $"状态: 未激活";
            }
        }
    }

    void UpdateGyroData(Vector3 angularVelocity)
    {
        // 更新陀螺仪文本
        if (gyroText != null)
        {
            gyroText.text = $"陀螺仪 (角速度):\n" +
                            $"X: {angularVelocity.x:F3}\n" +
                            $"Y: {angularVelocity.y:F3}\n" +
                            $"Z: {angularVelocity.z:F3}\n" +
                            $"幅度: {angularVelocity.magnitude:F3}";
        }

        // 为了清晰，我们只记录幅度较大的倾斜
        if (angularVelocity.magnitude > 0.1f)
        {
            LogEvent($"设备倾斜: {angularVelocity}");
        }
    }

    void UpdatePinchData(float delta)
    {
        // 更新捏合文本
        if (pinchText != null)
        {
            string direction = delta > 0 ? "放大 (E键)" : "缩小 (Q键)";
            pinchText.text = $"双指捏合模拟:\n" +
                             $"Delta: {delta:F3}\n" +
                             $"方向: {direction}\n" +
                             $"状态: {(Mathf.Abs(delta) > 0.01f ? "进行中" : "未激活")}";
        }

        if (Mathf.Abs(delta) > 0.01f)
        {
            LogEvent($"捏合输入: {delta:F3}");
        }
    }

    void LogEvent(string message)
    {
        logContent = $"[{Time.time:F2}] {message}\n" + logContent;

        // 限制日志行数
        string[] lines = logContent.Split('\n');
        if (lines.Length > MAX_LOG_LINES)
        {
            logContent = string.Join("\n", lines, 0, MAX_LOG_LINES);
        }

        if (eventLogText != null)
        {
            eventLogText.text = "最近事件:\n" + logContent;
        }
        else
        {
            Debug.Log(message); // 同时输出到Unity控制台
        }
    }

    string GetCurrentActionMapName()
    {
        if (InputManager.Instance == null) return "未知";
        // 注意: 这里需要根据你的状态机逻辑来判断，或者为InputManager添加一个公开属性
        // 暂时返回一个占位符
        return "根据StateManager判断";
    }

    void OnDestroy()
    {
        // 取消订阅，防止内存泄漏
        if (InputManager.Instance != null)
        {
            InputManager_Scene1.Instance.OnPrimaryTouchMoved -= UpdatePrimaryTouch;
            InputManager_Scene1.Instance.OnSecondaryTapPerformed -= UpdateSecondaryTap;
            InputManager_Scene1.Instance.OnDeviceTiltChanged -= UpdateGyroData;
            InputManager_Scene1.Instance.OnPinchDeltaChanged -= UpdatePinchData;
        }
    }
}