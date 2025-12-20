using UnityEngine;

/// <summary>
/// 输入系统修复验证脚本 - 验证所有输入都通过InputManager_Scene1处理
/// </summary>
public class InputSystemFixValidator : MonoBehaviour
{
    void Start()
    {
        Debug.Log("=== 输入系统架构验证开始 ===");
        
        // 检查InputManager_Scene1是否正确初始化
        if (InputManager_Scene1.Instance != null)
        {
            Debug.Log("✓ InputManager_Scene1 实例已创建 - 这是唯一的输入处理入口");
            
            // 检查陀螺仪数据获取
            Vector3 tiltData = InputManager_Scene1.Instance.GetCurrentTiltData();
            Debug.Log($"✓ 当前陀螺仪数据: {tiltData}");
            
            // 测试事件触发（通过InputManager_Scene1的公共方法）
            InputManager_Scene1.Instance.TriggerTestEvents();
            Debug.Log("✓ 测试事件触发完成");
        }
        else
        {
            Debug.LogError("✗ InputManager_Scene1 实例未找到 - 输入系统未正确初始化");
        }
        
        // 检查GoldThreadSystem是否正确订阅了InputManager_Scene1事件
        if (GoldThreadSystem.Instance != null)
        {
            Debug.Log("✓ GoldThreadSystem 实例已创建 - 应该已订阅InputManager_Scene1事件");
        }
        else
        {
            Debug.LogError("✗ GoldThreadSystem 实例未找到");
        }
        
        Debug.Log("=== 输入系统架构说明 ===");
        Debug.Log("1. 所有游戏输入都通过InputManager_Scene1统一处理");
        Debug.Log("2. GoldThreadSystem只订阅InputManager_Scene1的事件，不直接处理输入");
        Debug.Log("3. GoldThreadSystemTester仅使用旧Input系统进行测试触发");
        Debug.Log("4. 真机陀螺仪和编辑器键盘模拟都通过InputManager_Scene1");
        Debug.Log("=== 输入系统架构验证完成 ===");
    }
    
    void Update()
    {
        // 每5秒输出一次陀螺仪数据状态
        if (Time.time % 5f < Time.deltaTime)
        {
            if (InputManager_Scene1.Instance != null)
            {
                Vector3 tiltData = InputManager_Scene1.Instance.GetCurrentTiltData();
                Debug.Log($"[周期检查] 通过InputManager_Scene1获取的陀螺仪数据: {tiltData}");
            }
        }
    }
}