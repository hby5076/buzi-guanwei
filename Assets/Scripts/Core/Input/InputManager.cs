using System;
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
        // 1. 键盘模拟陀螺仪
        Vector2 inputFromAction = playerControls.GyroDrawing.DeviceTilt.ReadValue<Vector2>();

        if (inputFromAction != Vector2.zero)
        {
            inputFromAction *= tiltSensitivity;
            smoothedTilt = Vector2.Lerp(smoothedTilt, inputFromAction, tiltSmoothing);
            CurrentTilt = smoothedTilt;
            TryInvokeTiltChanged(CurrentTilt);
            Debug.Log($"[编辑器] 键盘模拟输入: {inputFromAction}, 处理后: {CurrentTilt}");
        }
        else
        {
            smoothedTilt = Vector2.Lerp(smoothedTilt, Vector2.zero, tiltSmoothing);
            CurrentTilt = smoothedTilt;
            TryInvokeTiltChanged(CurrentTilt);
        }

        // 2. 鼠标模拟触摸（保持不变）
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            TouchPosition = mousePos;
            IsTouching = true;
            touchStartPos = TouchPosition;
            isDragging = false;

            Debug.Log($"[编辑器] 鼠标按下，位置: {mousePos}");
            OnTouchBegan?.Invoke(TouchPosition, 0);
        }

        if (Mouse.current.leftButton.isPressed && IsTouching)
        {
            Vector2 mousePos = Mouse.current.position.ReadValue();
            if (mousePos != TouchPosition)
            {
                TouchPosition = mousePos;
                OnTouchMoved?.Invoke(TouchPosition, 0);
            }
        }

        if (Mouse.current.leftButton.wasReleasedThisFrame && IsTouching)
        {
            IsTouching = false;
            isDragging = false;
            Debug.Log($"[编辑器] 鼠标释放，位置: {TouchPosition}");
            OnTouchEnded?.Invoke(TouchPosition, 0);
        }

        UpdateTouchState();
    }
#endif

    // --- 关键新增方法：真机触摸输入处理 ---
    void UpdateTouchInput()
    {
        if (cachedTouchscreen == null)
        {
            // 每60帧警告一次，避免刷屏
            if (Time.frameCount % 60 == 0) Debug.LogWarning("[真机] 触摸屏设备未找到，触摸输入不可用");
            return;
        }

        // 读取当前所有触摸点
        var touches = cachedTouchscreen.touches;
        if (touches.Count > 0)
        {
            // 处理第一个触摸点（单点触控）
            var primaryTouch = touches[0];
            Vector2 touchPos = primaryTouch.position.ReadValue();

            // 触摸开始
            if (primaryTouch.press.wasPressedThisFrame)
            {
                TouchPosition = touchPos;
                IsTouching = true;
                touchStartPos = touchPos;
                isDragging = false;

                Debug.Log($"[真机] 触摸开始，位置: {touchPos}, 触摸点ID: {primaryTouch.touchId.ReadValue()}");
                OnTouchBegan?.Invoke(touchPos, primaryTouch.touchId.ReadValue());
            }
            // 触摸移动
            else if (primaryTouch.press.isPressed && IsTouching)
            {
                if (touchPos != TouchPosition)
                {
                    TouchPosition = touchPos;
                    OnTouchMoved?.Invoke(touchPos, primaryTouch.touchId.ReadValue());
                    // 可选：减少移动日志频率，避免刷屏
                    // if (Time.frameCount % 10 == 0) Debug.Log($"[真机] 触摸移动，位置: {touchPos}");
                }
            }
            // 触摸结束
            else if (primaryTouch.press.wasReleasedThisFrame && IsTouching)
            {
                IsTouching = false;
                isDragging = false;
                Debug.Log($"[真机] 触摸结束，位置: {touchPos}");
                OnTouchEnded?.Invoke(touchPos, primaryTouch.touchId.ReadValue());
            }

            UpdateTouchState(); // 更新拖拽状态
        }
        else if (IsTouching)
        {
            // 异常情况：IsTouching为true但没有触摸点，强制结束
            IsTouching = false;
            isDragging = false;
            Debug.LogWarning("[真机] 触摸状态异常，已强制重置");
        }
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
        return $"陀螺仪: {(gyroEnabled ? "已启用" : "模拟模式")}, 触摸: {(IsTouching ? "进行中" : "空闲")}, 设备: {cachedTouchscreen?.name ?? "无"}";
    }

    public void TriggerTestEvents()
    {
        OnTiltChanged?.Invoke(new Vector2(0.5f, 0.5f));
        OnTouchBegan?.Invoke(new Vector2(100, 200), 0);
    }
}