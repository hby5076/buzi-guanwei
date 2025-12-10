using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

public class InputManager : MonoBehaviour
{
    // 单例实例
    public static InputManager Instance { get; private set; }

    // Input Actions 引用
    private PlayerControls playerControls;

    // 使用 C# 事件
    public event Action<Vector2> OnTiltChanged;
    public event Action<Vector2, int> OnTouchBegan;
    public event Action<Vector2, int> OnTouchMoved;
    public event Action<Vector2, int> OnTouchEnded;

    [System.Serializable]
    public struct FingerData
    {
        public int fingerId;
        public Vector2 position;
        public Vector2 startPosition;
        public Vector2 deltaPosition;
        public float touchTime;
        public FingerRole role;
        public bool isLongPress;
        public bool isActive;
    }

    public enum FingerRole
    {
        None,
        Guiding,    // 引导指 - 绘制金线
        Operating,  // 操作指 - 钉固操作
        Unassigned  // 未分配
    }

    [Header("双指交互设置")]
    public float longPressDuration = 0.5f;      // 长按时间阈值
    public float longPressMoveThreshold = 20f;  // 长按允许移动的像素阈值

    // 手指追踪
    private Dictionary<int, FingerData> activeFingers = new Dictionary<int, FingerData>();
    private List<int> removedFingerIds = new List<int>(); // 用于安全移除

    // 新的事件
    public event Action<FingerData> OnFingerBegan;
    public event Action<FingerData> OnFingerMoved;
    public event Action<FingerData> OnFingerEnded;
    public event Action<FingerData> OnFingerLongPress;
    public event Action<Vector2> OnDeviceShake; // 设备摇晃检测（可选）

    // 角色追踪
    private int guidingFingerId = -1;
    private int operatingFingerId = -1;

    // 公开属性
    public Vector2 CurrentTilt { get; private set; }
    public Vector2 TouchPosition { get; private set; }
    public bool IsTouching { get; private set; }

    // 灵敏度设置
    [Header("陀螺仪设置")]
    [Range(0.5f, 5f)]
    public float tiltSensitivity = 2f;
    [Range(0.1f, 1f)]
    public float tiltSmoothing = 0.3f;

    [Header("触摸设置")]
    public float minDragThreshold = 10f;

    // 私有变量
    private Vector2 smoothedTilt;
    private Vector2 initialTilt;
    private Vector2 touchStartPos;
    private bool isDragging;
    private bool gyroEnabled;

    // --- 新增：防止事件重复触发的关键变量 ---
    private Vector2 lastPublishedTilt = Vector2.zero;
    private float tiltChangeThreshold = 0.01f;

    // --- 新增：缓存常用传感器设备，提升性能 ---
    private UnityEngine.InputSystem.AttitudeSensor cachedAttitudeSensor;
    private UnityEngine.InputSystem.Gyroscope cachedGyroscope;
    private UnityEngine.InputSystem.Accelerometer cachedAccelerometer;
    private Touchscreen cachedTouchscreen;

    void Awake()
    {
        Debug.Log("[InputManager] Awake() 开始");

        // 单例模式
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Debug.Log("[InputManager] 单例设置完成");
        }
        else
        {
            Debug.LogWarning("[InputManager] 发现重复实例，销毁当前对象");
            Destroy(gameObject);
            return;
        }

        // 初始化 Input System
        InitializeInputSystem();

        Debug.Log("[InputManager] Awake() 完成");
    }

    void InitializeInputSystem()
    {
        playerControls = new PlayerControls();

        // 提前缓存设备引用，避免每次查询
        CacheInputDevices();

        // 检查并启用传感器
        CheckAndEnableSensors();

        Debug.Log("[InputSystem] 初始化完成");
    }

    // --- 新增方法：缓存设备引用 ---
    void CacheInputDevices()
    {
        cachedAttitudeSensor = UnityEngine.InputSystem.AttitudeSensor.current;
        cachedGyroscope = UnityEngine.InputSystem.Gyroscope.current;
        cachedAccelerometer = UnityEngine.InputSystem.Accelerometer.current;
        cachedTouchscreen = Touchscreen.current;

        Debug.Log($"[InputManager] 设备缓存 - 姿态: {cachedAttitudeSensor != null}, 陀螺: {cachedGyroscope != null}, 加速: {cachedAccelerometer != null}, 触摸屏: {cachedTouchscreen != null}");
    }

    // --- 重构方法：检查并启用传感器 ---
    void CheckAndEnableSensors()
    {
        // 1. 优先尝试启用姿态传感器（最稳定）
        if (cachedAttitudeSensor != null)
        {
            InputSystem.EnableDevice(cachedAttitudeSensor);
            Debug.Log("[InputManager] 已启用姿态传感器 (AttitudeSensor)");
            gyroEnabled = true;
        }
        // 2. 备选：启用陀螺仪
        else if (cachedGyroscope != null)
        {
            InputSystem.EnableDevice(cachedGyroscope);
            Debug.Log("[InputManager] 已启用陀螺仪 (Gyroscope)");
            gyroEnabled = true;
        }
        // 3. 最后启用加速度计
        else if (cachedAccelerometer != null)
        {
            InputSystem.EnableDevice(cachedAccelerometer);
            Debug.Log("[InputManager] 已启用加速度计 (Accelerometer)");
            gyroEnabled = true; // 注意：加速度计不是严格意义上的陀螺仪，但用于倾斜检测
        }
        else
        {
            gyroEnabled = false;
            Debug.LogWarning("[InputManager] 未检测到任何倾斜传感器，将使用键盘模拟");
        }

        // 4. 确保触摸屏启用（关键！）
        if (cachedTouchscreen != null)
        {
            // 触摸屏设备通常默认启用，这里显式确保
            Debug.Log("[InputManager] 触摸屏设备就绪");
        }
        else
        {
            Debug.LogError("[InputManager] 未检测到触摸屏设备！");
        }
    }

    void OnEnable()
    {
        playerControls?.GyroDrawing.Enable();
        playerControls?.TouchInteraction.Enable();
        Debug.Log("[InputManager] Action Maps 已启用");
    }

    void OnDisable()
    {
        playerControls?.GyroDrawing.Disable();
        playerControls?.TouchInteraction.Disable();
        Debug.Log("[InputManager] Action Maps 已禁用");
    }

    void Start()
    {
        CalibrateTilt();
    }

    void Update()
    {
        // 在编辑器中使用键盘模拟
#if UNITY_EDITOR
        if (Application.isEditor)
        {
            HandleEditorInput();
            return;
        }
#endif

        // ===== 真机模式核心：必须同时处理传感器和触摸 =====
        UpdateGyroData();
        UpdateTouchInput(); // 关键修改：从 UpdateTouchState 改为 UpdateTouchInput
    }

#if UNITY_EDITOR
    void HandleEditorInput()
    {
        // 1. 键盘模拟陀螺仪（保持不变）
        Vector2 inputFromAction = playerControls.GyroDrawing.DeviceTilt.ReadValue<Vector2>();

        if (inputFromAction != Vector2.zero)
        {
            inputFromAction *= tiltSensitivity;
            smoothedTilt = Vector2.Lerp(smoothedTilt, inputFromAction, tiltSmoothing);
            CurrentTilt = smoothedTilt;
            TryInvokeTiltChanged(CurrentTilt);
        }
        else
        {
            smoothedTilt = Vector2.Lerp(smoothedTilt, Vector2.zero, tiltSmoothing);
            CurrentTilt = smoothedTilt;
            TryInvokeTiltChanged(CurrentTilt);
        }

        // 2. 鼠标模拟触摸（增强版）

        // 鼠标左键模拟第一个手指
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            HandleTouchBegan(0, mousePos); // 使用增强方法
        }

        if (Mouse.current.leftButton.isPressed)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            if (activeFingers.ContainsKey(0))
            {
                Vector2 lastPos = activeFingers[0].position;
                if (mousePos != lastPos)
                {
                    HandleTouchMoved(0, mousePos);
                }
            }
        }

        if (Mouse.current.leftButton.wasReleasedThisFrame && activeFingers.ContainsKey(0))
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            HandleTouchEnded(0, mousePos);
        }

        // 鼠标右键模拟第二个手指（用于测试双指）
        if (Mouse.current.rightButton.wasPressedThisFrame)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            HandleTouchBegan(1, mousePos);
        }

        if (Mouse.current.rightButton.isPressed && activeFingers.ContainsKey(1))
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            Vector2 lastPos = activeFingers[1].position;
            if (mousePos != lastPos)
            {
                HandleTouchMoved(1, mousePos);
            }
        }

        if (Mouse.current.rightButton.wasReleasedThisFrame && activeFingers.ContainsKey(1))
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            HandleTouchEnded(1, mousePos);
        }

        // 检查长按
        CheckLongPress();
    }
#endif

    // --- 关键新增方法：真机触摸输入处理 ---
    void UpdateTouchInput()
    {
        if (cachedTouchscreen == null)
        {
            if (Time.frameCount % 60 == 0)
                Debug.LogWarning("[真机] 触摸屏设备未找到");
            return;
        }

        // 清除已移除的手指
        foreach (var fingerId in removedFingerIds)
        {
            if (activeFingers.ContainsKey(fingerId))
            {
                activeFingers.Remove(fingerId);
            }
        }
        removedFingerIds.Clear();

        // 获取所有触摸点
        var touches = cachedTouchscreen.touches;

        // 处理每个触摸点
        for (int i = 0; i < touches.Count; i++)
        {
            var touchControl = touches[i];
            int touchId = touchControl.touchId.ReadValue();
            Vector2 touchPos = touchControl.position.ReadValue();

            // 触摸开始
            if (touchControl.press.wasPressedThisFrame)
            {
                HandleTouchBegan(touchId, touchPos);
            }
            // 触摸移动
            else if (touchControl.press.isPressed)
            {
                HandleTouchMoved(touchId, touchPos);
            }
            // 触摸结束
            else if (touchControl.press.wasReleasedThisFrame)
            {
                HandleTouchEnded(touchId, touchPos);
            }
        }

        // 检查长按
        CheckLongPress();
    }

    // 真机传感器数据更新（优化版）
    void UpdateGyroData()
    {
        Vector2 rawTilt = Vector2.zero;
        string sensorUsed = "None";

        // 方案1：使用姿态传感器（首选）
        if (cachedAttitudeSensor != null && cachedAttitudeSensor.enabled)
        {
            try
            {
                Quaternion deviceRotation = cachedAttitudeSensor.attitude.ReadValue();
                Vector3 eulerAngles = deviceRotation.eulerAngles;

                // 更稳定的映射公式，考虑设备初始朝向
                float normX = Mathf.Sin(eulerAngles.z * Mathf.Deg2Rad);
                float normY = Mathf.Sin(eulerAngles.x * Mathf.Deg2Rad);

                rawTilt = new Vector2(normX, normY);
                sensorUsed = "AttitudeSensor";
            }
            catch (Exception e)
            {
                Debug.LogError($"[传感器] 读取姿态传感器失败: {e.Message}");
            }
        }
        // 方案2：使用加速度计（备用）
        else if (cachedAccelerometer != null && cachedAccelerometer.enabled)
        {
            try
            {
                Vector3 acceleration = cachedAccelerometer.acceleration.ReadValue();
                // 静止时，acceleration近似为重力向量
                rawTilt = new Vector2(acceleration.x, acceleration.y);
                sensorUsed = "Accelerometer";
            }
            catch (Exception e)
            {
                Debug.LogError($"[传感器] 读取加速度计失败: {e.Message}");
            }
        }

        // 应用校准、灵敏度和平滑
        if (sensorUsed != "None")
        {
            rawTilt -= initialTilt;
            rawTilt *= tiltSensitivity;
            smoothedTilt = Vector2.Lerp(smoothedTilt, rawTilt, tiltSmoothing);
            CurrentTilt = smoothedTilt;

            // 每60帧输出一次传感器信息（调试用）
            if (Time.frameCount % 60 == 0)
            {
                Debug.Log($"[传感器] 使用: {sensorUsed}, 原始: {rawTilt}, 平滑: {CurrentTilt}");
            }
        }
        else
        {
            // 没有可用传感器时，平滑归零
            smoothedTilt = Vector2.Lerp(smoothedTilt, Vector2.zero, tiltSmoothing);
            CurrentTilt = smoothedTilt;
        }

        // 触发事件
        TryInvokeTiltChanged(CurrentTilt);
    }

    private void TryInvokeTiltChanged(Vector2 newTilt)
    {
        if (Vector2.Distance(newTilt, lastPublishedTilt) > tiltChangeThreshold)
        {
            lastPublishedTilt = newTilt;
            // 控制日志频率，避免刷屏
            if (Time.frameCount % 30 == 0) Debug.Log($"[TiltChanged] 事件触发: {newTilt}");
            OnTiltChanged?.Invoke(newTilt);
        }
    }

    public void CalibrateTilt()
    {
        Vector2 currentTilt = Vector2.zero;

        if (cachedAttitudeSensor != null && cachedAttitudeSensor.enabled)
        {
            try
            {
                Quaternion deviceRotation = cachedAttitudeSensor.attitude.ReadValue();
                Vector3 eulerAngles = deviceRotation.eulerAngles;
                float normX = Mathf.Sin(eulerAngles.z * Mathf.Deg2Rad);
                float normY = Mathf.Sin(eulerAngles.x * Mathf.Deg2Rad);
                currentTilt = new Vector2(normX, normY);
            }
            catch (Exception e)
            {
                Debug.LogError($"[校准] 读取传感器失败: {e.Message}");
            }
        }

        initialTilt = currentTilt;
        Debug.Log($"[校准] 完成。初始基准值: {initialTilt}");
    }

    void UpdateTouchState()
    {
        if (IsTouching)
        {
            if (!isDragging && Vector2.Distance(touchStartPos, TouchPosition) > minDragThreshold)
            {
                isDragging = true;
                Debug.Log($"[触摸] 进入拖拽状态，起点: {touchStartPos}, 当前: {TouchPosition}");
            }
        }
    }

    // 公开方法保持不变
    public Vector2 GetScreenToWorldPoint(Vector2 screenPos)
    {
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            return mainCam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, mainCam.nearClipPlane + 1));
        }
        return Vector2.zero;
    }

    public void SetTiltSensitivity(float sensitivity)
    {
        Debug.Log($"[灵敏度] 设置: {sensitivity}");
        tiltSensitivity = Mathf.Clamp(sensitivity, 0.5f, 5f);
    }

    public bool IsGyroAvailable()
    {
        return gyroEnabled;
    }

    public string GetInputStatus()
    {
        string status = $"陀螺仪: {(gyroEnabled ? "已启用" : "模拟模式")}\n";
        status += $"激活手指: {activeFingers.Count}\n";

        foreach (var kvp in activeFingers)
        {
            var finger = kvp.Value;
            status += $"  手指{finger.fingerId}: {finger.role} 位置: {finger.position}\n";
        }

        return status;
    }

    public void TriggerTestEvents()
    {
        OnTiltChanged?.Invoke(new Vector2(0.5f, 0.5f));
        OnTouchBegan?.Invoke(new Vector2(100, 200), 0);
    }

    void HandleTouchBegan(int fingerId, Vector2 position)
    {
        FingerData fingerData = new FingerData
        {
            fingerId = fingerId,
            position = position,
            startPosition = position,
            deltaPosition = Vector2.zero,
            touchTime = Time.time,
            role = FingerRole.Unassigned,
            isLongPress = false,
            isActive = true
        };

        activeFingers[fingerId] = fingerData;

        Debug.Log($"[触摸开始] 手指{fingerId}, 位置: {position}");
        OnFingerBegan?.Invoke(fingerData);

        // 同时触发旧事件以保持兼容性
        OnTouchBegan?.Invoke(position, fingerId);
    }

    void HandleTouchMoved(int fingerId, Vector2 position)
    {
        if (!activeFingers.ContainsKey(fingerId)) return;

        FingerData fingerData = activeFingers[fingerId];
        fingerData.deltaPosition = position - fingerData.position;
        fingerData.position = position;
        activeFingers[fingerId] = fingerData;

        // 检查是否移动超过长按阈值
        float moveDistance = Vector2.Distance(position, fingerData.startPosition);
        if (moveDistance > longPressMoveThreshold)
        {
            // 移动太大，取消长按资格
            fingerData.isLongPress = false;
            activeFingers[fingerId] = fingerData;
        }

        OnFingerMoved?.Invoke(fingerData);
        OnTouchMoved?.Invoke(position, fingerId);
    }

    void HandleTouchEnded(int fingerId, Vector2 position)
    {
        if (!activeFingers.ContainsKey(fingerId)) return;

        FingerData fingerData = activeFingers[fingerId];
        fingerData.position = position;
        fingerData.isActive = false;

        // 移除前触发事件
        OnFingerEnded?.Invoke(fingerData);
        OnTouchEnded?.Invoke(position, fingerId);

        Debug.Log($"[触摸结束] 手指{fingerId}, 持续时间: {Time.time - fingerData.touchTime:F2}秒");

        // 清理角色分配
        if (guidingFingerId == fingerId) guidingFingerId = -1;
        if (operatingFingerId == fingerId) operatingFingerId = -1;

        // 标记为待移除
        removedFingerIds.Add(fingerId);
    }

    void CheckLongPress()
    {
        foreach (var kvp in activeFingers)
        {
            int fingerId = kvp.Key;
            FingerData fingerData = kvp.Value;

            if (!fingerData.isLongPress &&
                Time.time - fingerData.touchTime >= longPressDuration)
            {
                // 检查移动距离
                float moveDistance = Vector2.Distance(
                    fingerData.position,
                    fingerData.startPosition
                );

                if (moveDistance <= longPressMoveThreshold)
                {
                    fingerData.isLongPress = true;
                    activeFingers[fingerId] = fingerData;

                    Debug.Log($"[长按检测] 手指{fingerId}, 位置: {fingerData.position}");
                    OnFingerLongPress?.Invoke(fingerData);
                }
            }
        }
    }

    // 角色管理
    public void AssignFingerRole(int fingerId, FingerRole role)
    {
        if (!activeFingers.ContainsKey(fingerId)) return;

        FingerData fingerData = activeFingers[fingerId];
        fingerData.role = role;
        activeFingers[fingerId] = fingerData;

        // 更新角色追踪
        switch (role)
        {
            case FingerRole.Guiding:
                guidingFingerId = fingerId;
                break;
            case FingerRole.Operating:
                operatingFingerId = fingerId;
                break;
        }

        Debug.Log($"[角色分配] 手指{fingerId} -> {role}");
    }

    public FingerRole GetFingerRole(int fingerId)
    {
        return activeFingers.ContainsKey(fingerId)
            ? activeFingers[fingerId].role
            : FingerRole.None;
    }

    public int GetGuidingFingerId() => guidingFingerId;
    public int GetOperatingFingerId() => operatingFingerId;

    public bool TryGetFingerData(int fingerId, out FingerData data)
    {
        return activeFingers.TryGetValue(fingerId, out data);
    }

    public List<FingerData> GetAllActiveFingers()
    {
        return activeFingers.Values.Where(f => f.isActive).ToList();
    }

    // 新增实用方法
    public bool IsFingerLongPressing(int fingerId)
    {
        return activeFingers.ContainsKey(fingerId) &&
               activeFingers[fingerId].isLongPress;
    }

    public float GetFingerPressDuration(int fingerId)
    {
        if (!activeFingers.ContainsKey(fingerId)) return 0f;
        return Time.time - activeFingers[fingerId].touchTime;
    }

    public Vector2 GetFingerStartPosition(int fingerId)
    {
        return activeFingers.ContainsKey(fingerId)
            ? activeFingers[fingerId].startPosition
            : Vector2.zero;
    }

    // 检查是否在特定区域
    public bool IsFingerInRect(int fingerId, Rect rect)
    {
        if (!activeFingers.ContainsKey(fingerId)) return false;
        return rect.Contains(activeFingers[fingerId].position);
    }

    // 获取所有手指的平均位置（用于特殊效果）
    public Vector2 GetAverageFingerPosition()
    {
        if (activeFingers.Count == 0) return Vector2.zero;

        Vector2 sum = Vector2.zero;
        int count = 0;

        foreach (var finger in activeFingers.Values)
        {
            if (finger.isActive)
            {
                sum += finger.position;
                count++;
            }
        }

        return count > 0 ? sum / count : Vector2.zero;
    }
}