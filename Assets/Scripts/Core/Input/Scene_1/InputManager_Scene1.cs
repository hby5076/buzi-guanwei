using System;
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

public class InputManager_Scene1 : MonoBehaviour
{
    public static InputManager_Scene1 Instance { get; private set; }
    // ---------------------------------------------------------

    // 公共事件
    public event Action<Vector2> OnPrimaryTouchStarted;
    public event Action<Vector2> OnPrimaryTouchMoved;
    public event Action<Vector2> OnPrimaryTouchEnded;
    public event Action<Vector2> OnSecondaryTapPerformed;
    public event Action<Vector3> OnDeviceTiltChanged;
    public event Action<float> OnPinchDeltaChanged;

    // 辅助方法：获取事件订阅者数量（用于调试）
    public int GetOnPrimaryTouchStartedSubscriberCount() => OnPrimaryTouchStarted?.GetInvocationList()?.Length ?? 0;
    public int GetOnPrimaryTouchEndedSubscriberCount() => OnPrimaryTouchEnded?.GetInvocationList()?.Length ?? 0;

    // 内部状态
    private InputActions_Scene1 _actions; 
    private Vector3 _currentTiltData;
    private Vector2 _lastPrimaryTouchPos;
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
            // 输入资产初始化成功

        // 2. 绑定输入事件回调
        SetupInputCallbacks();

        // 3. 检测并初始化陀螺仪
        InitializeGyroscope();
    }

    void Update()
    {
        // 更新传感器数据
        UpdateSensorData();
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

        // 检查并启用陀螺仪设备
        var gyro = UnityEngine.InputSystem.Gyroscope.current;
        if (gyro != null)
        {
            // 如果陀螺仪未启用，尝试启用它
            if (!gyro.enabled)
            {
                try
                {
                    InputSystem.EnableDevice(gyro);
                    // 已手动启用陀螺仪设备
                    
                    // 给设备一些时间来初始化
                    if (Time.frameCount % 60 == 0) // 每秒检查一次
                    {
                        // 等待陀螺仪设备初始化
                    }
                }
                catch (System.Exception e)
                {
                    // 启用设备失败 - 静默处理
                    return;
                }
            }

            // 读取陀螺仪数据
            if (gyro.enabled)
            {
                try
                {
                    currentTilt = gyro.angularVelocity.ReadValue();
                    
                    // 只在有实际数据时才输出日志，避免刷屏
                    if (Time.frameCount % 120 == 0 && currentTilt != Vector3.zero) // 每2秒输出一次
                    {
                        // 设备读取成功
                    }
                }
                catch (System.Exception e)
                {
                    // 设备读取失败 - 静默处理
                }
            }
            else
            {
                // 陀螺仪存在但无法启用
                if (Time.frameCount % 180 == 0) // 每3秒提醒一次
                {
                    // 设备存在但无法启用 - 静默处理
                }
            }
        }
        else
        {
            // 完全没有陀螺仪设备
            if (Time.frameCount % 180 == 0) // 每3秒提醒一次
            {
                // 设备不支持陀螺仪或未正确配置 - 静默处理
            }
        }

        // 数据处理
        float sensitivity = 50f;
        currentTilt *= sensitivity;

        // 添加死区过滤微小抖动
        float deadZone = 0.05f;
        if (Mathf.Abs(currentTilt.x) < deadZone) currentTilt.x = 0;
        if (Mathf.Abs(currentTilt.y) < deadZone) currentTilt.y = 0;
        if (Mathf.Abs(currentTilt.z) < deadZone) currentTilt.z = 0;

        _currentTiltData = currentTilt;
        
        // 只有在有有效数据时才发布事件
        if (_currentTiltData != Vector3.zero)
        {
            OnDeviceTiltChanged?.Invoke(_currentTiltData);
        }
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
            // 活跃触摸点状态更新
        }

        // 3. 核心：修复后的状态机逻辑
        switch (_currentPinchState)
        {
            case PinchState.None:
                if (activeTouchCount == 1)
                {
                    // 单指按下状态
                    _currentPinchState = PinchState.SingleTouch;
                    _lastTouchCount = activeTouchCount;
                }
                // 重要：如果没有触摸，或者直接两根手指按下(activeTouchCount==2)，则保持None状态，等待单指先触发。
                // 这更符合“从单指到双指”的自然手势。
                break;

            case PinchState.SingleTouch:
                if (activeTouchCount == 2 && _lastTouchCount == 1)
                {
                    // 检测到第二指，进入捏合状态
                    _currentPinchState = PinchState.Pinching;
                    _lastPinchDistance = CalculateTouchDistance(activeTouchList); // 初始化
                }
                else if (activeTouchCount == 0)
                {
                    // 手指全部抬起，回到无触摸状态
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
                    // 捏合结束，回到单指或无触摸状态
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

        // 平金：主触摸开始
        _actions.Pingjin.PrimaryTouch.started += ctx =>
        {
            _lastPrimaryTouchPos = ctx.ReadValue<Vector2>();
            OnPrimaryTouchStarted?.Invoke(_lastPrimaryTouchPos);
        };

        // 平金：主触摸移动
        _actions.Pingjin.PrimaryTouch.performed += ctx =>
        {
            _lastPrimaryTouchPos = ctx.ReadValue<Vector2>();
            OnPrimaryTouchMoved?.Invoke(_lastPrimaryTouchPos);
        };

        // 平金：主触摸结束 - 修复：使用PrimaryTouchContact而不是PrimaryTouch
        _actions.Pingjin.PrimaryTouchContact.canceled += ctx =>
        {
            Debug.Log($"[InputManager] ========== PrimaryTouchContact.canceled事件触发 ==========");
            Debug.Log($"[InputManager] 触摸结束时间: {System.DateTime.Now:HH:mm:ss.fff}");
            Debug.Log($"[InputManager] 最后触摸位置: ({_lastPrimaryTouchPos.x:F1}, {_lastPrimaryTouchPos.y:F1})");
            Debug.Log($"[InputManager] OnPrimaryTouchEnded订阅者数量: {GetOnPrimaryTouchEndedSubscriberCount()}");
            
            OnPrimaryTouchEnded?.Invoke(_lastPrimaryTouchPos);
            
            Debug.Log($"[InputManager] OnPrimaryTouchEnded事件调用完成");
        };

        // 平金：次触摸点击（钉固）
        _actions.Pingjin.SecondaryTap.performed += ctx =>
        {
            Vector2 tapPos = _actions.Pingjin.SecondaryTouch.ReadValue<Vector2>();
            OnSecondaryTapPerformed?.Invoke(tapPos);
        };

        // 其他Action的回调可以在此按需添加...
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
                // 切换到平金输入模式
                break;
            case StateManager_Scene1.AppState.Duiling_Paste:
            case StateManager_Scene1.AppState.Duiling_Stretch:
                _actions.Duiling.Enable(); // 【关键修正】这里也要改
                // 切换到堆绫输入模式
                break;
            case StateManager_Scene1.AppState.Panjin_Draw:
                _actions.Panjin.Enable();
                // 切换到盘金输入模式
                break;
        }
    }

    /// <summary>
    /// 获取当前倾斜数据（统一接口）
    /// </summary>
    public Vector3 GetCurrentTiltData() => _currentTiltData;

    /// <summary>
    /// 获取第二触摸点位置（移动端专用）
    /// </summary>
    public Vector2 GetSecondaryTouchPosition()
    {
        if (_actions != null && _actions.Pingjin.enabled)
        {
            // 从Action读取触摸数据
            return _actions.Pingjin.SecondaryTouch.ReadValue<Vector2>();
        }
        return Vector2.zero;
    }

    /// <summary>
    /// 获取输入环境信息（用于调试显示）
    /// </summary>
    public string GetInputEnvironmentInfo()
    {
        string info = $"移动端输入模式\n活跃设备: ";
        if (Touchscreen.current != null) info += "[触摸屏] ";
        if (UnityEngine.InputSystem.Gyroscope.current != null) info += "[陀螺仪] ";
        return info;
    }

    /// <summary>
    /// 测试事件触发（仅用于调试和测试）
    /// </summary>
    public void TriggerTestEvents()
    {
        // 开始触发测试事件
        
        // 测试设备倾斜事件
        Vector3 testTilt = new Vector3(0.1f, 0.1f, 0f);
        OnDeviceTiltChanged?.Invoke(testTilt);
        // 触发设备倾斜测试事件
        
        // 测试主触摸移动事件
        Vector2 testTouchPos = new Vector2(100f, 200f);
        OnPrimaryTouchMoved?.Invoke(testTouchPos);
        // 触发主触摸移动测试事件
        
        // 测试次要点击事件
        Vector2 testTapPos = new Vector2(150f, 250f);
        OnSecondaryTapPerformed?.Invoke(testTapPos);
        // 触发次要点击测试事件
        
        // 测试事件触发完成
    }

    void OnEnable() => _actions?.Enable();
    void OnDisable() => _actions?.Disable();

    /// <summary>
    /// 初始化陀螺仪设备
    /// </summary>
    private void InitializeGyroscope()
    {
        // 开始初始化陀螺仪设备
        
        // 首先检查权限（仅Android需要）
        if (!CheckGyroscopePermissions())
        {
            // 权限检查失败，陀螺仪功能无法使用
            return;
        }
        
        var gyro = UnityEngine.InputSystem.Gyroscope.current;
        if (gyro != null)
        {
            try
            {
                // 强制启用陀螺仪设备
                if (!gyro.enabled)
                {
                    InputSystem.EnableDevice(gyro);
                    // 已成功启用陀螺仪设备
                }
                else
                {
                    // 陀螺仪设备已经启用
                }

                // 检查Input Action的绑定状态
                if (_actions != null)
                {
                    if (!_actions.Pingjin.DeviceTilt.enabled)
                    {
                        _actions.Pingjin.DeviceTilt.Enable();
                        // 已启用陀螺仪Action
                    }
                }

                // 验证陀螺仪是否真正可用
                StartCoroutine(VerifyGyroscopeAfterDelay());
            }
            catch (System.Exception e)
            {
                // 初始化失败 - 静默处理
            }
        }
        else
        {
            // 设备不支持陀螺仪功能 - 静默处理
            
            // 在Android设备上，提供额外的诊断信息
            #if UNITY_ANDROID
            // Android设备提示信息已移除
            #endif
        }
    }

    /// <summary>
    /// 延迟验证陀螺仪功能
    /// </summary>
    private System.Collections.IEnumerator VerifyGyroscopeAfterDelay()
    {
        // 等待1秒让陀螺仪完全初始化
        yield return new WaitForSeconds(1.0f);
        
        var gyro = UnityEngine.InputSystem.Gyroscope.current;
        if (gyro != null && gyro.enabled)
        {
            try
            {
                // 尝试读取一次数据来验证功能
                Vector3 testReading = gyro.angularVelocity.ReadValue();
                // 验证成功
                
                if (testReading == Vector3.zero)
                {
                    // 设备已启用但当前读数为零，正常状态
                }
            }
            catch (System.Exception e)
            {
                // 验证失败 - 静默处理
            }
        }
        else
        {
            // 验证失败：设备仍然不可用 - 静默处理
        }
    }

    /// <summary>
    /// 检查陀螺仪权限（仅Android平台需要）
    /// </summary>
    /// <returns>是否有权限访问陀螺仪</returns>
    private bool CheckGyroscopePermissions()
    {
        #if UNITY_ANDROID
        // Android平台需要检查权限
        // 检查Android设备权限
        
        // 检查是否有陀螺仪硬件支持
        if (!SystemInfo.supportsGyroscope)
        {
            // 设备不支持陀螺仪硬件 - 静默处理
            return false;
        }
        
        // 检查基本权限（虽然陀螺仪通常不需要特殊权限，但某些设备可能需要）
        // 注意：Unity中陀螺仪通常不需要运行时权限，但我们可以检查一些相关权限
        if (!Permission.HasUserAuthorizedPermission("android.permission.WAKE_LOCK"))
        {
            // WAKE_LOCK权限未授权，可能影响陀螺仪性能 - 静默处理
            // 尝试请求权限
            Permission.RequestUserPermission("android.permission.WAKE_LOCK");
        }
        
        // 检查设备是否处于允许传感器的状态
        try
        {
            // 尝试访问陀螺仪来验证权限
            var gyro = UnityEngine.InputSystem.Gyroscope.current;
            if (gyro == null)
            {
                // 无法获取陀螺仪设备，可能是权限问题 - 静默处理
                return false;
            }
            
            // 权限检查通过
            return true;
        }
        catch (System.Exception e)
        {
            // 权限检查异常 - 静默处理
            return false;
        }
        
        #else
        // 非Android平台通常不需要特殊权限检查
        // 非Android平台，跳过权限检查
        
        // 仍然检查硬件支持
        if (!SystemInfo.supportsGyroscope)
        {
            // 设备不支持陀螺仪硬件 - 静默处理
            return false;
        }
        
        return true;
        #endif
    }
}