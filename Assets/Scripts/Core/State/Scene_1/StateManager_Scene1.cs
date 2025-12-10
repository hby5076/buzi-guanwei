using UnityEngine;
using System;

public class StateManager_Scene1 : MonoBehaviour
{
    // 单例模式，方便全局访问
    public static StateManager_Scene1 Instance { get; private set; }

    // 游戏状态枚举（必须和InputManager.SwitchInputMap中的判断一致）
    public enum AppState
    {
        Pingjin_Guide,      // 平金-引导铺设
        Pingjin_Inspection, // 平金-反光质检
        Pingjin_Repair,     // 平金-精修补齐
        Duiling_Paste,      // 堆绫-粘贴定位
        Duiling_Stretch,    // 堆绫-拉伸固定
        Panjin_Draw         // 盘金-描摹点睛
    }

    // 当前状态（私有字段+公有属性，便于触发状态改变事件）
    private AppState _currentState;
    public AppState CurrentState
    {
        get => _currentState;
        private set
        {
            if (_currentState != value)
            {
                var oldState = _currentState;
                _currentState = value;
                OnStateChanged?.Invoke(oldState, value);
            }
        }
    }

    // 状态改变事件（其他系统可以订阅此事件做出响应）
    public event Action<AppState, AppState> OnStateChanged;

    void Awake()
    {
        // 初始化单例
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    void Start()
    {
        // 游戏启动时，设置为第一个状态
        SetState(AppState.Pingjin_Guide);
    }

    /// <summary>
    /// 外部调用此方法来切换状态（例如：UI按钮、步骤完成时）
    /// </summary>
    /// <param name="newState">要切换到的目标状态</param>
    public void SetState(AppState newState)
    {
        if (CurrentState == newState) return;

        Debug.Log($"状态切换: {CurrentState} -> {newState}");

        // 1. 通知InputManager切换输入映射
        if (InputManager_Scene1.Instance != null)
        {
            InputManager_Scene1.Instance.SwitchInputMap(newState);
        }
        else
        {
            Debug.LogWarning("InputManager实例未找到，请确保它已存在于场景中。");
        }

        // 2. 更新当前状态（将自动触发OnStateChanged事件）
        CurrentState = newState;

        // 3. 你可以在这里添加其他全局的状态进入逻辑
        // 例如：播放音效、显示UI提示等
        HandleStateEntryLogic(newState);
    }

    /// <summary>
    /// 处理特定状态的进入逻辑
    /// </summary>
    private void HandleStateEntryLogic(AppState state)
    {
        switch (state)
        {
            case AppState.Pingjin_Guide:
                Debug.Log("开始平金引导铺设阶段");
                // 例如：显示指引动画
                break;
            case AppState.Pingjin_Inspection:
                Debug.Log("开始反光质检阶段，请倾斜设备检查");
                // 例如：显示“请倾斜手机”的提示
                break;
            case AppState.Duiling_Paste:
                Debug.Log("开始堆绫粘贴阶段");
                // 例如：初始化布料碎片
                break;
                // ... 其他状态的处理
        }
    }

    /// <summary>
    /// 辅助方法：快速进入下一个逻辑状态（用于线性流程）
    /// </summary>
    public void ProceedToNextState()
    {
        AppState nextState = CurrentState switch
        {
            AppState.Pingjin_Guide => AppState.Pingjin_Inspection,
            AppState.Pingjin_Inspection => AppState.Pingjin_Repair,
            AppState.Pingjin_Repair => AppState.Duiling_Paste,
            AppState.Duiling_Paste => AppState.Duiling_Stretch,
            AppState.Duiling_Stretch => AppState.Panjin_Draw,
            AppState.Panjin_Draw => AppState.Panjin_Draw, // 最后状态保持不变
            _ => AppState.Pingjin_Guide
        };

        SetState(nextState);
    }

    /// <summary>
    /// 在Unity编辑器中显示当前状态（调试用）
    /// </summary>
    void OnGUI()
    {
#if UNITY_EDITOR
        GUI.Label(new Rect(10, 10, 300, 30), $"当前状态: {CurrentState}");
#endif
    }
}