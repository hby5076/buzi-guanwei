using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class GoldThreadSystem : MonoBehaviour
{
    // 单例实例
    public static GoldThreadSystem Instance { get; private set; }

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
    public Material goldThreadMaterial;       // 金线材质
    public float goldThreadWidth = 0.02f;     // 金线宽度
    public float goldClothWidth = 0.1f;       // 金布宽度（金线周围的范围）
    public float apertureDistanceThreshold = 0.5f; // 光圈生成的距离阈值
    public float goldThreadBreakThreshold = 0.2f;  // 金线断开的距离阈值

    // 光源设置
    [Header("光源设置")]
    public float lightSphereRadius = 5.0f;    // 光源移动的球面半径
    public float lightSensitivity = 0.1f;     // 光源对陀螺仪输入的灵敏度

    // 内部状态
    private enum ThreadState
    {
        Idle,
        Dragging,
        Rooted,
        WaitingForApertureClick,
        Disconnected
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
        if (InputManager_Scene1.Instance != null)
        {
            InputManager_Scene1.Instance.OnPrimaryTouchMoved += HandlePrimaryTouchMoved;
            InputManager_Scene1.Instance.OnSecondaryTapPerformed += HandleSecondaryTapPerformed;
            InputManager_Scene1.Instance.OnDeviceTiltChanged += HandleDeviceTiltChanged;
        }
    }

    private void OnDisable()
    {
        if (InputManager_Scene1.Instance != null)
        {
            InputManager_Scene1.Instance.OnPrimaryTouchMoved -= HandlePrimaryTouchMoved;
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
    }

    private void Update()
    {
        // 处理长按检测
        HandleLongPress();
    }

    private void InitializeComponents()
    {
        // 确保所有必要的组件都已挂载
        if (spoolTransform == null)
        {
            Debug.LogError("[GoldThreadSystem] 线轴挂载点未设置");
        }
        else
        {
            Debug.Log("[GoldThreadSystem] 线轴挂载点已设置: " + spoolTransform.name);
        }

        if (baseClothTransform == null)
        {
            Debug.LogError("[GoldThreadSystem] 底布挂载点未设置");
        }
        else
        {
            Debug.Log("[GoldThreadSystem] 底布挂载点已设置: " + baseClothTransform.name);
        }

        if (goldClothTransform == null)
        {
            Debug.LogError("[GoldThreadSystem] 金布挂载点未设置");
        }
        else
        {
            Debug.Log("[GoldThreadSystem] 金布挂载点已设置: " + goldClothTransform.name);
        }

        if (lightSourceTransform == null)
        {
            Debug.LogError("[GoldThreadSystem] 光源挂载点未设置");
        }
        else
        {
            Debug.Log("[GoldThreadSystem] 光源挂载点已设置: " + lightSourceTransform.name);
        }

        // 验证线轴底部固定点
        if (spoolBottomPoint != null)
        {
            Debug.Log("[GoldThreadSystem] 线轴底部固定点已设置: " + spoolBottomPoint.name);
        }
        else
        {
            Debug.Log("[GoldThreadSystem] 将使用线轴默认底部位置");
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

    // 处理主触摸移动
    private void HandlePrimaryTouchMoved(Vector2 touchPosition)
    {
        lastTouchPosition = touchPosition;
        Vector3 worldPosition = ScreenToBaseClothPlane(touchPosition);

        switch (currentThreadState)
        {
            case ThreadState.Idle:
                // 检查是否从线轴开始拖动
                if (IsTouchNearSpool(touchPosition))
                {
                    StartNewGoldThread(worldPosition);
                    // 开始长按检测
                    StartLongPress(worldPosition);
                }
                break;

            case ThreadState.Dragging:
                // 更新金线跟随触摸位置
                UpdateGoldThreadPosition(worldPosition);
                
                // 如果移动距离过大，重置长按检测
                if (Vector3.Distance(worldPosition, pressStartPosition) > goldThreadWidth * 2)
                {
                    ResetLongPress();
                    StartLongPress(worldPosition);
                }
                break;

            case ThreadState.Rooted:
                // 跟随手指在底布上绘制曲线
                DrawGoldThreadCurve(worldPosition);
                break;

            case ThreadState.WaitingForApertureClick:
                // 等待光圈点击时，金线难以挪动
                if (IsThreadMovementAllowed(worldPosition))
                {
                    UpdateGoldThreadPosition(worldPosition);
                }
                break;
        }
    }

    // 金线扎根
    private void RootGoldThread(Vector3 position)
    {
        if (currentGoldThread == null || currentLineRenderer == null)
            return;

        // 确保位置在底布上
        if (!IsPositionOnBaseCloth(position))
            return;

        // 金线扎根，开始绘制曲线
        currentThreadState = ThreadState.Rooted;
        
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

    // 处理次要触摸点击（用于点击光圈）
    private void HandleSecondaryTapPerformed(Vector2 tapPosition)
    {
        if (currentThreadState == ThreadState.WaitingForApertureClick)
        {
            Vector3 worldPosition = ScreenToBaseClothPlane(tapPosition);
            CheckApertureClick(worldPosition);
        }
    }

    // 处理设备倾斜（用于控制光源）
    private void HandleDeviceTiltChanged(Vector3 tiltData)
    {
        UpdateLightSourcePosition(tiltData);
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
                // 检查是否在底布上长按
                if ((currentThreadState == ThreadState.Dragging || currentThreadState == ThreadState.Rooted) && IsPositionOnBaseCloth(pressStartPosition))
                {
                    // 在底布上长按，金线扎根
                    RootGoldThread(pressStartPosition);
                    Debug.Log("[GoldThreadSystem] 长按触发金线扎根");
                }
                
                // 重置长按状态
                ResetLongPress();
            }
        }
    }

    // 重置长按状态
    private void ResetLongPress()
    {
        isPressing = false;
        currentPressTime = 0f;
        pressStartPosition = Vector3.zero;
    }

    // 开始长按检测
    private void StartLongPress(Vector3 position)
    {
        isPressing = true;
        currentPressTime = 0f;
        pressStartPosition = position;
    }

    // 检查触摸是否在线轴附近
    private bool IsTouchNearSpool(Vector2 touchPosition)
    {
        if (spoolTransform == null)
            return false;

        Vector3 spoolScreenPosition = Camera.main.WorldToScreenPoint(spoolTransform.position);
        float distance = Vector2.Distance(touchPosition, spoolScreenPosition);
        return distance < 50f; // 50像素范围内
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
            Debug.LogError("[GoldThreadSystem] 无法开始新金线：线轴挂载点未设置");
            return;
        }

        // 创建新的金线
        currentGoldThread = new GameObject("GoldThread");
        currentLineRenderer = currentGoldThread.AddComponent<LineRenderer>();
        
        // 设置金线材质和宽度
        if (goldThreadMaterial != null)
        {
            currentLineRenderer.material = goldThreadMaterial;
        }
        else
        {
            Debug.LogWarning("[GoldThreadSystem] 金线材质未设置，使用默认材质");
        }
        
        currentLineRenderer.startWidth = goldThreadWidth;
        currentLineRenderer.endWidth = goldThreadWidth;
        currentLineRenderer.positionCount = 2;
        currentLineRenderer.numCornerVertices = 5; // 使曲线更平滑
        currentLineRenderer.numCapVertices = 5;
        currentLineRenderer.loop = false;

        // 初始化金线点
        threadPoints.Clear();
        fixedThreadPoints.Clear();
        threadPoints.Add(spoolBasePosition);
        threadPoints.Add(initialPosition);
        UpdateLineRenderer();

        currentThreadState = ThreadState.Dragging;
        distanceSinceLastAperture = 0f;
        Debug.Log("[GoldThreadSystem] 开始新的金线绘制");
    }

    // 更新金线位置
    private void UpdateGoldThreadPosition(Vector3 newPosition)
    {
        if (currentGoldThread == null || currentLineRenderer == null)
            return;

        // 更新金线终点
        threadPoints[threadPoints.Count - 1] = newPosition;
        UpdateLineRenderer();

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
            
            Debug.Log("[GoldThreadSystem] 金线拖动到底布上，切换到Rooted状态");
        }
    }

    // 在底布上绘制金线曲线
    private void DrawGoldThreadCurve(Vector3 newPosition)
    {
        if (currentGoldThread == null || currentLineRenderer == null)
            return;

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
            return;

        currentLineRenderer.positionCount = threadPoints.Count;
        currentLineRenderer.SetPositions(threadPoints.ToArray());
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
            return;

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
            return false;

        // 简化检查：判断Y坐标是否接近底布平面
        float clothY = baseClothTransform.position.y;
        return Mathf.Abs(position.y - clothY) < 0.1f;
    }

    // 将屏幕坐标转换到底布平面
    private Vector3 ScreenToBaseClothPlane(Vector2 screenPosition)
    {
        Ray ray = Camera.main.ScreenPointToRay(screenPosition);
        Plane baseClothPlane = new Plane(baseClothTransform.up, baseClothTransform.position);

        if (baseClothPlane.Raycast(ray, out float distance))
        {
            return ray.GetPoint(distance);
        }

        // 默认返回线轴位置
        return spoolBasePosition;
    }

    // 生成光圈
    private void GenerateApertures(int threadPointIndex)
    {
        if (threadPointIndex < 1 || threadPointIndex >= threadPoints.Count)
            return;

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
        Debug.Log("[GoldThreadSystem] 生成光圈，等待点击确认");
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
            Debug.Log("[GoldThreadSystem] 所有光圈都被点击，金线已固定，可以继续绘制");
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
            return;

        // 死区过滤，避免微小抖动导致光源移动
        float deadZone = 0.05f;
        if (Mathf.Abs(tiltData.x) < deadZone) tiltData.x = 0;
        if (Mathf.Abs(tiltData.y) < deadZone) tiltData.y = 0;

        // 将陀螺仪数据转换为球面上的位置
        float theta = tiltData.x * lightSensitivity;
        float phi = tiltData.y * lightSensitivity;

        // 限制角度范围，避免光源移动过于极端
        theta = Mathf.Clamp(theta, -Mathf.PI / 2, Mathf.PI / 2);
        phi = Mathf.Clamp(phi, -Mathf.PI / 2, Mathf.PI / 2);

        // 球面坐标转笛卡尔坐标
        float x = lightSphereRadius * Mathf.Sin(theta) * Mathf.Cos(phi);
        float y = lightSphereRadius * Mathf.Sin(theta) * Mathf.Sin(phi);
        float z = lightSphereRadius * Mathf.Cos(theta);

        // 反转光源移动方向，与手机倾斜方向相反
        Vector3 newPosition = new Vector3(-x, -y, -z);
        
        // 确保光源位置相对于底布中心
        if (baseClothTransform != null)
        {
            newPosition += baseClothTransform.position;
        }
        
        // 调整平滑过渡速度，使其更加自然
        float smoothSpeed = 3.0f;
        lightSourceTransform.position = Vector3.Lerp(lightSourceTransform.position, newPosition, Time.deltaTime * smoothSpeed);
        
        // 确保光源始终指向底布中心，提供更好的照明效果
        if (baseClothTransform != null)
        {
            lightSourceTransform.LookAt(baseClothTransform.position);
        }
    }

    // 结束当前金线
    private void EndCurrentGoldThread()
    {
        if (currentGoldThread != null)
        {
            // 转换金布可见性
            ConvertGoldCloth();

            // 销毁当前金线对象
            Destroy(currentGoldThread);
            currentGoldThread = null;
            currentLineRenderer = null;

            // 清除状态
            threadPoints.Clear();
            fixedThreadPoints.Clear();
            ClearActiveApertures();
        }

        currentThreadState = ThreadState.Disconnected;
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
            if (goldThreadMaterial != null)
            {
                renderer.material = goldThreadMaterial;
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

    
}
