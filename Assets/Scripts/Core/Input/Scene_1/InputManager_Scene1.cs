using UnityEngine;
using UnityEngine.InputSystem;
using System;

/// <summary>
/// 场景1完整输入管理器 - 纯净真机版
/// 移除了所有模拟逻辑，采用代码层 Pinch 计算，修复了陀螺仪及跨 Map 访问问题。
/// </summary>
public class InputManager_Scene1 : MonoBehaviour, 
    InputActions_Scene1.IDuilingActions,
    InputActions_Scene1.IPingjinActions,
    InputActions_Scene1.IPanjinActions
{
    public static InputManager_Scene1 Instance { get; private set; }

    [Header("设置")]
    [SerializeField] private float _pinchThreshold = 0.5f; // 捏合触发阈值

    // 资产引用
    public InputActions_Scene1 inputActions;

    // =========================================================
    // 公共事件 (Events)
    // =========================================================

    // Duiling 模式
    public event Action<Vector2> OnDragPerformed;
    public event Action<float> OnPinchDeltaChanged;  // 发布距离变化量
    public event Action OnDuilingTapPerformed;

    // Pingjin 模式
    public event Action OnPrimaryTouchContactStarted;
    public event Action OnPrimaryTouchContactCanceled;
    public event Action OnSecondaryTapPerformed;
    public event Action<Vector3> OnDeviceTilt;

    // Panjin 模式
    public event Action<Vector2> OnNeedlePositionMoved;
    public event Action OnTraceContactStarted;
    public event Action OnTraceContactCanceled;

    // =========================================================
    // 内部状态变量
    // =========================================================
    private float _lastPinchDistance = 0f;
    private bool _isPinching = false;
    private int _lastTouchCount = 0;
    [Header("调试")]
    [SerializeField] private bool _debugGyro = false; // 开启后会在控制台输出陀螺仪源与数据（限速）
    private float _lastGyroLogTime = 0f;

    // =========================================================
    // 生命周期 (Lifecycle)
    // =========================================================

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        inputActions = new InputActions_Scene1();

        // 启用陀螺仪设备（尝试同时启用新旧接口以兼容不同平台）
        if (UnityEngine.InputSystem.Gyroscope.current != null)
        {
            InputSystem.EnableDevice(UnityEngine.InputSystem.Gyroscope.current);
            Debug.Log("Input System 陀螺仪已启用");
        }
        if (SystemInfo.supportsGyroscope)
        {
            Input.gyro.enabled = true;
            Debug.Log("Legacy Gyroscope 已启用 (Input.gyro)");
        }

        // 绑定回调接口
        inputActions.Duiling.SetCallbacks(this);
        inputActions.Pingjin.SetCallbacks(this);
        inputActions.Panjin.SetCallbacks(this);

        // 显式启用真机传感器
        EnableSensors();
    }

    private void EnableSensors()
    {
        if (AttitudeSensor.current != null) InputSystem.EnableDevice(AttitudeSensor.current);
        if (UnityEngine.InputSystem.Gyroscope.current != null) InputSystem.EnableDevice(UnityEngine.InputSystem.Gyroscope.current);
        if (Accelerometer.current != null) InputSystem.EnableDevice(Accelerometer.current);
    }

    private void OnEnable() => inputActions?.Enable();
    private void OnDisable() => inputActions?.Disable();
    private void OnDestroy() => inputActions?.Dispose();

    // =========================================================
    // 核心逻辑：Update 轮询 (处理 Pinch 及传感器)
    // =========================================================

    private void Update()
    {
        HandleManualPinch();
        HandleDeviceTilt();
    }

    /// <summary>
    /// 读取设备陀螺仪 / 姿态数据：优先使用 Input System 的 Gyroscope，其次回退到 legacy Input.gyro。
    /// 在 Pingjin 模式启用时会触发 OnDeviceTilt 事件（向上游分发 Vector3 角速度）。
    /// 注意：Unity Remote 不会转发传感器数据，需打包到真机测试。
    /// </summary>
    private void HandleDeviceTilt()
    {
        // 如果既没有在 Pingjin 模式也没有开启调试，则跳过
        if (!inputActions.Pingjin.enabled && !_debugGyro) return;

        // 1) 尝试使用 Input System 的 Gyroscope (角速度)
        var gyro = UnityEngine.InputSystem.Gyroscope.current;
        if (gyro != null)
        {
            Vector3 ang = gyro.angularVelocity.ReadValue();
            OnDeviceTilt?.Invoke(ang);
            if (_debugGyro && Time.time - _lastGyroLogTime > 0.2f)
            {
                Debug.Log($"Gyro(InputSystem) angularVelocity: {ang}");
                _lastGyroLogTime = Time.time;
            }
            return;
        }

        // 2) 如果有 AttitudeSensor（提供朝向），也输出朝向（欧拉角）作为参考
        if (UnityEngine.InputSystem.AttitudeSensor.current != null)
        {
            Quaternion q = UnityEngine.InputSystem.AttitudeSensor.current.attitude.ReadValue();
            Vector3 euler = q.eulerAngles;
            if (_debugGyro && Time.time - _lastGyroLogTime > 0.2f)
            {
                Debug.Log($"AttitudeSensor attitude (euler): {euler}");
                _lastGyroLogTime = Time.time;
            }
            // 注意：Attitude 是朝向（orientation），不是角速度。这里仍然把朝向以 Vector3 形式分发，供上层参考。
            OnDeviceTilt?.Invoke(euler);
            return;
        }

        // 3) 回退到 legacy Input.gyro（角速度）
        if (SystemInfo.supportsGyroscope && Input.gyro != null && Input.gyro.enabled)
        {
            Vector3 rot = Input.gyro.rotationRateUnbiased; // 角速度 (rad/s)
            if (_debugGyro && Time.time - _lastGyroLogTime > 0.2f)
            {
                Debug.Log($"Legacy Input.gyro.rotationRateUnbiased: {rot}");
                _lastGyroLogTime = Time.time;
            }
            OnDeviceTilt?.Invoke(rot);
            return;
        }

        // 4) 最后尝试使用加速度计作为粗略倾斜指示（不是角速度）
        if (UnityEngine.InputSystem.Accelerometer.current != null)
        {
            Vector3 acc = UnityEngine.InputSystem.Accelerometer.current.acceleration.ReadValue();
            if (_debugGyro && Time.time - _lastGyroLogTime > 0.2f)
            {
                Debug.Log($"Accelerometer (InputSystem) acceleration: {acc}");
                _lastGyroLogTime = Time.time;
            }
            OnDeviceTilt?.Invoke(acc);
            return;
        }

        // 如果都没有数据且开启了调试，提示可能原因
        if (_debugGyro && Time.time - _lastGyroLogTime > 0.2f)
        {
            Debug.Log("No sensor data available. 如果使用 Unity Remote，请注意它不会转发陀螺仪/加速度计数据；请在真机构建中测试。");
            _lastGyroLogTime = Time.time;
        }
    }

    /// <summary>
    /// 在代码层手动计算捏合逻辑
    /// </summary>
    private void HandleManualPinch()
    {
        // 仅在 Duiling 模式且有触摸屏时计算
        if (!inputActions.Duiling.enabled || Touchscreen.current == null) return;

        int currentTouchCount = 0;
        // 统计当前活跃的触摸点
        for (int i = 0; i < Touchscreen.current.touches.Count; i++)
        {
            if (Touchscreen.current.touches[i].press.isPressed) currentTouchCount++;
        }

        if (currentTouchCount >= 2)
        {
            Vector2 pos1 = inputActions.Duiling.PrimaryTouch.ReadValue<Vector2>();
            Vector2 pos2 = inputActions.Duiling.SecondTouch.ReadValue<Vector2>();

            float currentDistance = Vector2.Distance(pos1, pos2);

            if (!_isPinching || _lastTouchCount < 2)
            {
                _isPinching = true;
                _lastPinchDistance = currentDistance;
            }
            else
            {
                float delta = currentDistance - _lastPinchDistance;
                if (Mathf.Abs(delta) > _pinchThreshold)
                {
                    OnPinchDeltaChanged?.Invoke(delta);
                    _lastPinchDistance = currentDistance;
                }
            }
        }
        else
        {
            _isPinching = false;
            _lastPinchDistance = 0f;
        }

        _lastTouchCount = currentTouchCount;
    }

    // =========================================================
    // 接口回调 (Interface Implementations)
    // =========================================================

    // --- Duiling ---
    void InputActions_Scene1.IDuilingActions.OnDrag(InputAction.CallbackContext context)
    {
        if (context.performed) OnDragPerformed?.Invoke(context.ReadValue<Vector2>());
    }
    void InputActions_Scene1.IDuilingActions.OnTap(InputAction.CallbackContext context)
    {
        if (context.performed) OnDuilingTapPerformed?.Invoke();
    }
    void InputActions_Scene1.IDuilingActions.OnPinch(InputAction.CallbackContext context) { /* 已改用手动计算 */ }
    void InputActions_Scene1.IDuilingActions.OnPrimaryTouch(InputAction.CallbackContext context) { }
    void InputActions_Scene1.IDuilingActions.OnSecondTouch(InputAction.CallbackContext context) { }

    // --- Pingjin ---
    void InputActions_Scene1.IPingjinActions.OnPrimaryTouchContact(InputAction.CallbackContext context)
    {
        if (context.started) OnPrimaryTouchContactStarted?.Invoke();
        else if (context.canceled) OnPrimaryTouchContactCanceled?.Invoke();
    }
    void InputActions_Scene1.IPingjinActions.OnSecondaryTap(InputAction.CallbackContext context)
    {
        if (context.performed) OnSecondaryTapPerformed?.Invoke();
    }
    void InputActions_Scene1.IPingjinActions.OnDeviceTilt(InputAction.CallbackContext context)
    {
        if (context.performed) OnDeviceTilt?.Invoke(context.ReadValue<Vector3>());
    }
    void InputActions_Scene1.IPingjinActions.OnPrimaryTouch(InputAction.CallbackContext context) { }
    void InputActions_Scene1.IPingjinActions.OnSecondaryTouch(InputAction.CallbackContext context) { }

    // --- Panjin ---
    void InputActions_Scene1.IPanjinActions.OnNeedlePosition(InputAction.CallbackContext context)
    {
        if (context.performed) OnNeedlePositionMoved?.Invoke(context.ReadValue<Vector2>());
    }
    void InputActions_Scene1.IPanjinActions.OnTraceContact(InputAction.CallbackContext context)
    {
        if (context.started) OnTraceContactStarted?.Invoke();
        else if (context.canceled) OnTraceContactCanceled?.Invoke();
    }

    // =========================================================
    // 公共查询接口 (Getters)
    // =========================================================

    public Vector2 GetPrimaryTouchPosition()
    {
        if (inputActions.Duiling.enabled) return inputActions.Duiling.PrimaryTouch.ReadValue<Vector2>();
        if (inputActions.Pingjin.enabled) return inputActions.Pingjin.PrimaryTouch.ReadValue<Vector2>();
        if (inputActions.Panjin.enabled) return inputActions.Panjin.NeedlePosition.ReadValue<Vector2>();
        return Vector2.zero;
    }

    public bool IsPrimaryTouchPressed()
    {
        if (Touchscreen.current == null)
            return false;

        return Touchscreen.current.primaryTouch.press.isPressed;
    }
    public Vector2 GetSecondaryTouchPosition()
    {
        if (inputActions.Duiling.enabled) return inputActions.Duiling.SecondTouch.ReadValue<Vector2>();
        if (inputActions.Pingjin.enabled) return inputActions.Pingjin.SecondaryTouch.ReadValue<Vector2>();
        return Vector2.zero;
    }



    // =========================================================
    // 模式切换 (Mode Switching)
    // =========================================================

    public void SetMode(string modeName)
    {
        inputActions.Duiling.Disable();
        inputActions.Pingjin.Disable();
        inputActions.Panjin.Disable();

        switch (modeName)
        {
            case "Duiling": inputActions.Duiling.Enable(); break;
            case "Pingjin": inputActions.Pingjin.Enable(); break;
            case "Panjin": inputActions.Panjin.Enable(); break;
        }
    }
}