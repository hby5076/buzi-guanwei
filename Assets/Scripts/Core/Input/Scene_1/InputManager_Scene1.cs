using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

public class InputManager_Scene1 : MonoBehaviour
{
    public static InputManager_Scene1 Instance { get; private set; }
    // ---------------------------------------------------------

    [Header("模拟设置 (仅编辑器)")]
    [SerializeField] private float _keyboardTiltSensitivity = 3.0f;
    [SerializeField] private float _pinchKeyboardSensitivity = 5.0f;

    // 公共事件
    public event Action<Vector2> OnPrimaryTouchMoved;
    public event Action<Vector2> OnSecondaryTapPerformed;
    public event Action<Vector3> OnDeviceTiltChanged;
    public event Action<float> OnPinchDeltaChanged;

    // 内部状态
    private InputActions_Scene1 _actions; 
    private Vector3 _currentTiltData;
    private Vector2 _lastPrimaryTouchPos;
    private bool _isUsingSimulatedInput = false;
    private float _lastPinchDistance = 0;
    private enum PinchState { None, SingleTouch, Pinching }
    private PinchState _currentPinchState = PinchState.None;
    private int _lastTouchCount = 0;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        // 1. 初始化Input Actions
        _actions = new InputActions_Scene1();
        _actions.Enable();
        Debug.Log("[InputManager] 输入资产初始化成功。");

        // 2. 绑定输入事件回调
        SetupInputCallbacks();

        // 3. 检测初始输入环境
        CheckInputEnvironment();
        Debug.Log($"[InputManager] 启动环境：模拟输入 = {_isUsingSimulatedInput}");

        // 4. 检测陀螺仪是否被启用
        if (UnityEngine.InputSystem.Gyroscope.current != null)
        {
            // 关键：强制启用陀螺仪设备
            InputSystem.EnableDevice(UnityEngine.InputSystem.Gyroscope.current);
            Debug.Log("[InputManager] 已启用陀螺仪设备");

            // 同时检查Input Action的绑定状态
            if (!_actions.Pingjin.DeviceTilt.enabled)
            {
                Debug.LogWarning("[InputManager] 陀螺仪Action未启用，尝试启用");
                _actions.Pingjin.DeviceTilt.Enable();
            }
        }
        else
        {
            Debug.LogError("[InputManager] 未检测到陀螺仪设备！请确认设备支持陀螺仪");
        }
    }

    void Update()
    {
        // 无论何种环境，都尝试更新传感器数据
        UpdateSensorData();

        // 模拟环境下，持续处理模拟输入（如键盘模拟陀螺仪)
        if (_isUsingSimulatedInput)
        {    
            HandleSimulatedInputs();
        }
        
    }

    private void UpdateSensorData()
    {
        // 1. 更新陀螺仪数据
        UpdateGyroscopeData();

        // 2. 更新真实触摸的捏合数据 (如果处于堆绫模式)
        if (_actions.Duiling.enabled)
        {
            UpdateRealPinchData();
        }
    }

    // 读取陀螺仪数据
    private void UpdateGyroscopeData()
    {
        if (!_actions.Pingjin.enabled) return;

        Vector3 currentTilt = Vector3.zero;

        // 方法1：检查直接访问陀螺仪设备
        if (UnityEngine.InputSystem.Gyroscope.current != null)
        {
            try
            {
                currentTilt = UnityEngine.InputSystem.Gyroscope.current.angularVelocity.ReadValue();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[陀螺仪] 直接读取失败: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning($"[陀螺仪] 未检测到陀螺仪设备");
        }

        // 方法2：通过Input Action读取
        if (_actions.Pingjin.DeviceTilt.enabled)
        {
            // 检查当前激活的控制设备
            var activeControl = _actions.Pingjin.DeviceTilt.activeControl;
            if (activeControl != null)
            {
                Debug.Log($"[陀螺仪] 激活的控制设备: {activeControl.device.name}");
            }

            try
            {
                currentTilt = _actions.Pingjin.DeviceTilt.ReadValue<Vector3>();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[陀螺仪] Action读取失败: {e.Message}");
            }
        }
        else
        {
            Debug.LogWarning($"[陀螺仪] DeviceTilt Action未启用");
        }

        // 发布数据（如果数据有变化）
        float sensitivity = 50f;
        currentTilt *= sensitivity;

        // 添加死区过滤微小抖动
        float deadZone = 0.05f;
        if (Mathf.Abs(currentTilt.x) < deadZone) currentTilt.x = 0;
        if (Mathf.Abs(currentTilt.y) < deadZone) currentTilt.y = 0;
        if (Mathf.Abs(currentTilt.z) < deadZone) currentTilt.z = 0;

        _currentTiltData = currentTilt;
        OnDeviceTiltChanged?.Invoke(_currentTiltData);
    }

    // 处理真实触摸屏的双指捏合手势
    private void UpdateRealPinchData()
    {
        if (Touchscreen.current == null) return;

        // 1. 重新编写：获取并统计当前真正活跃的触摸点
        int activeTouchCount = 0;
        // 使用列表存储活跃触摸点，供后续计算距离使用
        var activeTouchList = new System.Collections.Generic.List<UnityEngine.InputSystem.Controls.TouchControl>();

        for (int i = 0; i < Touchscreen.current.touches.Count; i++)
        {
            var touch = Touchscreen.current.touches[i];
            var phase = touch.phase.ReadValue();
            // 只统计处于 Began, Moved, Stationary 阶段的触摸点
            if (phase == UnityEngine.InputSystem.TouchPhase.Began || phase == UnityEngine.InputSystem.TouchPhase.Moved || phase == UnityEngine.InputSystem.TouchPhase.Stationary)
            {
                activeTouchList.Add(touch);
                activeTouchCount++;
            }
        }

        // 2. 调试日志 (限制输出频率，避免刷屏)
        if (Time.frameCount % 30 == 0)
        {
            Debug.Log($"[诊断] 活跃触摸点: {activeTouchCount}， 当前状态: {_currentPinchState}");
        }

        // 3. 核心：修复后的状态机逻辑
        switch (_currentPinchState)
        {
            case PinchState.None:
                if (activeTouchCount == 1)
                {
                    Debug.Log($"[状态机] 单指按下 -> 进入 SingleTouch");
                    _currentPinchState = PinchState.SingleTouch;
                    _lastTouchCount = activeTouchCount;
                }
                // 重要：如果没有触摸，或者直接两根手指按下(activeTouchCount==2)，则保持None状态，等待单指先触发。
                // 这更符合“从单指到双指”的自然手势。
                break;

            case PinchState.SingleTouch:
                if (activeTouchCount == 2 && _lastTouchCount == 1)
                {
                    Debug.Log($"[状态机] 检测到第二指 -> 进入 Pinching，初始化距离");
                    _currentPinchState = PinchState.Pinching;
                    _lastPinchDistance = CalculateTouchDistance(activeTouchList); // 初始化
                }
                else if (activeTouchCount == 0)
                {
                    Debug.Log($"[状态机] 手指全部抬起 -> 回到 None");
                    _currentPinchState = PinchState.None;
                    _lastPinchDistance = 0f;
                }
                // 其他情况（例如仍为1指）保持SingleTouch状态
                _lastTouchCount = activeTouchCount;
                break;

            case PinchState.Pinching:
                if (activeTouchCount == 2)
                {
                    float currentDistance = CalculateTouchDistance(activeTouchList);
                    // 调试日志：查看距离变化
                    // Debug.Log($"[捏合] 距离: {currentDistance}， 上帧: {_lastPinchDistance}");

                    if (_lastPinchDistance > 0)
                    {
                        float pinchDelta = (currentDistance - _lastPinchDistance) * 0.01f;
                        // 发布事件前，添加一个极小的死区过滤噪声
                        if (Mathf.Abs(pinchDelta) > 0.0005f)
                        {
                            // Debug.Log($"[捏合] 发布 Delta: {pinchDelta}");
                            OnPinchDeltaChanged?.Invoke(pinchDelta);
                        }
                    }
                    _lastPinchDistance = currentDistance;
                    _lastTouchCount = 2;
                }
                else if (activeTouchCount < 2) // 如果变成单指或全抬起
                {
                    Debug.Log($"[状态机] 捏合结束 -> 回到 SingleTouch 或 None");
                    _currentPinchState = (activeTouchCount == 1) ? PinchState.SingleTouch : PinchState.None;
                    _lastPinchDistance = 0f;
                    _lastTouchCount = activeTouchCount;
                }
                break;
        }
    }

    // 辅助方法：计算两指距离
    private float CalculateTouchDistance(System.Collections.Generic.List<UnityEngine.InputSystem.Controls.TouchControl> touches)
    {
        if (touches.Count < 2) return 0f;
        Vector2 pos0 = touches[0].position.ReadValue();
        Vector2 pos1 = touches[1].position.ReadValue();
        return Vector2.Distance(pos0, pos1);
    }

    // 辅助方法：初始化捏合距离
    private void InitializePinchDistance(System.Collections.Generic.List<UnityEngine.InputSystem.Controls.TouchControl> touches)
    {
        if (touches.Count < 2) return;

        _lastPinchDistance = CalculateTouchDistance(touches);
    }
    /// <summary>
    /// 将输入动作绑定到具体的事件处理函数
    /// </summary>
    private void SetupInputCallbacks()
    {
        if (_actions == null) return;

        // 平金：主触摸移动
        _actions.Pingjin.PrimaryTouch.performed += ctx =>
        {
            _lastPrimaryTouchPos = ctx.ReadValue<Vector2>();
            OnPrimaryTouchMoved?.Invoke(_lastPrimaryTouchPos);
        };

        // 平金：次触摸点击（钉固）
        _actions.Pingjin.SecondaryTap.performed += ctx =>
        {
            // 决定点击坐标的来源
            Vector2 tapPos = _isUsingSimulatedInput ? _lastPrimaryTouchPos : _actions.Pingjin.SecondaryTouch.ReadValue<Vector2>();
            OnSecondaryTapPerformed?.Invoke(tapPos);
        };

        // 其他Action的回调可以在此按需添加...
    }

    /// <summary>
    /// 检测当前是使用真机输入还是编辑器模拟输入
    /// </summary>
    private void CheckInputEnvironment()
    {
#if UNITY_EDITOR
        // 在编辑器下，如果有鼠标或键盘，优先认为使用模拟
        _isUsingSimulatedInput = (Mouse.current != null || Keyboard.current != null);
        Debug.Log($"[InputManager] 编辑器模式，使用模拟输入: {_isUsingSimulatedInput}");
#else
        // 在真机上，如果有触摸屏，则优先使用真实触摸
        _isUsingSimulatedInput = false; // 明确关闭模拟
        Debug.Log($"[InputManager] 真机模式，使用模拟输入: {_isUsingSimulatedInput}");
#endif
    }

    /// <summary>
    /// 处理所有通过键盘等设备模拟的输入
    /// </summary>
    private void HandleSimulatedInputs()
    {
        if (!_isUsingSimulatedInput) return;

#if UNITY_EDITOR
        // 模拟陀螺仪
        if (_actions.Pingjin.enabled)
        {
            Vector3 keyboardTilt = Vector3.zero;
            // 注意：这里假设你的DeviceTilt Action已按之前指导，用“3D Vector Composite”绑定了WASDQE键
            if (_actions.Pingjin.DeviceTilt.activeControl?.device is Keyboard)
            {
                keyboardTilt = _actions.Pingjin.DeviceTilt.ReadValue<Vector3>();
                _currentTiltData = keyboardTilt * _keyboardTiltSensitivity;
                OnDeviceTiltChanged?.Invoke(_currentTiltData);
            }
        }

        // 模拟双指捏合 
        if (_actions.Duiling.enabled && Keyboard.current != null)
        {
            float pinchDelta = 0f;
            if (Keyboard.current.eKey.isPressed) pinchDelta += 1f; // E键放大
            if (Keyboard.current.qKey.isPressed) pinchDelta -= 1f; // Q键缩小
            if (Mathf.Abs(pinchDelta) > 0.01f)
            {
                OnPinchDeltaChanged?.Invoke(pinchDelta * _pinchKeyboardSensitivity * Time.deltaTime);
            }
        }
#endif
    }

    /// <summary>
    /// 供StateManager调用，切换当前激活的输入模式
    /// </summary>
    public void SwitchInputMap(StateManager_Scene1.AppState targetState)
    {
        if (_actions == null) return;

        // 禁用所有Action Maps
        _actions.Pingjin.Disable();
        _actions.Duiling.Disable(); // 【关键修正】这里也要改
        _actions.Panjin.Disable();

        // 根据状态启用对应的Map
        switch (targetState)
        {
            case StateManager_Scene1.AppState.Pingjin_Guide:
            case StateManager_Scene1.AppState.Pingjin_Inspection:
            case StateManager_Scene1.AppState.Pingjin_Repair:
                _actions.Pingjin.Enable();
                Debug.Log($"[InputManager] 切换到：平金输入模式");
                break;
            case StateManager_Scene1.AppState.Duiling_Paste:
            case StateManager_Scene1.AppState.Duiling_Stretch:
                _actions.Duiling.Enable(); // 【关键修正】这里也要改
                Debug.Log($"[InputManager] 切换到：堆绫输入模式");
                break;
            case StateManager_Scene1.AppState.Panjin_Draw:
                _actions.Panjin.Enable();
                Debug.Log($"[InputManager] 切换到：盘金输入模式");
                break;
        }
    }

    /// <summary>
    /// 获取当前倾斜数据（统一接口）
    /// </summary>
    public Vector3 GetCurrentTiltData() => _currentTiltData;

    /// <summary>
    /// 获取第二触摸点位置（统一接口，考虑了模拟输入）
    /// </summary>
    public Vector2 GetSecondaryTouchPosition()
    {
        if (_isUsingSimulatedInput)
        {
            // 模拟环境下，按住Alt时鼠标位置作为第二指
            if (Keyboard.current != null && Keyboard.current.altKey.isPressed && Mouse.current != null)
            {
                return Mouse.current.position.ReadValue();
            }
        }
        else if (_actions != null && _actions.Pingjin.enabled)
        {
            // 真实环境下，直接从Action读取
            return _actions.Pingjin.SecondaryTouch.ReadValue<Vector2>();
        }
        return Vector2.zero;
    }

    /// <summary>
    /// 获取输入环境信息（用于调试显示）
    /// </summary>
    public string GetInputEnvironmentInfo()
    {
        string info = $"使用模拟输入: {_isUsingSimulatedInput}\n活跃设备: ";
        if (Touchscreen.current != null) info += "[触摸屏] ";
        if (Mouse.current != null) info += "[鼠标] ";
        if (Keyboard.current != null) info += "[键盘] ";
        if (UnityEngine.InputSystem.Gyroscope.current != null) info += "[陀螺仪] ";
        return info;
    }

    void OnEnable() => _actions?.Enable();
    void OnDisable() => _actions?.Disable();
}