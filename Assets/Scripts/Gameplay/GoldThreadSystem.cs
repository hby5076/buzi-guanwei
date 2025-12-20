using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public class GoldThreadSystem : MonoBehaviour
{
    // 单例实例
    public static GoldThreadSystem Instance { get; private set; }

    [Header("调试设置")]
    public bool showDebugInfo = true;           // 是否显示调试信息
    public bool enableTouchObjectDebug = true; // 是否启用触摸物体识别调试

    // 接口挂载点
    [Header("组件挂载点")]
    public Transform spoolTransform;          // 线轴挂载点
    public Transform baseClothTransform;      // 底布挂载点
    public Transform goldClothTransform;      // 金布挂载点
    public Transform lightSourceTransform;    // 光源挂载点

    // 线轴底部固定点（开放接口）
    public Transform spoolBottomPoint;

    // 金线设置
    [Header("金线设置")]
    public GameObject goldThreadPrefab;      // 金线预制体（包含LineRenderer组件）
    public float goldThreadWidth = 0.1f;      // 金线宽度（可调参数，增加默认值确保可见性）
    public float goldClothWidth = 0.1f;       // 金布宽度（金线周围的范围）
    public float apertureDistanceThreshold = 0.5f; // 光圈生成的距离阈值
    public float goldThreadBreakThreshold = 0.2f;  // 金线断开的距离阈值

    // 光源设置
    [Header("光源设置")]
    public float lightSphereRadius = 5.0f;    // 光源移动的球面半径
    public float lightSensitivity = 0.5f;     // 光源对陀螺仪输入的灵敏度
    public float lightSmoothSpeed = 8.0f;     // 光源移动的平滑速度
    public bool useInverseMapping = true;     // 是否使用反向映射（手机倾斜与补子倾斜相反）
    
    [Header("光源优化设置")]
    public float deadZone = 0.02f;           // 死区阈值，过滤微小抖动
    public float responsiveness = 15.0f;     // 响应速度，控制光源跟随的灵敏度
    public bool useAdvancedSmoothing = true;  // 是否使用高级平滑算法
    public float predictionFactor = 0.3f;     // 预测因子，提前预测移动方向

    // 内部状态
    private enum ThreadState
    {
        Idle,
        Dragging,
        Rooted,
        WaitingForApertureClick,
        Disconnected
    }

    // 全局交互状态 - 新增状态管理系统
    public enum GlobalInteractionState
    {
        Idle = 0,           // 闲置状态 - 用户可以通过移动手机观察反光
        Dragging = 1,       // 牵引状态 - 用户成功从线轴中引出金线，以手指牵引金线移动
        Paving = 2          // 打底状态 - 用户开始真正将金线铺进底部，金色的底布开始产生
    }

    // 当前全局交互状态
    private GlobalInteractionState currentGlobalState = GlobalInteractionState.Idle;

    // 提供公共接口获取当前全局状态
    public GlobalInteractionState CurrentGlobalState => currentGlobalState;

    // 全局状态转换方法 - 核心状态管理
    private void TransitionToGlobalState(GlobalInteractionState newState)
    {
        GlobalInteractionState previousState = currentGlobalState;
        currentGlobalState = newState;
        
        Debug.Log($"[全局状态] 状态转换: {previousState} -> {newState}");
        
        // 根据新状态执行相应的初始化操作
        switch (newState)
        {
            case GlobalInteractionState.Idle:
                Debug.Log("[全局状态] 进入闲置状态 - 用户可以通过移动手机观察反光");
                break;
                
            case GlobalInteractionState.Dragging:
                Debug.Log("[全局状态] 进入牵引状态 - 用户成功从线轴中引出金线，以手指牵引金线移动");
                break;
                
            case GlobalInteractionState.Paving:
                Debug.Log("[全局状态] 进入打底状态 - 用户开始真正将金线铺进底部，金色的底布开始产生");
                break;
        }
        
        // 更新调试UI显示全局状态
        UpdateDebugInfo();
    }

    private ThreadState currentThreadState = ThreadState.Idle;
    private GameObject currentGoldThread;     // 当前正在绘制的金线
    private LineRenderer currentLineRenderer;
    private List<Vector3> threadPoints = new List<Vector3>();
    private List<Vector3> fixedThreadPoints = new List<Vector3>();
    private Vector2 lastTouchPosition;
    private Vector3 spoolBasePosition;
    private float distanceSinceLastAperture = 0f;
    private List<Aperture> activeApertures = new List<Aperture>();
    private GameObject goldClothMesh;
    
    // 光源高级平滑变量
    private Vector3 lastTargetPosition;
    private Vector3 currentVelocity;
    private Vector3 lastFilteredTilt;
    private float lastUpdateTime;
    private bool isFirstUpdate = true;
    
    // 长按检测
    private float longPressDuration = 0.5f;    // 长按时长
    private float currentPressTime = 0f;
    private bool isPressing = false;
    private Vector3 pressStartPosition;
    
    // 金布区域管理
    private List<Vector3> goldClothRegions = new List<Vector3>();
    private float goldClothGenerationInterval = 0.05f; // 金布区域生成的间隔
    private float lastGoldClothUpdate = 0f;

    // 光圈类
    [Serializable]
    private class Aperture
    {
        public GameObject gameObject;
        public Vector3 position;
        public int threadPointIndex;
        public bool isClicked;
    }

    // 输入事件订阅
    private void OnEnable()
    {
        // 订阅InputManager_Scene1的事件
        if (InputManager_Scene1.Instance != null)
        {
            // 调试日志：检查事件订阅前的状态
            int subscriberCountBefore = InputManager_Scene1.Instance.GetOnPrimaryTouchStartedSubscriberCount();
            int endSubscriberCountBefore = InputManager_Scene1.Instance.GetOnPrimaryTouchEndedSubscriberCount();
            Debug.Log($"[事件订阅] 订阅前 - OnPrimaryTouchStarted订阅者数量: {subscriberCountBefore}");
            Debug.Log($"[事件订阅] 订阅前 - OnPrimaryTouchEnded订阅者数量: {endSubscriberCountBefore}");
            
            InputManager_Scene1.Instance.OnPrimaryTouchStarted += HandlePrimaryTouchStarted;
            InputManager_Scene1.Instance.OnPrimaryTouchMoved += HandlePrimaryTouchMoved;
            InputManager_Scene1.Instance.OnPrimaryTouchEnded += HandlePrimaryTouchEnded;
            InputManager_Scene1.Instance.OnSecondaryTapPerformed += HandleSecondaryTapPerformed;
            InputManager_Scene1.Instance.OnDeviceTiltChanged += HandleDeviceTiltChanged;
            
            // 调试日志：检查事件订阅后的状态
            int subscriberCountAfter = InputManager_Scene1.Instance.GetOnPrimaryTouchStartedSubscriberCount();
            int endSubscriberCountAfter = InputManager_Scene1.Instance.GetOnPrimaryTouchEndedSubscriberCount();
            Debug.Log($"[事件订阅] 订阅后 - OnPrimaryTouchStarted订阅者数量: {subscriberCountAfter}");
            Debug.Log($"[事件订阅] 订阅后 - OnPrimaryTouchEnded订阅者数量: {endSubscriberCountAfter}");
            Debug.Log($"[事件订阅] GoldThreadSystem成功订阅所有输入事件");
        }
        else
        {
            // 输入管理器未找到 - 静默处理
            Debug.LogError("[事件订阅] InputManager_Scene1.Instance为空，无法订阅输入事件");
        }
    }

    private void OnDisable()
    {
        if (InputManager_Scene1.Instance != null)
        {
            InputManager_Scene1.Instance.OnPrimaryTouchStarted -= HandlePrimaryTouchStarted;
            InputManager_Scene1.Instance.OnPrimaryTouchMoved -= HandlePrimaryTouchMoved;
            InputManager_Scene1.Instance.OnPrimaryTouchEnded -= HandlePrimaryTouchEnded;
            InputManager_Scene1.Instance.OnSecondaryTapPerformed -= HandleSecondaryTapPerformed;
            InputManager_Scene1.Instance.OnDeviceTiltChanged -= HandleDeviceTiltChanged;
        }
    }

    private void Awake()
    {
        // 单例模式
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // 初始化组件
        InitializeComponents();
    }

    private void Start()
    {
        // 隐藏金布
        if (goldClothTransform != null)
        {
            goldClothTransform.gameObject.SetActive(false);
        }

        // 设置线轴底部固定点
        if (spoolBottomPoint != null)
        {
            spoolBasePosition = spoolBottomPoint.position;
        }
        else if (spoolTransform != null)
        {
            // 如果没有设置线轴底部点，默认使用线轴底部位置
            spoolBasePosition = spoolTransform.position - spoolTransform.up * spoolTransform.localScale.y * 0.5f;
        }

        // 初始化光源位置
        InitializeLightSource();
        
        // 验证材质挂载情况
        ValidateMaterialSetup();
    }

    private void LateUpdate()
    {
        // 降低检查频率，每5帧检查一次
        if (Time.frameCount % 5 != 0)
        {
            return;
        }
        
        // 最终材质保障机制：确保每帧最后都使用正确的材质
        if (currentLineRenderer != null && currentGoldThread != null)
        {
            // 从预制体获取目标材质
            Material targetMaterial = GetGoldThreadMaterial();
            if (targetMaterial == null)
            {
                return;
            }
            
            // 关键修复：彻底避免访问.material属性，只使用sharedMaterial
            // 这是最重要的修复 - 任何对.material的访问都会创建实例！
            
            // 检查sharedMaterial引用
            Material currentShared = currentLineRenderer.sharedMaterial;
            if (currentShared != targetMaterial)
            {
                Debug.LogWarning("[材质保障] 检测到sharedMaterial被覆盖，强制恢复用户材质");
                Debug.LogWarning($"[材质保障] 当前sharedMaterial: {currentShared?.name} (ID: {currentShared?.GetInstanceID()})");
                Debug.LogWarning($"[材质保障] 应该材质: {targetMaterial.name} (ID: {targetMaterial.GetInstanceID()})");
                
                // 强制材质恢复 - 重复设置确保生效
                currentLineRenderer.sharedMaterial = targetMaterial;
                currentLineRenderer.sharedMaterial = targetMaterial; // 双重保险
                
                // 同时恢复LineRenderer颜色
                currentLineRenderer.startColor = targetMaterial.color;
                currentLineRenderer.endColor = targetMaterial.color;
                
                // 验证恢复结果
                Material verifyShared = currentLineRenderer.sharedMaterial;
                if (verifyShared != targetMaterial)
                {
                    Debug.LogError("[材质保障] sharedMaterial恢复失败！Unity可能强制创建了实例");
                    Debug.LogError("[材质保障] 这可能是Unity的内部问题，但不会创建材质副本");
                    Debug.LogError("[材质保障] 继续使用当前材质，让Unity处理材质实例化");
                }
                else
                {
                    Debug.Log("[材质保障] sharedMaterial恢复成功");
                }
            }
            
            // 每隔一段时间强制刷新sharedMaterial引用，防止Unity内部覆盖
            if (Time.time % 5.0f < Time.deltaTime) // 改为每5秒检查一次，进一步减少频率
            {
                // 只使用sharedMaterial，绝对避免触发material实例化
                currentLineRenderer.sharedMaterial = targetMaterial;
                
                // 同时设置LineRenderer颜色
                currentLineRenderer.startColor = targetMaterial.color;
                currentLineRenderer.endColor = targetMaterial.color;
                
                // 静默验证，减少日志噪音
                if (currentLineRenderer.sharedMaterial != targetMaterial)
                {
                    Debug.LogError("[材质保障] 定期检查发现sharedMaterial设置失败");
                }
            }
        }
    }

    private void Update()
    {
        // 处理长按检测
        HandleLongPress();
        
        // 添加状态监控调试信息 - 降低频率
        if (currentGoldThread != null)
        {
            // 每10秒输出一次状态信息，进一步减少频率
            if (Time.time % 10.0f < Time.deltaTime)
            {
                Debug.Log($"[Update监控] 当前状态: {currentThreadState}, 金线存在: {currentGoldThread != null}, LineRenderer存在: {currentLineRenderer != null}");
                if (currentLineRenderer != null)
                {
                    Material currentMat = currentLineRenderer.sharedMaterial;
                    Debug.Log($"[Update监控] LineRenderer材质: {currentMat?.name} (ID: {currentMat?.GetInstanceID()}), 颜色: {currentMat?.color}");
                    
                    // 实时材质检查和修复 - 降低频率，避免与其他方法冲突
                    Material targetMaterial = GetGoldThreadMaterial();
                    if (targetMaterial != null && currentMat != targetMaterial)
                    {
                        Debug.LogWarning("[Update监控] 检测到材质异常，立即修复");
                        Debug.LogWarning($"[Update监控] 当前材质: {currentMat?.name} (ID: {currentMat?.GetInstanceID()})");
                        Debug.LogWarning($"[Update监控] 应该材质: {targetMaterial.name} (ID: {targetMaterial.GetInstanceID()})");
                        
                        // 立即修复材质
                        currentLineRenderer.sharedMaterial = targetMaterial;
                        
                        // 同时修复LineRenderer颜色
                        currentLineRenderer.startColor = targetMaterial.color;
                        currentLineRenderer.endColor = targetMaterial.color;
                        
                        // 验证修复结果
                        Material fixedMat = currentLineRenderer.sharedMaterial;
                        if (fixedMat == targetMaterial)
                        {
                            Debug.Log("[Update监控] 材质修复成功");
                        }
                        else
                        {
                            Debug.LogError("[Update监控] 材质修复失败");
                            Debug.LogError("[Update监控] 这可能是Unity的内部问题，但不会创建材质副本");
                            Debug.LogError("[Update监控] 继续使用当前材质，让Unity处理材质实例化");
                        }
                    }
                }
            }
        }
        
        // 更新调试信息
        if (showDebugInfo)
        {
            UpdateDebugInfo();
        }
    }

    private void InitializeComponents()
    {
        // 验证必要的挂载点
        if (spoolTransform == null)
        {
            // 线轴挂载点未设置 - 静默处理
        }
        else
        {
            spoolBasePosition = spoolTransform.position;
        }

        if (baseClothTransform == null)
        {
            // 底布挂载点未设置 - 静默处理
        }

        if (goldClothTransform == null)
        {
            // 金布挂载点未设置 - 静默处理
        }

        if (lightSourceTransform == null)
        {
            // 光源挂载点未设置 - 静默处理
        }

        if (spoolBottomPoint == null)
        {
            spoolBasePosition = spoolTransform.position;
        }
        else
        {
            spoolBasePosition = spoolBottomPoint.position;
        }
    }

    private void InitializeLightSource()
    {
        if (lightSourceTransform != null)
        {
            // 将光源初始位置设置在球面上
            lightSourceTransform.position = Vector3.up * lightSphereRadius;
        }
    }

    // 金线扎根
    private void RootGoldThread(Vector3 position)
    {
        if (currentGoldThread == null || currentLineRenderer == null)
        {
            return;
        }

        // 确保位置在底布上
        if (!IsPositionOnBaseCloth(position))
        {
            return;
        }

        // 金线扎根，开始绘制曲线
        currentThreadState = ThreadState.Rooted;
        
        // 转换到全局打底状态
        TransitionToGlobalState(GlobalInteractionState.Paving);
        
        // 更新线程点
        if (threadPoints.Count < 2)
        {
            threadPoints.Add(position);
        }
        else
        {
            threadPoints[threadPoints.Count - 1] = position;
        }
        
        fixedThreadPoints.Add(position);
        UpdateLineRenderer();
        
        // 更新金布可见性
        UpdateGoldClothVisibility();
    }

    // 处理长按检测
    private void HandleLongPress()
    {
        if (isPressing)
        {
            currentPressTime += Time.deltaTime;
            
            // 检查长按时间是否达到阈值
            if (currentPressTime >= longPressDuration)
            {
                Debug.Log($"[长按检测] 长按时间达到阈值: {currentPressTime:F2}s >= {longPressDuration:F2}s");
                
                // 检查是否在底布上长按
                if ((currentThreadState == ThreadState.Dragging || currentThreadState == ThreadState.Rooted) && IsPositionOnBaseCloth(pressStartPosition))
                {
                    // 判断长按位置是否为spool transform挂载的物体
                    bool isOnSpool = IsTouchOnSpoolTransform(Camera.main.WorldToScreenPoint(pressStartPosition));
                    
                    // 在底布上长按，金线扎根
                    RootGoldThread(pressStartPosition);
                }
                // 检查是否在线轴上长按（空闲状态下）
                else if (currentThreadState == ThreadState.Idle && IsTouchOnSpoolTransform(Camera.main.WorldToScreenPoint(pressStartPosition)))
                {
                    Debug.Log("[金线拉取] 在线轴上长按，开始创建金线");
                    // 在线轴上长按，开始新的金线
                    StartNewGoldThread(pressStartPosition);
                }
                else if (currentThreadState == ThreadState.Idle)
                {
                    // 空闲状态下长按但未在线轴或底布上，金线拉伸失败
                    Debug.Log($"[金线拉取] 长按失败 - 未在线轴上。位置: {pressStartPosition}");
                    Debug.Log("[是否成功拉伸出金线]: 否");
                }
                
                // 重置长按状态
                ResetLongPress();
            }
        }
    }

    // 重置长按状态
    private void ResetLongPress()
    {
        Debug.Log($"[长按重置] 重置前 - 按压中: {isPressing}, 时间: {currentPressTime:F2}s");
        
        isPressing = false;
        currentPressTime = 0f;
        pressStartPosition = Vector3.zero;
        
        Debug.Log("[长按重置] 长按状态已重置");
    }

    // 验证状态一致性
    private void ValidateStateConsistency()
    {
        // 检查状态与金线对象的一致性
        if (currentThreadState == ThreadState.Idle && currentGoldThread != null)
        {
            Debug.LogWarning("[状态验证] 异常：空闲状态但存在金线对象");
        }
        
        if ((currentThreadState == ThreadState.Dragging || currentThreadState == ThreadState.Rooted || currentThreadState == ThreadState.WaitingForApertureClick) 
            && currentGoldThread == null)
        {
            Debug.LogWarning("[状态验证] 异常：活动状态但无金线对象");
        }
        
        // 检查长按状态一致性
        if (isPressing && currentThreadState != ThreadState.Idle)
        {
            Debug.LogWarning("[状态验证] 异常：非空闲状态下仍在长按检测");
        }
    }

    // 开始长按检测
    private void StartLongPress(Vector3 position)
    {
        isPressing = true;
        currentPressTime = 0f;
        pressStartPosition = position;
    }

    // 检查触摸是否点击了spool transform挂载的物体
    private bool IsTouchOnSpoolTransform(Vector2 screenPosition)
    {
        if (spoolTransform == null)
        {
            Debug.Log("[射线检测] 线轴Transform为空");
            return false;
        }

        // 检查spool是否有Collider
        Collider spoolCollider = spoolTransform.GetComponent<Collider>();
        if (spoolCollider == null)
        {
            Debug.Log("[射线检测] 线轴没有Collider组件");
            return false;
        }

        // 执行射线检测
        Ray ray = Camera.main.ScreenPointToRay(screenPosition);
        RaycastHit hit;
        
        if (Physics.Raycast(ray, out hit, Mathf.Infinity))
        {
            GameObject hitObject = hit.collider.gameObject;
            Debug.Log($"[射线检测] 射线击中物体: {hitObject.name}");
            
            // 检查是否直接点击了线轴物体
            if (hitObject == spoolTransform.gameObject)
            {
                Debug.Log("[射线检测] 直接击中线轴主体");
                return true;
            }
            
            // 检查是否点击了线轴的子物体
            Transform hitTransform = hitObject.transform;
            if (hitTransform.IsChildOf(spoolTransform))
            {
                Debug.Log($"[射线检测] 击中线轴子物体: {hitObject.name}");
                return true;
            }
        }
        else
        {
            Debug.Log("[射线检测] 射线未击中任何物体");
        }
        
        return false;
    }

    // 检查触摸是否在线轴附近
    private bool IsTouchNearSpool(Vector2 touchPosition)
    {
        if (spoolTransform == null)
        {
            return false;
        }

        Vector3 spoolScreenPosition = Camera.main.WorldToScreenPoint(spoolTransform.position);
        float distance = Vector2.Distance(touchPosition, spoolScreenPosition);
        bool isNear = distance < 50f; // 50像素范围内
        
        return isNear;
    }

    // 开始新的金线绘制
    private void StartNewGoldThread(Vector3 initialPosition)
    {
        if (currentGoldThread != null)
        {
            EndCurrentGoldThread();
        }

        // 确保线轴底部位置已正确设置
        if (spoolTransform != null)
        {
            if (spoolBottomPoint != null)
            {
                spoolBasePosition = spoolBottomPoint.position;
            }
            else
            {
                // 如果没有设置线轴底部点，默认使用线轴底部位置
                spoolBasePosition = spoolTransform.position - spoolTransform.up * spoolTransform.localScale.y * 0.5f;
            }
        }
        else
        {
            // 无法开始新金线：线轴挂载点未设置 - 静默处理
            return;
        }

        // 创建新的金线 - 使用预制体实例化
        if (goldThreadPrefab == null)
        {
            Debug.LogError("[金线创建] 错误：未挂载金线预制体！请在Inspector中为goldThreadPrefab参数指定预制体");
            return;
        }
        
        currentGoldThread = Instantiate(goldThreadPrefab, transform);
        currentGoldThread.name = "GoldThread_Instance";
        currentLineRenderer = currentGoldThread.GetComponent<LineRenderer>();
        
        if (currentLineRenderer == null)
        {
            Debug.LogError("[金线创建] 错误：预制体上未找到LineRenderer组件！");
            Destroy(currentGoldThread);
            currentGoldThread = null;
            return;
        }
        
        // 获取预制体的材质
        Material targetMaterial = GetGoldThreadMaterial();
        if (targetMaterial == null)
        {
            Debug.LogError("[金线创建] 错误：无法从预制体获取材质！");
            Destroy(currentGoldThread);
            currentGoldThread = null;
            return;
        }
        
        // 直接使用预制体的材质，不创建副本
        // 预制体已经正确设置了材质和颜色
        currentLineRenderer.sharedMaterial = targetMaterial;
        
        // 验证设置是否成功
        Material verifyMaterial = currentLineRenderer.sharedMaterial;
        if (verifyMaterial != targetMaterial)
        {
            Debug.LogError("[金线材质] 材质设置失败，Unity可能强制创建了实例");
            Debug.LogError("[金线材质] 这可能是Unity的内部问题，尝试继续使用当前材质");
        }
        else
        {
            Debug.Log("[金线材质] 预制体材质设置成功");
        }
        
        // 输出材质信息
        Debug.Log($"[金线材质] 使用预制体材质");
        Debug.Log($"[金线材质] 材质名称: {targetMaterial.name}");
        Debug.Log($"[金线材质] Shader: {targetMaterial.shader.name}");
        Debug.Log($"[金线材质] 主颜色: {targetMaterial.color}");
        Debug.Log($"[金线材质] 渲染队列: {targetMaterial.renderQueue}");
        Debug.Log($"[金线材质] 是否启用实例化: {targetMaterial.enableInstancing}");
        
        // 验证材质是否支持LineRenderer
        if (!targetMaterial.shader.isSupported)
        {
            Debug.LogError($"[金线材质] 警告：材质Shader {targetMaterial.shader.name} 在当前设备上不支持！");
        }
        
        // 检查材质的关键属性
        LogMaterialProperties(targetMaterial);
        
        // 设置金线宽度 - 使用预制体的宽度设置
        float actualWidth = Mathf.Max(goldThreadWidth, 0.1f); // 增加最小宽度到0.1
        currentLineRenderer.startWidth = actualWidth;
        currentLineRenderer.endWidth = actualWidth;
        currentLineRenderer.positionCount = 2;
        
        // 注意：尽量使用预制体的LineRenderer设置，减少覆盖
        // 只设置必要的属性，其他属性使用预制体的设置
        
        // 设置LineRenderer颜色，确保与材质颜色一致
        currentLineRenderer.startColor = targetMaterial.color;
        currentLineRenderer.endColor = targetMaterial.color;
        
        // 最终确保材质设置
        currentLineRenderer.sharedMaterial = targetMaterial;
        Debug.Log("[金线材质] 最终材质设置完成，使用预制体材质");
        
        // 初始化金线点 - 确保正确的端点位置
        threadPoints.Clear();
        fixedThreadPoints.Clear();
        
        // 第一个端点：线轴底部固定点
        threadPoints.Add(spoolBasePosition);
        
        // 第二个端点：手指当前触摸位置的投影点
        threadPoints.Add(initialPosition);
        
        UpdateLineRenderer();

        currentThreadState = ThreadState.Dragging;
        distanceSinceLastAperture = 0f;
        LogOperationStatus("成功拉出金线");
        
        // 转换到全局牵引状态
        TransitionToGlobalState(GlobalInteractionState.Dragging);
        
        // 临时调试：输出金线端点位置
        Debug.Log($"[金线端点] 线轴: {spoolBasePosition}, 手指: {initialPosition}");
    }

    // 更新金线位置
    private void UpdateGoldThreadPosition(Vector3 newPosition)
    {
        if (currentGoldThread == null || currentLineRenderer == null)
        {
            return;
        }

        // 调试日志：记录输入的新位置
        Debug.Log($"[更新金线位置] 输入新位置: ({newPosition.x:F3}, {newPosition.y:F3}, {newPosition.z:F3}), 当前状态: {currentThreadState}");
        
        // 快速材质验证（避免频繁调用影响性能）
        if (Time.frameCount % 60 == 0) // 每秒检查一次
        {
            ValidateAndFixMaterial();
        }

        // 在Dragging状态下，金线只有两个端点：
        // 第一个端点：线轴底部固定点（保持不变）
        // 第二个端点：手指当前触摸位置的投影点（实时更新）
        if (currentThreadState == ThreadState.Dragging)
        {
            // 调试日志：记录更新前的threadPoints状态
            Debug.Log($"[更新金线位置] 更新前threadPoints数量: {threadPoints.Count}");
            if (threadPoints.Count >= 2)
            {
                Debug.Log($"[更新金线位置] 更新前端点1: ({threadPoints[0].x:F3}, {threadPoints[0].y:F3}, {threadPoints[0].z:F3})");
                Debug.Log($"[更新金线位置] 更新前端点2: ({threadPoints[1].x:F3}, {threadPoints[1].y:F3}, {threadPoints[1].z:F3})");
            }

            // 确保金线始终只有两个点
            if (threadPoints.Count != 2)
            {
                threadPoints.Clear();
                threadPoints.Add(spoolBasePosition); // 固定端点
                threadPoints.Add(newPosition);       // 移动端点
                Debug.Log($"[更新金线位置] 重新初始化threadPoints，端点1(线轴): ({spoolBasePosition.x:F3}, {spoolBasePosition.y:F3}, {spoolBasePosition.z:F3}), 端点2(手指): ({newPosition.x:F3}, {newPosition.y:F3}, {newPosition.z:F3})");
            }
            else
            {
                // 只更新第二个端点（手指位置）
                threadPoints[1] = newPosition;
                Debug.Log($"[更新金线位置] 更新端点2为: ({newPosition.x:F3}, {newPosition.y:F3}, {newPosition.z:F3})");
            }
            
            // 强制更新LineRenderer
            UpdateLineRenderer();
            
            // 调试日志：记录更新后的状态
            Debug.Log($"[更新金线位置] 更新后threadPoints数量: {threadPoints.Count}");
            Debug.Log($"[更新金线位置] 更新后端点1: ({threadPoints[0].x:F3}, {threadPoints[0].y:F3}, {threadPoints[0].z:F3})");
            Debug.Log($"[更新金线位置] 更新后端点2: ({threadPoints[1].x:F3}, {threadPoints[1].y:F3}, {threadPoints[1].z:F3})");
        }
        else
        {
            // 其他状态下的处理
            threadPoints[threadPoints.Count - 1] = newPosition;
            UpdateLineRenderer();
        }

        // 检查是否拖动到底布上
        if (currentThreadState == ThreadState.Dragging && IsPositionOnBaseCloth(newPosition))
        {
            currentThreadState = ThreadState.Rooted;
            
            // 确保固定点列表正确初始化
            if (fixedThreadPoints.Count == 0)
            {
                fixedThreadPoints.Add(spoolBasePosition);
            }
            fixedThreadPoints.Add(newPosition);
        }
    }

    // 在底布上绘制金线曲线
    private void DrawGoldThreadCurve(Vector3 newPosition)
    {
        if (currentGoldThread == null || currentLineRenderer == null)
        {
            return;
        }

        // 检查新位置是否与金布区域重叠
        if (IsPositionOverlappingGoldCloth(newPosition))
        {
            // 金线不能穿过已转化的金布区域，结束当前金线
            EndCurrentGoldThread();
            return;
        }

        // 添加新的曲线点
        threadPoints.Add(newPosition);
        fixedThreadPoints.Add(newPosition);
        UpdateLineRenderer();

        // 更新金布可见性
        UpdateGoldClothVisibility();

        // 计算距离并检查是否生成光圈
        Vector3 lastPoint = threadPoints[threadPoints.Count - 2];
        float segmentDistance = Vector3.Distance(lastPoint, newPosition);
        distanceSinceLastAperture += segmentDistance;

        if (distanceSinceLastAperture >= apertureDistanceThreshold)
        {
            GenerateApertures(threadPoints.Count - 1);
            distanceSinceLastAperture = 0f;
        }
    }

    // 检查位置是否与金布区域重叠
    private bool IsPositionOverlappingGoldCloth(Vector3 position)
    {
        foreach (Vector3 goldClothPoint in goldClothRegions)
        {
            if (Vector3.Distance(position, goldClothPoint) < goldClothWidth)
            {
                return true;
            }
        }
        return false;
    }

    // 更新LineRenderer
    private void UpdateLineRenderer()
    {
        if (currentLineRenderer == null)
        {
            Debug.LogWarning("[LineRenderer] LineRenderer为空，无法更新");
            return;
        }

        if (threadPoints.Count == 0)
        {
            Debug.LogWarning("[LineRenderer] 金线点数为空，无法更新");
            return;
        }

        // 验证材质是否存在且有效
        if (currentLineRenderer.sharedMaterial == null)
        {
            Debug.LogError("[LineRenderer] 材质为空，无法更新金线显示");
            // 尝试重新应用用户挂载的材质
            Material targetMaterial = GetGoldThreadMaterial();
            if (targetMaterial != null)
            {
                Debug.Log("[LineRenderer] 尝试重新应用预制体材质");
                currentLineRenderer.sharedMaterial = targetMaterial;
                LogMaterialProperties(targetMaterial);
            }
            else
            {
                Debug.LogError("[LineRenderer] 无法从预制体获取材质，无法修复");
                return;
            }
        }
        
        // 使用专门的材质验证和修复方法
        if (!ValidateAndFixMaterial())
        {
            Debug.LogError("[LineRenderer] 材质验证和修复失败");
            return;
        }
        
        // 定期验证材质属性（每5秒一次）
        if (Time.time % 5.0f < Time.deltaTime)
        {
            Debug.Log("[LineRenderer] 定期材质检查");
            LogMaterialProperties(currentLineRenderer.sharedMaterial);
        }
        
        // 每次更新后强制刷新材质（前10次更新）
        if (threadPoints.Count > 0 && threadPoints.Count <= 10)
        {
            Debug.Log($"[LineRenderer] 强制材质刷新 (点数: {threadPoints.Count})");
            ForceMaterialRefresh();
        }

        // 验证金线宽度
        if (currentLineRenderer.startWidth <= 0.01f)
        {
            Debug.LogWarning("[LineRenderer] 金线宽度过小，重置为默认值");
            currentLineRenderer.startWidth = 0.08f;
            currentLineRenderer.endWidth = 0.08f;
        }

        // 更新位置
        currentLineRenderer.positionCount = threadPoints.Count;
        currentLineRenderer.SetPositions(threadPoints.ToArray());
        
        // 调试信息：每秒输出一次
        if (Time.time % 1.0f < Time.deltaTime)
        {
            Debug.Log($"[LineRenderer] 更新完成 - 点数: {threadPoints.Count}, 第一个点: {threadPoints[0]}, 最后一个点: {threadPoints[threadPoints.Count-1]}, 宽度: {currentLineRenderer.startWidth:F3}");
        }
    }

    // 更新金布可见性
    private void UpdateGoldClothVisibility()
    {
        if (goldClothTransform == null || threadPoints.Count < 2)
            return;

        // 确保金布可见
        if (!goldClothTransform.gameObject.activeSelf)
        {
            goldClothTransform.gameObject.SetActive(true);
        }

        // 控制金布生成的时间间隔，避免性能问题
        if (Time.time - lastGoldClothUpdate < goldClothGenerationInterval)
        {
            return;
        }

        lastGoldClothUpdate = Time.time;

        // 在金线周围生成金布区域
        // 只处理最新添加的线段，提高性能
        for (int i = Mathf.Max(0, threadPoints.Count - 2); i < threadPoints.Count - 1; i++)
        {
            Vector3 start = threadPoints[i];
            Vector3 end = threadPoints[i + 1];
            float segmentLength = Vector3.Distance(start, end);
            int segments = Mathf.CeilToInt(segmentLength / (goldClothWidth * 0.5f));
            
            for (int j = 0; j <= segments; j++)
            {
                float t = (float)j / segments;
                Vector3 point = Vector3.Lerp(start, end, t);
                
                // 检查该点是否已经在金布区域中
                bool alreadyExists = false;
                foreach (Vector3 existingPoint in goldClothRegions)
                {
                    if (Vector3.Distance(point, existingPoint) < goldClothWidth / 3)
                    {
                        alreadyExists = true;
                        break;
                    }
                }
                
                if (!alreadyExists)
                {
                    goldClothRegions.Add(point);
                }
            }
        }
    }

    // 检查位置是否在底布上
    private bool IsPositionOnBaseCloth(Vector3 position)
    {
        if (baseClothTransform == null)
        {
            return false;
        }

        // 简化检查：判断Y坐标是否接近底布平面
        float clothY = baseClothTransform.position.y;
        return Mathf.Abs(position.y - clothY) < 0.1f;
    }

    // 将屏幕坐标转换到底布平面
    private Vector3 ScreenToBaseClothPlane(Vector2 screenPosition)
    {
        Ray ray = Camera.main.ScreenPointToRay(screenPosition);

        Debug.Log($"[屏幕坐标转换] 输入屏幕位置: ({screenPosition.x:F1}, {screenPosition.y:F1})");

        // 方法1：尝试使用射线投射到底布的Collider
        if (baseClothTransform != null)
        {
            Collider baseClothCollider = baseClothTransform.GetComponent<Collider>();
            if (baseClothCollider != null)
            {
                if (Physics.Raycast(ray, out RaycastHit hit, Mathf.Infinity))
                {
                    // 检查击中的是否是底布或其子物体
                    if (hit.transform == baseClothTransform || hit.transform.IsChildOf(baseClothTransform))
                    {
                        Vector3 worldPos = hit.point;
                        Debug.Log($"[屏幕坐标转换] 射线投射到底布Collider成功，击中点: ({worldPos.x:F3}, {worldPos.y:F3}, {worldPos.z:F3})");
                        return worldPos;
                    }
                }
            }
        }

        // 方法2：使用几何平面投射（备用方法）
        if (baseClothTransform != null)
        {
            Plane baseClothPlane = new Plane(baseClothTransform.up, baseClothTransform.position);
            
            if (baseClothPlane.Raycast(ray, out float distance))
            {
                Vector3 worldPos = ray.GetPoint(distance);
                Debug.Log($"[屏幕坐标转换] 几何平面投射成功，距离: {distance:F3}, 世界坐标: ({worldPos.x:F3}, {worldPos.y:F3}, {worldPos.z:F3})");
                return worldPos;
            }
        }

        // 方法3：使用固定距离投射（最后的备用方法）
        if (Camera.main != null)
        {
            Vector3 worldPos = ray.GetPoint(5.0f); // 固定距离5单位
            Debug.LogWarning($"[屏幕坐标转换] 使用固定距离投射，距离: 5.0f, 世界坐标: ({worldPos.x:F3}, {worldPos.y:F3}, {worldPos.z:F3})");
            return worldPos;
        }

        // 最后的备用方案：返回线轴位置
        Debug.LogError($"[屏幕坐标转换] 所有投射方法都失败，返回线轴位置: ({spoolBasePosition.x:F3}, {spoolBasePosition.y:F3}, {spoolBasePosition.z:F3})");
        return spoolBasePosition;
    }

    // 生成光圈
    private void GenerateApertures(int threadPointIndex)
    {
        if (threadPointIndex < 1 || threadPointIndex >= threadPoints.Count)
        {
            return;
        }

        Vector3 point = threadPoints[threadPointIndex];
        Vector3 prevPoint = threadPoints[threadPointIndex - 1];
        Vector3 nextPoint = threadPoints.Count > threadPointIndex + 1 ? threadPoints[threadPointIndex + 1] : point;

        // 计算线段方向
        Vector3 direction = (nextPoint - prevPoint).normalized;
        Vector3 perpendicular = Vector3.Cross(direction, Vector3.up).normalized;

        // 在当前点两侧生成光圈
        Vector3 aperture1Pos = point + perpendicular * 0.1f;
        Vector3 aperture2Pos = point - perpendicular * 0.1f;

        Aperture aperture1 = CreateAperture(aperture1Pos, threadPointIndex);
        Aperture aperture2 = CreateAperture(aperture2Pos, threadPointIndex);

        activeApertures.Add(aperture1);
        activeApertures.Add(aperture2);

        currentThreadState = ThreadState.WaitingForApertureClick;
    }

    // 创建光圈
    private Aperture CreateAperture(Vector3 position, int threadPointIndex)
    {
        GameObject apertureObj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        apertureObj.transform.position = position;
        apertureObj.transform.localScale = Vector3.one * 0.05f;
        apertureObj.GetComponent<Renderer>().material.color = Color.yellow;

        Aperture aperture = new Aperture
        {
            gameObject = apertureObj,
            position = position,
            threadPointIndex = threadPointIndex,
            isClicked = false
        };

        return aperture;
    }

    // 检查光圈点击
    private void CheckApertureClick(Vector3 clickPosition)
    {
        foreach (Aperture aperture in activeApertures)
        {
            if (!aperture.isClicked && Vector3.Distance(clickPosition, aperture.position) < 0.1f)
            {
                aperture.isClicked = true;
                aperture.gameObject.GetComponent<Renderer>().material.color = Color.green;
                CheckAllAperturesClicked();
            }
        }
    }

    // 检查所有光圈是否都被点击
    private void CheckAllAperturesClicked()
    {
        bool allClicked = true;
        foreach (Aperture aperture in activeApertures)
        {
            if (!aperture.isClicked)
            {
                allClicked = false;
                break;
            }
        }

        if (allClicked)
        {
            // 所有光圈都被点击，金线固定
            currentThreadState = ThreadState.Rooted;
            ClearActiveApertures();
        }
    }

    // 清除活跃光圈
    private void ClearActiveApertures()
    {
        foreach (Aperture aperture in activeApertures)
        {
            Destroy(aperture.gameObject);
        }
        activeApertures.Clear();
    }

    // 检查金线移动是否被允许
    private bool IsThreadMovementAllowed(Vector3 newPosition)
    {
        // 金线在等待光圈点击时难以挪动
        Vector3 currentEnd = threadPoints[threadPoints.Count - 1];
        float distance = Vector3.Distance(currentEnd, newPosition);
        
        // 只有当移动距离足够大时才允许移动
        return distance > goldThreadBreakThreshold * 3f;
    }

    // 更新光源位置
    private void UpdateLightSourcePosition(Vector3 tiltData)
    {
        if (lightSourceTransform == null)
        {
            return;
        }

        float currentTime = Time.time;
        
        // 初始化检查
        if (isFirstUpdate)
        {
            isFirstUpdate = false;
            lastTargetPosition = lightSourceTransform.position;
            lastFilteredTilt = Vector3.zero;
            lastUpdateTime = currentTime;
            currentVelocity = Vector3.zero;
        }

        // 高级死区过滤，使用渐进式过滤
        Vector3 filteredTilt = ApplyAdvancedDeadZone(tiltData, lastFilteredTilt);
        
        // 如果没有有效倾斜，平滑返回到默认位置
        if (filteredTilt == Vector3.zero)
        {
            Vector3 defaultPosition = Vector3.up * lightSphereRadius;
            if (baseClothTransform != null)
            {
                defaultPosition += baseClothTransform.position;
            }
            
            if (useAdvancedSmoothing)
            {
                lightSourceTransform.position = SmoothDampAdvanced(
                    lightSourceTransform.position, 
                    defaultPosition, 
                    ref currentVelocity, 
                    1.0f / responsiveness, 
                    Time.deltaTime
                );
            }
            else
            {
                lightSourceTransform.position = Vector3.Lerp(
                    lightSourceTransform.position, 
                    defaultPosition, 
                    Time.deltaTime * lightSmoothSpeed
                );
            }
            
            lastFilteredTilt = filteredTilt;
            lastUpdateTime = currentTime;
            return;
        }

        // 计算目标位置
        Vector3 targetPosition = CalculateTargetPosition(filteredTilt);
        
        // 应用高级平滑算法
        if (useAdvancedSmoothing)
        {
            // 使用预测性平滑
            Vector3 predictedPosition = targetPosition;
            if (predictionFactor > 0 && lastTargetPosition != Vector3.zero)
            {
                Vector3 movementDirection = (targetPosition - lastTargetPosition).normalized;
                float movementMagnitude = Vector3.Distance(targetPosition, lastTargetPosition);
                predictedPosition = targetPosition + movementDirection * movementMagnitude * predictionFactor;
            }
            
            // 应用平滑阻尼
            lightSourceTransform.position = SmoothDampAdvanced(
                lightSourceTransform.position,
                predictedPosition,
                ref currentVelocity,
                1.0f / responsiveness,
                Time.deltaTime
            );
        }
        else
        {
            // 使用简单的线性插值
            lightSourceTransform.position = Vector3.Lerp(
                lightSourceTransform.position, 
                targetPosition, 
                Time.deltaTime * lightSmoothSpeed
            );
        }
        
        // 确保光源始终指向底布中心，提供最佳的照明效果
        if (baseClothTransform != null)
        {
            lightSourceTransform.LookAt(baseClothTransform.position);
        }
        
        // 更新状态变量
        lastTargetPosition = targetPosition;
        lastFilteredTilt = filteredTilt;
        lastUpdateTime = currentTime;
        
        // 调试输出（降低频率，每2秒输出一次）
        if (Time.frameCount % 120 == 0)
        {
            float horizontalAngle = filteredTilt.x * lightSensitivity;
            float verticalAngle = filteredTilt.y * lightSensitivity;
        }
    }
    
    // 高级死区过滤
    private Vector3 ApplyAdvancedDeadZone(Vector3 currentTilt, Vector3 lastFilteredTilt)
    {
        Vector3 filtered = currentTilt;
        
        // 分别对每个轴应用死区
        for (int i = 0; i < 3; i++)
        {
            float axisValue = currentTilt[i];
            float lastValue = lastFilteredTilt[i];
            
            // 如果当前值在死区内，设为0
            if (Mathf.Abs(axisValue) < deadZone)
            {
                filtered[i] = 0f;
            }
            else
            {
                // 应用渐进式过滤，减少突变
                float filteredValue = lastValue + (axisValue - lastValue) * 0.3f;
                filtered[i] = filteredValue;
            }
        }
        
        return filtered;
    }
    
    // 计算目标位置
    private Vector3 CalculateTargetPosition(Vector3 filteredTilt)
    {
        // 使用更直观的球面坐标映射
        float horizontalAngle = filteredTilt.x * lightSensitivity;  // 左右倾斜角度
        float verticalAngle = filteredTilt.y * lightSensitivity;    // 前后倾斜角度
        
        // 限制角度范围，确保光源在合理范围内移动
        horizontalAngle = Mathf.Clamp(horizontalAngle, -60f, 60f);   // 限制左右倾斜范围
        verticalAngle = Mathf.Clamp(verticalAngle, -45f, 45f);       // 限制前后倾斜范围
        
        // 转换为弧度
        float horizontalRad = horizontalAngle * Mathf.Deg2Rad;
        float verticalRad = verticalAngle * Mathf.Deg2Rad;
        
        // 计算球面上的位置
        Vector3 spherePosition;
        spherePosition.x = lightSphereRadius * Mathf.Sin(horizontalRad) * Mathf.Cos(verticalRad);
        spherePosition.y = lightSphereRadius * Mathf.Sin(verticalRad);
        spherePosition.z = lightSphereRadius * Mathf.Cos(horizontalRad) * Mathf.Cos(verticalRad);
        
        // 应用反向映射：手机倾斜方向与光源移动方向完全相反
        if (useInverseMapping)
        {
            spherePosition = -spherePosition;
        }
        
        // 确保光源位置相对于底布中心
        if (baseClothTransform != null)
        {
            spherePosition += baseClothTransform.position;
        }
        
        return spherePosition;
    }
    
    // 高级平滑阻尼（类似Unity的SmoothDamp但优化过）
    private Vector3 SmoothDampAdvanced(Vector3 current, Vector3 target, ref Vector3 velocity, float smoothTime, float deltaTime)
    {
        // 基于物理的平滑算法
        float omega = 2f / smoothTime;
        float x = omega * deltaTime;
        float exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
        
        Vector3 change = current - target;
        Vector3 originalTo = target;
        
        Vector3 temp = (velocity + omega * change) * deltaTime;
        velocity = (velocity - omega * temp) * exp;
        
        Vector3 output = target + (change + temp) * exp;
        
        // 防止 overshoot
        if (Vector3.Dot(originalTo - current, output - originalTo) > 0f)
        {
            output = originalTo;
            velocity = (output - originalTo) / deltaTime;
        }
        
        return output;
    }

    // 结束当前金线
    private void EndCurrentGoldThread()
    {
        Debug.Log($"[金线断开] ========== 开始断开金线 ==========");
        Debug.Log($"[金线断开] 当前状态: {currentThreadState}");
        Debug.Log($"[金线断开] 金线对象是否存在: {currentGoldThread != null}");
        Debug.Log($"[金线断开] LineRenderer是否存在: {currentLineRenderer != null}");
        Debug.Log($"[金线断开] threadPoints数量: {threadPoints.Count}");
        Debug.Log($"[金线断开] fixedThreadPoints数量: {fixedThreadPoints.Count}");
        
        if (currentGoldThread != null)
        {
            // 转换金布可见性
            Debug.Log("[金线断开] 开始转换金布可见性");
            ConvertGoldCloth();

            // 销毁当前金线对象
            Debug.Log($"[金线断开] 准备销毁金线对象: {currentGoldThread.name}");
            Destroy(currentGoldThread);
            currentGoldThread = null;
            currentLineRenderer = null;
            Debug.Log("[金线断开] 金线对象已销毁并置空");

            // 清除状态
            threadPoints.Clear();
            fixedThreadPoints.Clear();
            ClearActiveApertures();
            Debug.Log("[金线断开] 状态数据已清除");
        }
        else
        {
            Debug.Log("[金线断开] 当前没有金线对象需要销毁");
        }

        // 彻底重置所有状态
        ThreadState previousState = currentThreadState;
        currentThreadState = ThreadState.Idle;
        Debug.Log($"[金线断开] 状态从 {previousState} 重置为 {currentThreadState}");
        
        // 转换到全局闲置状态
        TransitionToGlobalState(GlobalInteractionState.Idle);
        
        // 重置长按检测状态
        Debug.Log("[金线断开] 重置长按检测状态");
        ResetLongPress();
        
        // 重置距离计数器
        distanceSinceLastAperture = 0f;
        
        // 重置触摸位置
        lastTouchPosition = Vector2.zero;
        
        LogOperationStatus("金线断开");
        
        Debug.Log($"[金线断开] 最终状态检查:");
        Debug.Log($"[金线断开] - currentThreadState: {currentThreadState}");
        Debug.Log($"[金线断开] - currentGoldThread: {currentGoldThread != null}");
        Debug.Log($"[金线断开] - currentLineRenderer: {currentLineRenderer != null}");
        Debug.Log($"[金线断开] - threadPoints数量: {threadPoints.Count}");
        Debug.Log($"[金线断开] ========== 金线断开处理完成 ==========");
        
        // 验证状态重置的一致性
        ValidateStateConsistency();
    }

    // 转换金布可见性
    private void ConvertGoldCloth()
    {
        if (goldClothTransform == null)
            return;

        // 使金布可见
        goldClothTransform.gameObject.SetActive(true);

        // 为金布添加网格渲染器和材质
        if (goldClothTransform.GetComponent<MeshRenderer>() == null)
        {
            MeshRenderer renderer = goldClothTransform.gameObject.AddComponent<MeshRenderer>();
            Material targetMaterial = GetGoldThreadMaterial();
            if (targetMaterial != null)
            {
                renderer.sharedMaterial = targetMaterial;
            }
        }

        // 为金布添加网格过滤器
        if (goldClothTransform.GetComponent<MeshFilter>() == null)
        {
            MeshFilter filter = goldClothTransform.gameObject.AddComponent<MeshFilter>();
            
            // 创建一个简单的平面网格作为金布
            Mesh mesh = new Mesh();
            Vector3[] vertices = new Vector3[4];
            vertices[0] = new Vector3(-1, 0, -1);
            vertices[1] = new Vector3(1, 0, -1);
            vertices[2] = new Vector3(-1, 0, 1);
            vertices[3] = new Vector3(1, 0, 1);
            
            int[] triangles = new int[6] { 0, 2, 1, 2, 3, 1 };
            Vector2[] uv = new Vector2[4] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
            
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uv;
            mesh.RecalculateNormals();
            
            filter.mesh = mesh;
        }

        // 更新金布的大小和位置，使其覆盖所有金布区域
        if (goldClothRegions.Count > 0)
        {
            Vector3 min = Vector3.one * float.MaxValue;
            Vector3 max = Vector3.one * float.MinValue;
            
            foreach (Vector3 point in goldClothRegions)
            {
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }
            
            Vector3 size = max - min;
            Vector3 center = (min + max) / 2;
            
            goldClothTransform.position = center;
            goldClothTransform.localScale = new Vector3(size.x, 0.01f, size.z);
        }
    }

    // 检查金线是否与金布区域重叠
    private bool IsThreadOverlappingGoldCloth(Vector3 start, Vector3 end)
    {
        // 检查线段是否穿过已有的金布区域
        if (goldClothRegions.Count == 0)
            return false;

        // 在start和end之间采样多个点进行检查
        int samples = 10; // 采样点数量，可以根据需要调整
        for (int i = 0; i <= samples; i++)
        {
            float t = (float)i / samples;
            Vector3 point = Vector3.Lerp(start, end, t);
            
            if (IsPositionOverlappingGoldCloth(point))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 获取光源Transform（用于测试）
    /// </summary>
    public Transform GetLightSourceTransform()
    {
        return lightSourceTransform;
    }
    
    /// <summary>
    /// 获取光源配置参数（用于测试）
    /// </summary>
    public void GetLightSourceConfig(out float radius, out float sensitivity, out float smoothSpeed, 
        out bool inverseMapping, out bool advancedSmoothing, out float responsiveness, 
        out float predictionFactor, out float deadZone)
    {
        radius = lightSphereRadius;
        sensitivity = lightSensitivity;
        smoothSpeed = lightSmoothSpeed;
        inverseMapping = useInverseMapping;
        advancedSmoothing = useAdvancedSmoothing;
        responsiveness = this.responsiveness;
        predictionFactor = this.predictionFactor;
        deadZone = this.deadZone;
    }

    // 更新调试信息显示
    private void UpdateDebugInfo()
    {
        // 简单直白的屏幕输出，不需要复杂的UI组件
    }

    // 简化的屏幕调试信息显示
    private void OnGUI()
    {
        if (!showDebugInfo) return;

        // 设置GUI样式
        GUIStyle style = new GUIStyle(GUI.skin.label);
        style.fontSize = 16;
        style.normal.textColor = Color.white;
        
        // 黑色背景 - 扩大区域以容纳金线端点信息和全局状态
        GUI.color = new Color(0, 0, 0, 0.7f);
        GUI.DrawTexture(new Rect(5, 5, 400, 370), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // 显示输入数据
        float y = 10;
        GUI.Label(new Rect(10, y, 390, 20), "=== 输入数据 ===", style);
        y += 25;

        // 触摸位置
        GUI.Label(new Rect(10, y, 390, 20), $"触摸位置: {lastTouchPosition}", style);
        y += 20;

        // 陀螺仪数据
        Vector3 tiltData = Vector3.zero;
        if (InputManager_Scene1.Instance != null)
        {
            tiltData = InputManager_Scene1.Instance.GetCurrentTiltData();
        }
        GUI.Label(new Rect(10, y, 390, 20), $"陀螺仪: X={tiltData.x:F3} Y={tiltData.y:F3} Z={tiltData.z:F3}", style);
        y += 20;

        // 系统状态
        GUI.Label(new Rect(10, y, 390, 20), $"金线状态: {currentThreadState}", style);
        y += 20;

        // 全局交互状态
        GUI.Label(new Rect(10, y, 390, 20), $"全局状态: {currentGlobalState} ({(int)currentGlobalState})", style);
        y += 20;

        GUI.Label(new Rect(10, y, 390, 20), $"金线点数: {threadPoints.Count}", style);
        y += 20;

        GUI.Label(new Rect(10, y, 390, 20), $"金布区域: {goldClothRegions.Count}", style);
        y += 20;

        GUI.Label(new Rect(10, y, 390, 20), $"长按状态: {(isPressing ? $"{currentPressTime:F2}s" : "未开始")}", style);
        y += 25;

        // 金线端点坐标
        GUI.Label(new Rect(10, y, 390, 20), "=== 金线端点坐标 ===", style);
        y += 20;

        if (currentGoldThread != null && threadPoints.Count >= 2)
        {
            // 调试日志：记录UI显示时的threadPoints状态
            Debug.Log($"[调试UI] 当前threadPoints数量: {threadPoints.Count}");
            
            // 线轴固定端点
            Vector3 startPoint = threadPoints[0];
            Debug.Log($"[调试UI] 端点1(线轴)从threadPoints[0]读取: ({startPoint.x:F3}, {startPoint.y:F3}, {startPoint.z:F3})");
            GUI.Label(new Rect(10, y, 390, 20), $"端点1(线轴): ({startPoint.x:F2}, {startPoint.y:F2}, {startPoint.z:F2})", style);
            y += 20;

            // 手指移动端点 - 在Dragging状态下明确使用threadPoints[1]
            Vector3 endPoint;
            if (currentThreadState == ThreadState.Dragging && threadPoints.Count >= 2)
            {
                endPoint = threadPoints[1]; // 在Dragging状态下，第二个端点始终是threadPoints[1]
                Debug.Log($"[调试UI] 端点2(手指)从threadPoints[1]读取(Dragging状态): ({endPoint.x:F3}, {endPoint.y:F3}, {endPoint.z:F3})");
            }
            else
            {
                endPoint = threadPoints[threadPoints.Count - 1]; // 其他状态使用最后一个点
                Debug.Log($"[调试UI] 端点2(手指)从threadPoints[{threadPoints.Count - 1}]读取(非Dragging状态): ({endPoint.x:F3}, {endPoint.y:F3}, {endPoint.z:F3})");
            }
            GUI.Label(new Rect(10, y, 390, 20), $"端点2(手指): ({endPoint.x:F2}, {endPoint.y:F2}, {endPoint.z:F2})", style);
            y += 20;

            // 线长度
            float length = Vector3.Distance(startPoint, endPoint);
            GUI.Label(new Rect(10, y, 390, 20), $"金线长度: {length:F3}", style);
            y += 20;

            // LineRenderer状态
            if (currentLineRenderer != null)
            {
                GUI.Label(new Rect(10, y, 390, 20), $"LineRenderer点数: {currentLineRenderer.positionCount}", style);
                y += 20;
                GUI.Label(new Rect(10, y, 390, 20), $"金线宽度: {currentLineRenderer.startWidth:F3}", style);
            }
        }
        else
        {
            GUI.Label(new Rect(10, y, 390, 20), "金线未创建或端点不足", style);
        }
        y += 25;

        // 组件状态
        GUI.Label(new Rect(10, y, 390, 20), "=== 组件状态 ===", style);
        y += 20;

        GUI.Label(new Rect(10, y, 390, 20), $"线轴: {(spoolTransform != null ? "✓" : "✗")}", style);
        y += 20;

        GUI.Label(new Rect(10, y, 390, 20), $"底布: {(baseClothTransform != null ? "✓" : "✗")}", style);
        y += 20;

        GUI.Label(new Rect(10, y, 390, 20), $"金布: {(goldClothTransform != null ? "✓" : "✗")}", style);
        y += 20;

        GUI.Label(new Rect(10, y, 390, 20), $"光源: {(lightSourceTransform != null ? "✓" : "✗")}", style);
        y += 20;

        GUI.Label(new Rect(10, y, 390, 20), $"输入系统: {(InputManager_Scene1.Instance != null ? "✓" : "✗")}", style);
    }

    // 处理主触摸移动 - 增强调试版本
    // 检查触摸位置是否点击了关键物体
    private string IdentifyTouchedObject(Vector2 screenPosition)
    {
        if (!enableTouchObjectDebug)
            return "调试已禁用";

        // 执行射线检测
        Ray ray = Camera.main.ScreenPointToRay(screenPosition);
        RaycastHit hit;
        
        if (Physics.Raycast(ray, out hit, Mathf.Infinity))
        {
            GameObject hitObject = hit.collider.gameObject;
            
            // 检查是否点击了关键挂载物体
            if (spoolTransform != null && hitObject == spoolTransform.gameObject)
                return $"线轴 ({spoolTransform.name})";
            
            if (baseClothTransform != null && hitObject == baseClothTransform.gameObject)
                return $"底布 ({baseClothTransform.name})";
            
            if (goldClothTransform != null && hitObject == goldClothTransform.gameObject)
                return $"金布 ({goldClothTransform.name})";
            
            if (lightSourceTransform != null && hitObject == lightSourceTransform.gameObject)
                return $"光源 ({lightSourceTransform.name})";
            
            // 检查是否点击了关键物体的子物体
            Transform hitTransform = hitObject.transform;
            if (spoolTransform != null && hitTransform.IsChildOf(spoolTransform))
                return $"线轴子物体 ({hitObject.name})";
            
            if (baseClothTransform != null && hitTransform.IsChildOf(baseClothTransform))
                return $"底布子物体 ({hitObject.name})";
            
            if (goldClothTransform != null && hitTransform.IsChildOf(goldClothTransform))
                return $"金布子物体 ({hitObject.name})";
            
            if (lightSourceTransform != null && hitTransform.IsChildOf(lightSourceTransform))
                return $"光源子物体 ({hitObject.name})";
            
            // 检查是否点击了金线
            if (currentGoldThread != null && hitObject == currentGoldThread)
                return $"当前金线 ({currentGoldThread.name})";
            
            // 检查是否点击了光圈
            foreach (var aperture in activeApertures)
            {
                if (aperture.gameObject != null && hitObject == aperture.gameObject)
                    return $"光圈 ({aperture.gameObject.name})";
            }
            
            // 其他物体
            return $"其他物体 ({hitObject.name})";
        }
        else
        {
            // 没有碰撞到任何物体，检查是否接近关键物体（用于UI或无碰撞体的物体）
            if (spoolTransform != null && IsTouchNearTransform(screenPosition, spoolTransform))
                return $"接近线轴 ({spoolTransform.name})";
            
            if (baseClothTransform != null && IsTouchNearTransform(screenPosition, baseClothTransform))
                return $"接近底布 ({baseClothTransform.name})";
            
            return "未点击到任何物体";
        }
    }
    
    // 检查触摸是否接近指定Transform（用于无碰撞体的物体）
    private bool IsTouchNearTransform(Vector2 screenPosition, Transform target)
    {
        if (target == null)
        {
            return false;
        }
        
        Vector3 targetScreenPosition = Camera.main.WorldToScreenPoint(target.position);
        float distance = Vector2.Distance(screenPosition, targetScreenPosition);
        return distance < 100f; // 100像素范围内
    }

    // 从预制体获取LineRenderer材质
    private Material GetGoldThreadMaterial()
    {
        if (goldThreadPrefab == null)
        {
            Debug.LogError("[材质获取] 金线预制体未挂载！");
            return null;
        }
        
        LineRenderer prefabLineRenderer = goldThreadPrefab.GetComponent<LineRenderer>();
        if (prefabLineRenderer == null)
        {
            Debug.LogError("[材质获取] 金线预制体上未找到LineRenderer组件！");
            return null;
        }
        
        Material prefabMaterial = prefabLineRenderer.sharedMaterial;
        if (prefabMaterial == null)
        {
            Debug.LogError("[材质获取] 金线预制体的LineRenderer未设置材质！");
            return null;
        }
        
        Debug.Log($"[材质获取] 成功获取预制体材质: {prefabMaterial.name}");
        return prefabMaterial;
    }

    // 验证材质设置
    private void ValidateMaterialSetup()
    {
        Debug.Log("[材质验证] ========== 开始材质设置验证 ==========");
        
        Material targetMaterial = GetGoldThreadMaterial();
        if (targetMaterial == null)
        {
            Debug.LogError("[材质验证] 严重错误：无法从预制体获取材质！");
            Debug.LogError("[材质验证] 请在Unity Inspector中为GoldThreadSystem组件的goldThreadPrefab参数指定预制体，并确保预制体包含带有材质的LineRenderer组件");
            return;
        }
        
        Debug.Log($"[材质验证] 材质已挂载: {targetMaterial.name}");
        Debug.Log($"[材质验证] 材质Shader: {targetMaterial.shader.name}");
        Debug.Log($"[材质验证] 材质颜色: {targetMaterial.color}");
        Debug.Log($"[材质验证] 渲染队列: {targetMaterial.renderQueue}");
        
        // 检查Shader支持
        if (!targetMaterial.shader.isSupported)
        {
            Debug.LogError($"[材质验证] Shader {targetMaterial.shader.name} 在当前设备上不支持！");
        }
        else
        {
            Debug.Log($"[材质验证] Shader {targetMaterial.shader.name} 支持良好");
        }
        
        // 检查关键属性
        bool hasColor = targetMaterial.HasProperty("_Color") || targetMaterial.HasProperty("_BaseColor");
        bool hasMainTex = targetMaterial.HasProperty("_MainTex") || targetMaterial.HasProperty("_BaseMap");
        
        Debug.Log($"[材质验证] 颜色属性存在: {hasColor}");
        Debug.Log($"[材质验证] 主纹理属性存在: {hasMainTex}");
        
        // 详细材质属性日志
        LogMaterialProperties(targetMaterial);
        
        Debug.Log("[材质验证] ========== 材质设置验证完成 ==========");
    }

    // 强制材质刷新机制
    // 测试材质修复效果的公共方法
    [ContextMenu("深度诊断金线材质问题")]
    public void DeepDiagnoseGoldThreadMaterial()
    {
        Debug.Log("========== 深度诊断金线材质问题 ==========");
        
        if (currentLineRenderer == null)
        {
            Debug.LogError("LineRenderer为空，无法诊断");
            return;
        }
        
        // 获取预制体材质
        Material prefabMaterial = GetGoldThreadMaterial();
        if (prefabMaterial == null)
        {
            Debug.LogError("无法从预制体获取材质");
            return;
        }
        
        Debug.Log($"预制体材质: {prefabMaterial.name}");
        Debug.Log($"预制体材质Shader: {prefabMaterial.shader.name}");
        Debug.Log($"预制体材质颜色: {prefabMaterial.color}");
        Debug.Log($"预制体材质渲染队列: {prefabMaterial.renderQueue}");
        
        // 获取当前LineRenderer材质
        Material currentMaterial = currentLineRenderer.sharedMaterial;
        Debug.Log($"当前LineRenderer材质: {currentMaterial?.name}");
        Debug.Log($"当前LineRenderer材质Shader: {currentMaterial?.shader.name}");
        Debug.Log($"当前LineRenderer材质颜色: {currentMaterial?.color}");
        Debug.Log($"当前LineRenderer材质渲染队列: {currentMaterial?.renderQueue}");
        
        // 检查是否是Unity默认材质
        if (currentMaterial != null && (currentMaterial.name.Contains("Default") || currentMaterial.name.Contains("Default-Material")))
        {
            Debug.LogError("检测到Unity默认材质！这就是灰蓝色的原因");
            
            // 尝试强制设置材质
            Debug.Log("尝试强制设置预制体材质...");
            currentLineRenderer.sharedMaterial = prefabMaterial;
            
            // 禁用可能影响材质显示的功能
            currentLineRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            currentLineRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            currentLineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            currentLineRenderer.receiveShadows = false;
            
            // 验证设置结果
            Material verifyMaterial = currentLineRenderer.sharedMaterial;
            if (verifyMaterial == prefabMaterial)
            {
                Debug.Log("材质设置成功！");
            }
            else
            {
                Debug.LogError("材质设置失败，Unity仍然使用默认材质");
                Debug.LogError("不会创建材质副本，让Unity处理材质实例化");
            }
        }
        else
        {
            Debug.Log("当前材质不是Unity默认材质");
            
            // 检查材质颜色是否为灰蓝色
            if (currentMaterial != null)
            {
                Color color = currentMaterial.color;
                Debug.Log($"当前材质颜色: R={color.r:F2}, G={color.g:F2}, B={color.b:F2}, A={color.a:F2}");
                
                if (color.r < 0.5f && color.g < 0.5f && color.b > 0.5f)
                {
                    Debug.LogWarning("材质颜色偏蓝灰色，可能是Unity默认颜色");
                }
            }
        }
        
        // 检查LineRenderer的其他设置
        Debug.Log($"LineRenderer宽度: {currentLineRenderer.startWidth} - {currentLineRenderer.endWidth}");
        Debug.Log($"LineRenderer颜色: {currentLineRenderer.startColor} - {currentLineRenderer.endColor}");
        Debug.Log($"LineRenderer使用世界空间: {currentLineRenderer.useWorldSpace}");
        
        Debug.Log("========== 诊断完成 ==========");
    }
    
    // 测试材质修复效果的公共方法
    [ContextMenu("强制修复金线材质显示")]
    public void ForceFixGoldThreadMaterial()
    {
        Debug.Log("========== 强制修复金线材质显示 ==========");
        
        if (currentLineRenderer == null)
        {
            Debug.LogError("LineRenderer为空，无法修复");
            return;
        }
        
        // 获取预制体材质
        Material targetMaterial = GetGoldThreadMaterial();
        if (targetMaterial == null)
        {
            Debug.LogError("无法从预制体获取材质");
            return;
        }
        
        // 禁用所有可能影响材质显示的功能
        currentLineRenderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        currentLineRenderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        currentLineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        currentLineRenderer.receiveShadows = false;
        currentLineRenderer.allowOcclusionWhenDynamic = false;
        
        // 确保使用预制体材质
        if (targetMaterial != null)
        {
            // 检查当前是否使用了Unity默认材质
            Material currentMaterial = currentLineRenderer.sharedMaterial;
            if (currentMaterial != null && (currentMaterial.name.Contains("Default") || currentMaterial.name.Contains("Default-Material")))
            {
                Debug.LogError("[强制修复] 检测到Unity默认材质！这就是灰蓝色的原因");
                Debug.LogError("[强制修复] Unity强制使用了默认材质，用户材质被覆盖");
            }
            
            // 强制设置材质 - 使用多种方式确保生效
            currentLineRenderer.sharedMaterial = targetMaterial;
            currentLineRenderer.sharedMaterial = targetMaterial; // 重复设置确保生效
            
            // 强制设置颜色 - 确保与材质颜色一致
            Color materialColor = targetMaterial.color;
            currentLineRenderer.startColor = materialColor;
            currentLineRenderer.endColor = materialColor;
            
            // 验证设置是否成功
            Material verifyMaterialSet = currentLineRenderer.sharedMaterial;
            if (verifyMaterialSet == targetMaterial)
            {
                Debug.Log($"[强制修复] 材质设置成功: {targetMaterial.name}");
            }
            else
            {
                Debug.LogError("[强制修复] 材质设置失败，Unity可能强制创建了实例");
                Debug.LogError($"[强制修复] 当前材质: {verifyMaterialSet?.name}, 目标材质: {targetMaterial.name}");
            }
            
            // 额外的材质设置 - 确保在下一帧也生效
            StartCoroutine(ForceMaterialNextFrame(targetMaterial, materialColor));
            
            Debug.Log($"[强制修复] 使用预制体材质: {targetMaterial.name}");
            Debug.Log($"[强制修复] 材质颜色: {materialColor}");
            Debug.Log($"[强制修复] LineRenderer颜色设置: start={materialColor}, end={materialColor}");
        }
        else
        {
            Debug.LogWarning("[强制修复] 无法获取材质，使用默认黄色");
            currentLineRenderer.startColor = Color.yellow;
            currentLineRenderer.endColor = Color.yellow;
        }
        
        // 确保宽度足够明显
        currentLineRenderer.startWidth = Mathf.Max(currentLineRenderer.startWidth, 0.2f);
        currentLineRenderer.endWidth = Mathf.Max(currentLineRenderer.endWidth, 0.2f);
        
        // 验证修复结果
        Material verifyMaterial = currentLineRenderer.sharedMaterial;
        Debug.Log($"修复后材质: {verifyMaterial?.name}");
        Debug.Log($"修复后材质颜色: {verifyMaterial?.color}");
        Debug.Log($"修复后LineRenderer颜色: {currentLineRenderer.startColor}");
        
        // 强制刷新LineRenderer
        currentLineRenderer.enabled = false;
        currentLineRenderer.enabled = true;
        
        Debug.Log("========== 修复完成 ==========");
    }
    
    // 测试材质修复效果的公共方法
    [ContextMenu("测试材质修复")]
    public void TestMaterialFix()
    {
        if (currentLineRenderer == null)
        {
            Debug.LogError("[材质测试] LineRenderer为空，无法测试");
            return;
        }
        
        // 从预制体获取目标材质
        Material targetMaterial = GetGoldThreadMaterial();
        if (targetMaterial == null)
        {
            Debug.LogError("[材质测试] 无法从预制体获取材质，无法测试");
            return;
        }
        
        Debug.Log("[材质测试] ========== 开始材质修复测试 ==========");
        
        // 记录测试前状态
        Material beforeTest = currentLineRenderer.sharedMaterial;
        Debug.Log($"[材质测试] 测试前材质: {beforeTest?.name} (ID: {beforeTest?.GetInstanceID()})");
        Debug.Log($"[材质测试] 用户材质: {targetMaterial.name} (ID: {targetMaterial.GetInstanceID()})");
        
        // 执行材质验证和修复
        bool fixResult = ValidateAndFixMaterial();
        
        // 记录测试后状态
        Material afterTest = currentLineRenderer.sharedMaterial;
        Debug.Log($"[材质测试] 测试后材质: {afterTest?.name} (ID: {afterTest?.GetInstanceID()})");
        Debug.Log($"[材质测试] 修复结果: {(fixResult ? "成功" : "失败")}");
        
        // 判断材质匹配状态
        string matchStatus;
        if (afterTest == targetMaterial)
        {
            matchStatus = "完全匹配";
        }
        else if (afterTest?.name?.Contains("_Copy") == true)
        {
            matchStatus = "使用副本";
        }
        else
        {
            matchStatus = "不匹配";
        }
        Debug.Log($"[材质测试] 材质匹配: {matchStatus}");
        
        // 如果是副本，显示副本信息
        if (afterTest != targetMaterial && afterTest?.name?.Contains("_Copy") == true)
        {
            Debug.Log($"[材质测试] 副本材质: {afterTest.name}");
            Debug.Log($"[材质测试] 副本Shader: {afterTest.shader.name}");
            Debug.Log($"[材质测试] 副本颜色: {afterTest.color}");
        }
        
        Debug.Log("[材质测试] ========== 材质修复测试完成 ==========");
    }

    // 测试预制体设置的公共方法
    [ContextMenu("测试预制体设置")]
    public void TestPrefabSetup()
    {
        Debug.Log("[预制体测试] ========== 开始预制体设置测试 ==========");
        
        // 检查预制体是否挂载
        if (goldThreadPrefab == null)
        {
            Debug.LogError("[预制体测试] 错误：goldThreadPrefab未挂载！");
            Debug.LogError("[预制体测试] 请在Unity Inspector中为GoldThreadSystem组件的goldThreadPrefab参数指定预制体");
            return;
        }
        Debug.Log($"[预制体测试] 预制体已挂载: {goldThreadPrefab.name}");
        
        // 检查预制体是否有LineRenderer组件
        LineRenderer prefabLineRenderer = goldThreadPrefab.GetComponent<LineRenderer>();
        if (prefabLineRenderer == null)
        {
            Debug.LogError("[预制体测试] 错误：预制体上未找到LineRenderer组件！");
            return;
        }
        Debug.Log("[预制体测试] 预制体包含LineRenderer组件");
        
        // 检查LineRenderer是否有材质
        Material prefabMaterial = prefabLineRenderer.sharedMaterial;
        if (prefabMaterial == null)
        {
            Debug.LogError("[预制体测试] 错误：预制体的LineRenderer未设置材质！");
            return;
        }
        Debug.Log($"[预制体测试] 预制体材质已设置: {prefabMaterial.name}");
        
        // 输出材质详细信息
        Debug.Log($"[预制体测试] 材质Shader: {prefabMaterial.shader.name}");
        Debug.Log($"[预制体测试] 材质颜色: {prefabMaterial.color}");
        Debug.Log($"[预制体测试] 材质渲染队列: {prefabMaterial.renderQueue}");
        Debug.Log($"[预制体测试] 材质实例ID: {prefabMaterial.GetInstanceID()}");
        
        // 检查Shader支持
        if (!prefabMaterial.shader.isSupported)
        {
            Debug.LogError($"[预制体测试] 警告：材质Shader {prefabMaterial.shader.name} 在当前设备上不支持！");
        }
        else
        {
            Debug.Log($"[预制体测试] Shader {prefabMaterial.shader.name} 支持良好");
        }
        
        // 测试实例化预制体
        Debug.Log("[预制体测试] 测试实例化预制体...");
        GameObject testInstance = Instantiate(goldThreadPrefab, transform);
        testInstance.name = "TestPrefabInstance";
        
        LineRenderer testLineRenderer = testInstance.GetComponent<LineRenderer>();
        if (testLineRenderer == null)
        {
            Debug.LogError("[预制体测试] 错误：实例化后的预制体未找到LineRenderer组件！");
            Destroy(testInstance);
            return;
        }
        
        Material testMaterial = testLineRenderer.sharedMaterial;
        Debug.Log($"[预制体测试] 实例化后材质: {testMaterial?.name} (ID: {testMaterial?.GetInstanceID()})");
        
        // 检查材质是否保持一致
        if (testMaterial == prefabMaterial)
        {
            Debug.Log("[预制体测试] 成功：实例化后材质与预制体材质完全一致");
        }
        else
        {
            Debug.LogWarning("[预制体测试] 警告：实例化后材质与预制体材质不一致");
            Debug.LogWarning($"[预制体测试] 预制体材质: {prefabMaterial.name} (ID: {prefabMaterial.GetInstanceID()})");
            Debug.LogWarning($"[预制体测试] 实例化材质: {testMaterial?.name} (ID: {testMaterial?.GetInstanceID()})");
        }
        
        // 清理测试对象
        Destroy(testInstance);
        Debug.Log("[预制体测试] 测试实例已清理");
        
        Debug.Log("[预制体测试] ========== 预制体设置测试完成 ==========");
    }

    // 验证并修复材质引用的专用方法
    private bool ValidateAndFixMaterial()
    {
        if (currentLineRenderer == null)
        {
            return false;
        }
        
        // 从预制体获取目标材质
        Material targetMaterial = GetGoldThreadMaterial();
        if (targetMaterial == null)
        {
            return false;
        }
        
        Material currentMat = currentLineRenderer.sharedMaterial;
        
        // 如果材质正确，返回成功
        if (currentMat == targetMaterial)
        {
            return true;
        }
        
        // 材质不正确，需要修复
        Debug.LogWarning("[材质验证] 检测到材质不一致，开始修复");
        Debug.LogWarning($"[材质验证] 当前材质: {currentMat?.name} (ID: {currentMat?.GetInstanceID()})");
        Debug.LogWarning($"[材质验证] 目标材质: {targetMaterial.name} (ID: {targetMaterial.GetInstanceID()})");
        
        // 尝试直接修复
        currentLineRenderer.sharedMaterial = targetMaterial;
        
        // 验证修复结果
        Material verifyMat = currentLineRenderer.sharedMaterial;
        if (verifyMat == targetMaterial)
        {
            Debug.Log("[材质验证] 直接修复成功");
            return true;
        }
        
        // 直接修复失败，不会创建材质副本
        Debug.LogWarning("[材质验证] 直接修复失败，但不会创建材质副本");
        Debug.LogWarning("[材质验证] 让Unity处理材质实例化");
        
        return false;
    }

    private void ForceMaterialRefresh()
    {
        if (currentLineRenderer == null)
        {
            Debug.LogWarning("[材质刷新] LineRenderer为空，无法刷新");
            return;
        }
        
        // 从预制体获取目标材质
        Material targetMaterial = GetGoldThreadMaterial();
        if (targetMaterial == null)
        {
            Debug.LogWarning("[材质刷新] 无法从预制体获取材质，无法刷新");
            return;
        }
        
        Debug.Log("[材质刷新] ========== 开始强制材质刷新 ==========");
        
        // 记录刷新前的状态
        Material beforeRefresh = currentLineRenderer.sharedMaterial;
        Debug.Log($"[材质刷新] 刷新前材质: {beforeRefresh?.name} (ID: {beforeRefresh?.GetInstanceID()})");
        Debug.Log($"[材质刷新] 目标材质: {targetMaterial.name} (ID: {targetMaterial.GetInstanceID()})");
        
        // 强制材质设置 - 使用多种方法确保生效
        for (int i = 0; i < 5; i++)
        {
            currentLineRenderer.sharedMaterial = targetMaterial;
            // 短暂延迟确保设置生效
            System.Threading.Thread.Sleep(1);
        }
        
        // 强制重新启用LineRenderer组件
        currentLineRenderer.enabled = false;
        currentLineRenderer.enabled = true;
        
        // 再次强制设置材质
        currentLineRenderer.sharedMaterial = targetMaterial;
        
        // 清除可能的材质缓存并重新设置
        if (currentLineRenderer.sharedMaterial != targetMaterial)
        {
            Debug.LogError("[材质刷新] 材质设置失败，尝试强制清除");
            currentLineRenderer.sharedMaterial = null;
            currentLineRenderer.sharedMaterial = targetMaterial;
        }
        
        // 如果sharedMaterial仍然失败，不会创建材质副本
        if (currentLineRenderer.sharedMaterial != targetMaterial)
        {
            Debug.LogWarning("[材质刷新] sharedMaterial持续失败，但不会创建材质副本");
            Debug.LogWarning("[材质刷新] 让Unity处理材质实例化");
        }
        
        // 重新设置位置以确保更新
        Vector3[] currentPoints = new Vector3[currentLineRenderer.positionCount];
        currentLineRenderer.GetPositions(currentPoints);
        currentLineRenderer.SetPositions(currentPoints);
        
        // 验证刷新结果
        Material afterRefresh = currentLineRenderer.sharedMaterial;
        Debug.Log($"[材质刷新] 刷新后材质: {afterRefresh?.name} (ID: {afterRefresh?.GetInstanceID()})");
        Debug.Log($"[材质刷新] 刷新后Shader: {afterRefresh?.shader?.name}");
        Debug.Log($"[材质刷新] 刷新后颜色: {afterRefresh?.color}");
        Debug.Log($"[材质刷新] 材质设置成功: {afterRefresh == targetMaterial || (afterRefresh?.name?.Contains("_Copy") == true)}");
        Debug.Log($"[材质刷新] LineRenderer启用状态: {currentLineRenderer.enabled}");
        Debug.Log("[材质刷新] ========== 材质刷新完成 ==========");
    }

    // 记录材质详细属性用于调试
    private void LogMaterialProperties(Material material)
    {
        if (material == null)
        {
            Debug.LogError("[材质调试] 材质为空，无法记录属性");
            return;
        }
        
        Debug.Log($"[材质调试] ========== 材质属性详细分析 ==========");
        Debug.Log($"[材质调试] 材质名称: {material.name}");
        Debug.Log($"[材质调试] Shader名称: {material.shader.name}");
        Debug.Log($"[材质调试] 渲染队列: {material.renderQueue}");
        Debug.Log($"[材质调试] 是否启用实例化: {material.enableInstancing}");
        Debug.Log($"[材质调试] 全局照明: {material.globalIlluminationFlags}");
        
        // 检查常用属性
        string[] commonProperties = { "_Color", "_BaseColor", "_MainTex", "_BaseMap", "_EmissionColor", "_Metallic", "_Smoothness", "_NormalMap" };
        
        foreach (string prop in commonProperties)
        {
            if (material.HasProperty(prop))
            {
                switch (prop)
                {
                    case "_Color":
                    case "_BaseColor":
                        Color color = material.GetColor(prop);
                        Debug.Log($"[材质调试] {prop}: {color} (Alpha: {color.a:F2})");
                        break;
                    case "_MainTex":
                    case "_BaseMap":
                        Texture texture = material.GetTexture(prop);
                        Debug.Log($"[材质调试] {prop}: {texture?.name ?? "null"}");
                        break;
                    case "_EmissionColor":
                        Color emissionColor = material.GetColor(prop);
                        Debug.Log($"[材质调试] {prop}: {emissionColor}");
                        break;
                    case "_Metallic":
                        float metallic = material.GetFloat(prop);
                        Debug.Log($"[材质调试] {prop}: {metallic:F2}");
                        break;
                    case "_Smoothness":
                        float smoothness = material.GetFloat(prop);
                        Debug.Log($"[材质调试] {prop}: {smoothness:F2}");
                        break;
                    case "_NormalMap":
                        Texture normalMap = material.GetTexture(prop);
                        Debug.Log($"[材质调试] {prop}: {normalMap?.name ?? "null"}");
                        break;
                }
            }
            else
            {
                Debug.Log($"[材质调试] {prop}: (属性不存在)");
            }
        }
        
        // 检查LineRenderer特定的兼容性
        Debug.Log($"[材质调试] ========== LineRenderer兼容性检查 ==========");
        Debug.Log($"[材质调试] Shader是否支持LineRenderer: {material.shader.isSupported}");
        Debug.Log($"[材质调试] Shader名称: {material.shader.name}");
        
        // 检查Shader的渲染队列类型
        Debug.Log($"[材质调试] 材质渲染队列: {material.renderQueue}");
        
        // 检查Shader是否适合LineRenderer（通过检查常用属性）
        bool hasColorProperty = material.HasProperty("_Color") || material.HasProperty("_BaseColor");
        bool hasWidthProperty = material.HasProperty("_Width") || material.HasProperty("_LineWidth");
        Debug.Log($"[材质调试] 是否有颜色属性: {hasColorProperty}");
        Debug.Log($"[材质调试] 是否有宽度属性: {hasWidthProperty}");
        
        // 检查材质关键字
        string[] keywords = material.shaderKeywords;
        Debug.Log($"[材质调试] 材质关键字数量: {keywords.Length}");
        foreach (string keyword in keywords)
        {
            Debug.Log($"[材质调试] 关键字: {keyword}");
        }
        
        Debug.Log($"[材质调试] ========== 材质属性分析完成 ==========");
    }

    private void DiagnoseMaterialIssue()
    {
        Debug.Log("[材质诊断] ========== 开始材质问题诊断 ==========");
        
        if (currentLineRenderer == null)
        {
            Debug.LogError("[材质诊断] LineRenderer为空");
            return;
        }
        
        // 从预制体获取目标材质
        Material targetMaterial = GetGoldThreadMaterial();
        if (targetMaterial == null)
        {
            Debug.LogError("[材质诊断] 无法从预制体获取材质");
            return;
        }
        
        // 关键测试：检查LineRenderer的材质行为（只使用sharedMaterial）
        Debug.Log("[材质诊断] ========== LineRenderer材质行为测试 ==========");
        Debug.Log($"[材质诊断] 用户材质实例ID: {targetMaterial.GetInstanceID()}");
        Debug.Log($"[材质诊断] 用户材质名称: {targetMaterial.name}");
        
        // 只测试sharedMaterial访问，避免触发material实例化
        Material sharedMat = currentLineRenderer.sharedMaterial;
        Debug.Log($"[材质诊断] sharedMaterial实例ID: {sharedMat?.GetInstanceID()}");
        Debug.Log($"[材质诊断] sharedMaterial名称: {sharedMat?.name}");
        
        // 检查是否是Unity的默认材质
        if (sharedMat != null && (sharedMat.name.Contains("Default") || sharedMat.name.Contains("Default-Material")))
        {
            Debug.LogError("[材质诊断] 检测到Unity默认材质！这就是灰蓝色的原因");
            Debug.LogError("[材质诊断] Unity强制使用了默认材质，用户材质被覆盖");
        }
        
        // 检查材质颜色（只检查sharedMaterial）
        if (sharedMat != null)
        {
            Color materialColor = sharedMat.HasProperty("_Color") ? sharedMat.GetColor("_Color") : 
                                 sharedMat.HasProperty("_BaseColor") ? sharedMat.GetColor("_BaseColor") : Color.gray;
            
            Debug.Log($"[材质诊断] 当前材质颜色: {materialColor}");
            
            if (materialColor.r < 0.3f && materialColor.g < 0.3f && materialColor.b < 0.5f)
            {
                Debug.LogError("[材质诊断] 材质颜色偏暗蓝灰色，确认为Unity默认材质");
            }
        }
        
        // 关键发现：LineRenderer的特殊行为
        Debug.LogWarning("[材质诊断] ========== 关键发现 ==========");
        Debug.LogWarning("[材质诊断] LineRenderer的material属性会自动创建实例！");
        Debug.LogWarning("[材质诊断] 解决方案：永远不要访问.material属性，只使用.sharedMaterial");
        Debug.LogWarning("[材质诊断] 这就是为什么材质实例ID不同的原因");
        
        // 实施正确的解决方案
        Debug.LogWarning("[材质诊断] ========== 实施解决方案 ==========");
        
        // 方案1：强制使用sharedMaterial并且永不访问material属性
        currentLineRenderer.sharedMaterial = targetMaterial;
        Debug.Log("[材质诊断] 已设置sharedMaterial，避免访问material属性");
        
        // 验证sharedMaterial设置
        Material verifyShared = currentLineRenderer.sharedMaterial;
        Debug.Log($"[材质诊断] 验证sharedMaterial ID: {verifyShared?.GetInstanceID()}");
        Debug.Log($"[材质诊断] sharedMaterial设置成功: {verifyShared == targetMaterial}");
        
        // 如果sharedMaterial设置失败，不会创建材质副本
        if (verifyShared != targetMaterial)
        {
            Debug.LogWarning("[材质诊断] sharedMaterial设置失败，但不会创建材质副本");
            Debug.LogWarning("[材质诊断] 让Unity处理材质实例化");
        }
        
        Debug.Log("[材质诊断] ========== 材质问题诊断完成 ==========");
    }

    private void HandlePrimaryTouchMoved(Vector2 touchPosition)
    {
        lastTouchPosition = touchPosition;
        Vector3 worldPosition = ScreenToBaseClothPlane(touchPosition);
        
        // 识别触摸的物体
        string touchedObject = IdentifyTouchedObject(touchPosition);

        // 首先验证当前状态是否允许触摸移动
        if (currentThreadState == ThreadState.Idle)
        {
            // 空闲状态下不应该有金线在移动，如果有则是异常情况
            if (currentGoldThread != null)
            {
                Debug.LogWarning("[触摸移动] 异常：空闲状态下检测到金线对象，强制清理");
                EndCurrentGoldThread();
            }
            // 空闲状态下的触摸移动，不做特殊处理
            return;
        }

        switch (currentThreadState)
        {
            case ThreadState.Dragging:
                // 更新金线跟随触摸位置
                UpdateGoldThreadPosition(worldPosition);
                LogOperationStatus("牵引金线");
                break;

            case ThreadState.Rooted:
                // 跟随手指在底布上绘制曲线
                DrawGoldThreadCurve(worldPosition);
                LogOperationStatus("牵引金线");
                break;

            case ThreadState.WaitingForApertureClick:
                // 等待光圈点击时，金线难以挪动
                if (IsThreadMovementAllowed(worldPosition))
                {
                    UpdateGoldThreadPosition(worldPosition);
                    LogOperationStatus("牵引金线");
                }
                break;
        }
    }

    // 输出当前操作状态
    private void LogOperationStatus(string status)
    {
        Debug.Log($"[用户当前操作状态]: {status}");
    }

    // 处理主触摸开始
    private void HandlePrimaryTouchStarted(Vector2 touchPosition)
    {
        lastTouchPosition = touchPosition;
        Vector3 worldPosition = ScreenToBaseClothPlane(touchPosition);
        
        // 识别触摸的物体
        string touchedObject = IdentifyTouchedObject(touchPosition);

        bool isOnSpool = IsTouchOnSpoolTransform(touchPosition);
        Debug.Log($"[触摸开始] 屏幕位置: {touchPosition}, 当前状态: {currentThreadState}, 是否在线轴上: {isOnSpool}");
        
        // 首先检查状态一致性
        if (currentThreadState == ThreadState.Idle && currentGoldThread != null)
        {
            Debug.LogWarning("[触摸开始] 异常：空闲状态下检测到金线对象，强制清理");
            EndCurrentGoldThread();
        }
        
        // 根据当前状态处理触摸开始
        switch (currentThreadState)
        {
            case ThreadState.Idle:
                // 在空闲状态下，只有触摸在线轴上时才开始长按检测
                if (isOnSpool)
                {
                    LogOperationStatus("点击线轴");
                    Debug.Log("[触摸开始] 开始长按检测");
                    StartLongPress(worldPosition);
                }
                else
                {
                    LogOperationStatus("未操作");
                    Debug.Log("[触摸开始] 未点击线轴，不开始长按检测");
                }
                break;
                
            default:
                // 非空闲状态下，不应该开始新的长按检测
                Debug.Log($"[触摸开始] 当前状态为 {currentThreadState}，忽略新的触摸开始");
                break;
        }
    }

    // 处理主触摸结束
    private void HandlePrimaryTouchEnded(Vector2 touchPosition)
    {
        // 添加调用栈和时间戳调试
        Debug.Log($"[触摸结束] ========== HandlePrimaryTouchEnded被调用 ==========");
        Debug.Log($"[触摸结束] 调用时间: {System.DateTime.Now:HH:mm:ss.fff}");
        Debug.Log($"[触摸结束] 输入触摸位置: ({touchPosition.x:F1}, {touchPosition.y:F1})");
        
        // 打印调用堆栈以追踪调用来源
        System.Diagnostics.StackTrace stackTrace = new System.Diagnostics.StackTrace();
        Debug.Log($"[触摸结束] 调用堆栈: {stackTrace.ToString()}");
        
        lastTouchPosition = touchPosition;
        Vector3 worldPosition = ScreenToBaseClothPlane(touchPosition);
        
        Debug.Log($"[触摸结束] ========== 触摸结束事件触发 ==========");
        Debug.Log($"[触摸结束] 当前状态: {currentThreadState}");
        Debug.Log($"[触摸结束] 触摸位置: {touchPosition}");
        Debug.Log($"[触摸结束] 世界坐标: ({worldPosition.x:F3}, {worldPosition.y:F3}, {worldPosition.z:F3})");
        Debug.Log($"[触摸结束] 是否存在金线: {currentGoldThread != null}");
        Debug.Log($"[触摸结束] threadPoints数量: {threadPoints.Count}");
        
        // 处理触摸结束逻辑
        switch (currentThreadState)
        {
            case ThreadState.Dragging:
                Debug.Log("[触摸结束] 检测到Dragging状态，手指离开界面，金线应该断开");
                LogOperationStatus("金线断开");
                EndCurrentGoldThread();
                break;
                
            case ThreadState.Rooted:
                Debug.Log("[触摸结束] 检测到Rooted状态，手指离开界面，金线应该断开");
                LogOperationStatus("金线断开");
                EndCurrentGoldThread();
                break;
                
            case ThreadState.WaitingForApertureClick:
                Debug.Log("[触摸结束] 检测到WaitingForApertureClick状态，手指离开界面，金线应该断开");
                LogOperationStatus("金线断开");
                EndCurrentGoldThread();
                break;
                
            default:
                Debug.Log($"[触摸结束] {currentThreadState}状态，只重置长按检测");
                // 其他状态下只重置长按检测
                ResetLongPress();
                break;
        }
        
        Debug.Log($"[触摸结束] 处理完成，最终状态: {currentThreadState}");
        Debug.Log($"[触摸结束] 是否还存在金线: {currentGoldThread != null}");
        Debug.Log($"[触摸结束] ========== 触摸结束处理完成 ==========");
    }

    // 处理次触摸点击 - 增强调试版本
    private void HandleSecondaryTapPerformed(Vector2 tapPosition)
    {
        Vector3 worldPosition = ScreenToBaseClothPlane(tapPosition);

        // 检查是否点击了光圈
        CheckApertureClick(worldPosition);
    }

    // 处理设备倾斜
    private void HandleDeviceTiltChanged(Vector3 tiltData)
    {
        // 更新光源位置
        UpdateLightSourcePosition(tiltData);
    }

    // 强制在下一帧设置材质的协程
    private System.Collections.IEnumerator ForceMaterialNextFrame(Material targetMaterial, Color materialColor)
    {
        yield return null; // 等待下一帧
        
        if (currentLineRenderer != null && targetMaterial != null)
        {
            // 检查当前是否使用了Unity默认材质
            Material currentMaterial = currentLineRenderer.sharedMaterial;
            if (currentMaterial != null && (currentMaterial.name.Contains("Default") || currentMaterial.name.Contains("Default-Material")))
            {
                Debug.LogError("[下一帧修复] 检测到Unity默认材质！这就是灰蓝色的原因");
                Debug.LogError("[下一帧修复] Unity强制使用了默认材质，用户材质被覆盖");
            }
            
            // 再次强制设置材质和颜色
            currentLineRenderer.sharedMaterial = targetMaterial;
            currentLineRenderer.startColor = materialColor;
            currentLineRenderer.endColor = materialColor;
            
            // 验证设置是否成功
            Material verifyMaterialNext = currentLineRenderer.sharedMaterial;
            if (verifyMaterialNext == targetMaterial)
            {
                Debug.Log($"[下一帧修复] 材质设置成功: {targetMaterial.name}");
            }
            else
            {
                Debug.LogError("[下一帧修复] 材质设置失败，Unity可能强制创建了实例");
                Debug.LogError($"[下一帧修复] 当前材质: {verifyMaterialNext?.name}, 目标材质: {targetMaterial.name}");
                
                // 如果设置失败，尝试在下一帧再次设置
                StartCoroutine(ForceMaterialNextFrame(targetMaterial, materialColor));
            }
            
            Debug.Log($"[下一帧修复] 材质: {targetMaterial.name}, 颜色: {materialColor}");
        }
    }

}
