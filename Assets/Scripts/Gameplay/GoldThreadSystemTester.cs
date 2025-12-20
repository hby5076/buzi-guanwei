using UnityEngine;

/// <summary>
/// 金线系统测试脚本 - 用于验证输入系统和金线功能（移动端专用）
/// </summary>
public class GoldThreadSystemTester : MonoBehaviour
{
    [Header("测试设置")]
    public bool enableTestLogs = true;
    
    private GoldThreadSystem goldThreadSystem;
    private InputManager_Scene1 inputManager;
    
    void Start()
    {
        // 获取系统引用
        goldThreadSystem = GoldThreadSystem.Instance;
        inputManager = InputManager_Scene1.Instance;
        
        if (enableTestLogs)
        {
            LogTestInfo();
        }
    }
    
    void Update()
    {
        // 定期输出状态（每5秒一次）
        if (enableTestLogs && Time.time % 5f < Time.deltaTime)
        {
            LogSystemStatus();
        }
        
        // 陀螺仪测试 - 每10秒输出一次陀螺仪状态
        if (enableTestLogs && Time.time % 10f < Time.deltaTime)
        {
            TestGyroscopeStatus();
        }
    }
    
    private void LogTestInfo()
    {
        Debug.Log("=== 金线系统测试开始（移动端） ===");
        Debug.Log($"GoldThreadSystem: {(goldThreadSystem != null ? "✓ 已找到" : "✗ 未找到")}");
        Debug.Log($"InputManager_Scene1: {(inputManager != null ? "✓ 已找到" : "✗ 未找到")}");
        Debug.Log("说明：所有游戏输入都通过InputManager_Scene1系统处理");
        Debug.Log("移动端测试：请使用触摸和陀螺仪进行操作");
        Debug.Log("========================");
    }
    
    private void LogSystemStatus()
    {
        if (goldThreadSystem == null || inputManager == null)
        {
            Debug.LogWarning("[测试] 系统组件未完全初始化");
            return;
        }
        
        Debug.Log("[状态检查] 金线系统运行正常");
    }
    
    /// <summary>
    /// 测试陀螺仪状态
    /// </summary>
    private void TestGyroscopeStatus()
    {
        Debug.Log("=== 陀螺仪状态检查 ===");
        
        // 检查硬件支持
        bool hardwareSupported = SystemInfo.supportsGyroscope;
        Debug.Log($"硬件支持陀螺仪: {(hardwareSupported ? "✓" : "✗")}");
        
        // 检查Input System陀螺仪
        var gyro = UnityEngine.InputSystem.Gyroscope.current;
        bool gyroAvailable = gyro != null;
        bool gyroEnabled = gyroAvailable && gyro.enabled;
        
        Debug.Log($"Input System陀螺仪: {(gyroAvailable ? "✓ 可用" : "✗ 不可用")}");
        if (gyroAvailable)
        {
            Debug.Log($"陀螺仪启用状态: {(gyroEnabled ? "✓ 已启用" : "✗ 未启用")}");
            
            if (gyroEnabled)
            {
                try
                {
                    Vector3 reading = gyro.angularVelocity.ReadValue();
                    Debug.Log($"当前陀螺仪读数: {reading}");
                    Debug.Log($"读数状态: {(reading != Vector3.zero ? "✓ 有数据" : "○ 静止/零")}");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"读取陀螺仪数据失败: {e.Message}");
                }
            }
        }
        
        // 检查InputManager状态
        if (inputManager != null)
        {
            Debug.Log("InputManager_Scene1: ✓ 运行中");
        }
        else
        {
            Debug.LogError("InputManager_Scene1: ✗ 未找到");
        }
        
        Debug.Log("===================");
    }
    
    /// <summary>
    /// 测试光源系统
    /// </summary>
    private void TestLightSourceSystem()
    {
        Debug.Log("=== 光源系统检查 ===");
        
        if (goldThreadSystem == null)
        {
            Debug.LogError("GoldThreadSystem未找到，无法测试光源");
            return;
        }
        
        // 检查光源Transform
        Transform lightTransform = goldThreadSystem.GetLightSourceTransform();
        if (lightTransform != null)
        {
            Debug.Log($"光源Transform: ✓ {lightTransform.name}");
            Debug.Log($"当前位置: {lightTransform.position}");
            Debug.Log($"当前旋转: {lightTransform.rotation.eulerAngles}");
        }
        else
        {
            Debug.LogError("光源Transform: ✗ 未设置");
        }
        
        // 检查光源配置
        Debug.Log("=== 光源配置参数 ===");
        goldThreadSystem.GetLightSourceConfig(
            out float radius, out float sensitivity, out float smoothSpeed,
            out bool inverseMapping, out bool advancedSmoothing, out float responsiveness,
            out float predictionFactor, out float deadZone
        );
        
        Debug.Log($"球面半径: {radius:F2}");
        Debug.Log($"灵敏度: {sensitivity:F2}");
        Debug.Log($"平滑速度: {smoothSpeed:F2}");
        Debug.Log($"反向映射: {(inverseMapping ? "启用" : "禁用")}");
        Debug.Log($"高级平滑: {(advancedSmoothing ? "启用" : "禁用")}");
        Debug.Log($"响应速度: {responsiveness:F2}");
        Debug.Log($"预测因子: {predictionFactor:F2}");
        Debug.Log($"死区阈值: {deadZone:F4}");
        
        Debug.Log("===================");
    }
    
    /// <summary>
    /// 在Inspector中显示当前系统状态
    /// </summary>
    void OnGUI()
    {
        if (!enableTestLogs) return;
        
        GUILayout.BeginArea(new Rect(10, 300, 300, 150));
        GUILayout.Label("=== 金线系统测试面板（移动端） ===");
        GUILayout.Label($"GoldThreadSystem: {(goldThreadSystem != null ? "✓" : "✗")}");
        GUILayout.Label($"InputManager: {(inputManager != null ? "✓" : "✗")}");
        GUILayout.Label("使用触摸和陀螺仪进行测试");
        
        if (GUILayout.Button("重新检查系统"))
        {
            goldThreadSystem = GoldThreadSystem.Instance;
            inputManager = InputManager_Scene1.Instance;
            LogTestInfo();
        }
        
        if (GUILayout.Button("测试光源系统"))
        {
            TestLightSourceSystem();
        }
        
        if (GUILayout.Button("测试陀螺仪状态"))
        {
            TestGyroscopeStatus();
        }
        
        GUILayout.EndArea();
    }
}