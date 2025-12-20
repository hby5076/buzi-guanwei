using UnityEngine;
using System;
using UnityEngine.InputSystem;

public class InputTester_Scene1 : MonoBehaviour
{
    private InputManager_Scene1 inputManager;
    
    // 用于在屏幕上显示的状态信息
    private string displayMessage = "等待 InputManager 初始化...";
    private Vector3 lastTilt = Vector3.zero;
    private Vector2 lastPrimaryPos = Vector2.zero;
    private float lastPinchDelta = 0f;

    void Start()
    {
        // 获取单例
        inputManager = InputManager_Scene1.Instance;
        if (inputManager == null)
        {
            displayMessage = "错误：场景中未找到 InputManager_Scene1 实例！";
            Debug.LogError(displayMessage);
            return;
        }

        // 默认初始化为 Pingjin 模式
        inputManager.SetMode("Pingjin");
        displayMessage = "测试启动：当前模式 - Pingjin";

        // ----------------------------------------------------
        // 订阅事件 (与新版 InputManager 保持一致)
        // ----------------------------------------------------
        
        // Pingjin (平金) 模式事件
        inputManager.OnPrimaryTouchContactStarted += HandlePrimaryStarted;
        inputManager.OnPrimaryTouchContactCanceled += HandlePrimaryCanceled;
        inputManager.OnSecondaryTapPerformed += HandleSecondaryTap;
        inputManager.OnDeviceTilt += HandleDeviceTilt;

        // Duiling (堆绫) 模式事件
        inputManager.OnDuilingTapPerformed += HandleDuilingTap;
        inputManager.OnDragPerformed += HandleDragPerformed;
        inputManager.OnPinchDeltaChanged += HandlePinchDelta; // 现在接收 float

        // Panjin (盘金) 模式事件
        inputManager.OnTraceContactStarted += HandleTraceStarted;
        inputManager.OnTraceContactCanceled += HandleTraceCanceled;
        inputManager.OnNeedlePositionMoved += HandleNeedlePositionMoved;

        Debug.Log("[Test] 输入系统测试脚本已就绪。");
    }

    void OnDestroy()
    {
        if (inputManager != null)
        {
            // 取消订阅
            inputManager.OnPrimaryTouchContactStarted -= HandlePrimaryStarted;
            inputManager.OnPrimaryTouchContactCanceled -= HandlePrimaryCanceled;
            inputManager.OnSecondaryTapPerformed -= HandleSecondaryTap;
            inputManager.OnDeviceTilt -= HandleDeviceTilt;
            inputManager.OnDuilingTapPerformed -= HandleDuilingTap;
            inputManager.OnDragPerformed -= HandleDragPerformed;
            inputManager.OnPinchDeltaChanged -= HandlePinchDelta;
            inputManager.OnTraceContactStarted -= HandleTraceStarted;
            inputManager.OnTraceContactCanceled -= HandleTraceCanceled;
            inputManager.OnNeedlePositionMoved -= HandleNeedlePositionMoved;
        }
    }

    // =========================================================
    // 事件处理回调
    // =========================================================

    private void HandlePrimaryStarted() => UpdateDisplay("主触点按下");
    private void HandlePrimaryCanceled() => UpdateDisplay("主触点抬起");
    
    private void HandleSecondaryTap() => UpdateDisplay("检测到双指 Tap！");

    private void HandleDeviceTilt(Vector3 tilt)
    {
        lastTilt = tilt;
    }

    private void HandleDuilingTap() => UpdateDisplay("堆绫模式点击");

    private void HandleDragPerformed(Vector2 pos)
    {
        lastPrimaryPos = pos;
        // 拖拽高频更新，不建议每次都写 UpdateDisplay 干扰日志
    }

    private void HandlePinchDelta(float delta)
    {
        lastPinchDelta = delta;
        UpdateDisplay($"捏合变化: {delta:F2}");
    }

    private void HandleNeedlePositionMoved(Vector2 pos)
    {
        lastPrimaryPos = pos;
    }

    private void HandleTraceStarted() => UpdateDisplay("盘金描摹开始");
    private void HandleTraceCanceled() => UpdateDisplay("盘金描摹结束");

    // =========================================================
    // UI 显示
    // =========================================================

    private void UpdateDisplay(string msg)
    {
        displayMessage = $"{DateTime.Now:HH:mm:ss} - {msg}";
        Debug.Log($"[InputTest] {msg}");
    }

    void OnGUI()
    {
        // 简单的调试界面
        GUILayout.BeginArea(new Rect(20, 20, Screen.width - 40, Screen.height - 40));
        
        GUI.skin.label.fontSize = 25;
        GUILayout.Label("<color=yellow>--- Input System 最终真机测试 ---</color>");
        GUILayout.Label($"状态: {displayMessage}");
        GUILayout.Space(20);

        // 数据面板
        GUI.skin.label.fontSize = 20;
        GUILayout.Label($"当前模式: {GetCurrentModeName()}");
        GUILayout.Label($"主触点位置: {inputManager.GetPrimaryTouchPosition():F0}");
        GUILayout.Label($"次触点位置: {inputManager.GetSecondaryTouchPosition():F0}");
        GUILayout.Space(10);
        
        GUILayout.Label($"<color=cyan>陀螺仪 (Tilt): {lastTilt:F2}</color>");
        GUILayout.Label("传感器状态: " + (UnityEngine.InputSystem.AttitudeSensor.current?.enabled == true ? "激活" : "未激活"));
        GUILayout.Label($"<color=lime>捏合 (Pinch Delta): {lastPinchDelta:F2}</color>");
        
        GUILayout.FlexibleSpace();

        // 模式切换按钮
        GUILayout.Label("切换 Action Map:");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("平金 (Pingjin)", GUILayout.Height(80))) inputManager.SetMode("Pingjin");
        if (GUILayout.Button("堆绫 (Duiling)", GUILayout.Height(80))) inputManager.SetMode("Duiling");
        if (GUILayout.Button("盘金 (Panjin)", GUILayout.Height(80))) inputManager.SetMode("Panjin");
        GUILayout.EndHorizontal();

        GUILayout.EndArea();
    }

    private string GetCurrentModeName()
    {
        if (inputManager == null || inputManager.inputActions == null) return "Unknown";
        if (inputManager.inputActions.Pingjin.enabled) return "Pingjin (平金)";
        if (inputManager.inputActions.Duiling.enabled) return "Duiling (堆绫)";
        if (inputManager.inputActions.Panjin.enabled) return "Panjin (盘金)";
        return "None";
    }
}