using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Solo.MOST_IN_ONE;

public class WeavingStackController : MonoBehaviour
{
    public Camera MainCamera;
    public LayerMask PieceLayerMask;
    public float MoveLerp = 25f;
    public float SnapLerp = 15f;
    public float SnapDistance = 100f;
    public InputManager_Scene1 inputManager;

    [Header("Vibration")]
    public bool EnableVibration = true;
    public float HeavyPulseCount = 3f;
    public float LightPulseCount = 1f;
    public float PulseInterval = 0.1f;

    [Header("Snapped Movement Constraint")]
    public float SnappedMoveRadius = 0.3f;

    private float _flattenThreshold = 2.0f;

    private PieceBehaviour _selected = null;
    private Vector3 _selectedOffset;
    private Vector3 _selectedDesiredPos;
    private float _selectedZ;
    private float _flattenProgress = 0f;
    private bool _wasTouchingLastFrame = false;
    private bool _isReturning = false;

    private enum GuideStep { Press, Drag, Pinch, Completed }
    private GuideStep _currentStep = GuideStep.Press;
    private bool _isFirstPieceHandled = false;

    public GameObject PressGuide;
    public GameObject DragGuide;
    public GameObject PinchGuide;

    private void Start()
    {
        if (MainCamera == null) MainCamera = Camera.main;
        inputManager = InputManager_Scene1.Instance;

        if (inputManager != null)
        {
            // 确保进入堆绫模式
            inputManager.SetMode("Duiling");

            inputManager.OnPrimaryTouchContactStarted += OnPrimaryTouchStarted;
            inputManager.OnPrimaryTouchContactCanceled += OnPrimaryTouchCanceled;
            inputManager.OnPinchDeltaChanged += OnPinchDelta;
        }

        UpdateGuideUI();
    }

    private void Update()
    {
        if (inputManager == null) return;

        bool isTouching = inputManager.IsPrimaryTouchPressed();
        Vector2 screenPos = inputManager.GetPrimaryTouchPosition();

        if (isTouching)
        {
            _isReturning = false;
            if (!_wasTouchingLastFrame)
            {
                // 等价于“Touch Started”
                TrySelect(screenPos);
            }

            UpdateDrag(screenPos);            
            
        }
        else
        {
            if (_wasTouchingLastFrame)
            {
                // 等价于“Touch Canceled”
                if(_selected != null && _selected.IsSnapped)
                {
                    StartCoroutine(HandleRelease());
                }
                
                ReleaseSelection();
            }
        }

        _wasTouchingLastFrame = isTouching;

        UpdateSnapAndDepth();
    }

    private void OnPrimaryTouchStarted()
    {
        Vector2 screenPos = inputManager.GetPrimaryTouchPosition();
        Debug.Log($"TouchPos = {screenPos}, Screen = {Screen.width} x {Screen.height}");
        Ray ray = MainCamera.ScreenPointToRay(screenPos);
        RaycastHit hit;

        // 只有点中布片才开始选择
        if (Physics.Raycast(ray, out hit, 2000f, PieceLayerMask))
        {
            Debug.Log("击中了物体：" + hit.collider.name);
            var p = hit.collider.GetComponent<PieceBehaviour>();
            if (p != null && !p.IsFlattened) // 彻底锁定的布片不再响应
            {
                _selected = p;
                _selected.UnSnap(); // 重新开始移动，解除暂时吸附状态
                _selectedOffset = _selected.transform.position - ScreenToWorld(screenPos, _selected.transform.position.z);
                _selectedDesiredPos = _selected.transform.position;
            }
        }
    }

    private void OnDragPerformed(Vector2 screenPos)
    {
        // 只有已经选中了布片，才更新期望坐标
        if (_selected != null && !_selected.IsFlattened)
        {
            _selectedDesiredPos = ScreenToWorld(screenPos, _selected.transform.position.z) + _selectedOffset;
        }
    }

    private void OnPrimaryTouchCanceled()
    {
        _selected = null;
        _flattenProgress = 0;
    }

    private void OnPinchDelta(float delta)
    {
        PieceBehaviour target = _selected;

        if(target == null)
        {
            Vector2 touchPos = inputManager.GetPrimaryTouchPosition();
            Ray ray = MainCamera.ScreenPointToRay(touchPos);
            if(Physics.Raycast(ray, out RaycastHit hit, 2000f, PieceLayerMask))
            {
                target = hit.collider.GetComponent<PieceBehaviour>();
            }
        }
        // 抚平逻辑：必须是已吸附但未彻底锁定的物体
        if (target == null || !target.IsSnapped || target.IsFlattened) return;

        if (delta > 0.01f) // 识别向外扩撑
        {
            _flattenProgress += delta * 5f; // 调节灵敏度

            if (_flattenProgress >= _flattenThreshold)
            {
                if(target.SnapTarget != null)
                {
                    target.transform.position = target.SnapTarget.position;
                    _selectedDesiredPos = target.SnapTarget.position;
                }

                _selected.SetFlattened(true);

                if (EnableVibration)
                {
                    if (_selected.Type == PieceBehaviour.PieceType.Water)
                    {
                        // 水纹：调用 HeavyImpact（沉重震动，模拟厚实感）
                        MOST_HapticFeedback.Generate(MOST_HapticFeedback.HapticTypes.HeavyImpact); 
                        Debug.Log("执行沉重震动：咚—");
                    }
                    else if(_selected.Type == PieceBehaviour.PieceType.Cloud)
                    {
                        // 云纹：调用 LightImpact（轻盈震动，模拟轻飘感）
                        MOST_HapticFeedback.Generate(MOST_HapticFeedback.HapticTypes.LightImpact);
                        Debug.Log("执行轻盈震动：嗡~");
                    }
                    else if(_selected.Type == PieceBehaviour.PieceType.Grass)
                    {
                        // 草纹：调用 MediumImpact（中等震动）
                        MOST_HapticFeedback.Generate(MOST_HapticFeedback.HapticTypes.MediumImpact);
                        Debug.Log("执行中等震动：嗡~");
                    }
                }
                Debug.Log("纹布已抚平固定。");

                if(_currentStep == GuideStep.Pinch)
                {
                    _currentStep = GuideStep.Completed;
                    UpdateGuideUI();
                }

                // if(EnableVibration) Handheld.Vibrate();

                // 清理状态
                _selected = null; 
                _flattenProgress = 0f;
            }
        }
        else
        {
            _flattenProgress = Mathf.Max(0f, _flattenProgress + delta);
        }
    }

    private Vector3 ScreenToWorld(Vector2 screenPos, float actualZ)
    {
        // 修正：确保 Z 轴使用相机与平面的绝对距离
        float distanceToPlane = Mathf.Abs(MainCamera.transform.position.z - actualZ);

        Vector3 sp = new Vector3(screenPos.x, screenPos.y, distanceToPlane);
        Vector3 worldPos = MainCamera.ScreenToWorldPoint(sp);

        worldPos.z = actualZ;
        return worldPos;
    }

    private IEnumerator SnapAndHaptics(PieceBehaviour piece)
    {
        if (piece == null) yield break;
        
        piece.SnapToTarget(false); // 标记为已吸附，停止 Update 里的吸附检查
        
        Vector3 start = piece.transform.position;
        Vector3 end = piece.SnapTarget.position;
        float t = 0f;
        while (t < 1f)
        {
            t += Time.deltaTime * SnapLerp;
            piece.transform.position = Vector3.Lerp(start, end, t);
            _selectedDesiredPos = piece.transform.position; // 同步期望坐标，防止抖动
            yield return null;
        }
        
    }

    private IEnumerator HapticPulse(int count)
    {
        for (int i = 0; i < count; i++)
        {
            Handheld.Vibrate();
            yield return new WaitForSeconds(PulseInterval);
        }
    }

    private void OnDestroy()
    {
        if (inputManager != null)
        {
            inputManager.OnPrimaryTouchContactStarted -= OnPrimaryTouchStarted;
            inputManager.OnPrimaryTouchContactCanceled -= OnPrimaryTouchCanceled;
            inputManager.OnPinchDeltaChanged -= OnPinchDelta;
        }
    }

    private void TrySelect(Vector2 screenPos)
    {
        Ray ray = MainCamera.ScreenPointToRay(screenPos);
        RaycastHit hit;

        if (Physics.Raycast(ray, out hit, 2000f, PieceLayerMask))
        {
            var p = hit.collider.GetComponent<PieceBehaviour>();
            if (p != null && !p.IsFlattened)
            {
                _selected = p;

                if(_currentStep == GuideStep.Press)
                {
                    _currentStep = GuideStep.Drag;
                    UpdateGuideUI();
                }

                _selected.SetGhostVisible(true);
                _selected.UnSnap();

                _selectedZ = _selected.transform.position.z;
                Debug.Log($"_selectedZ的值为:{_selectedZ}");

                _selectedOffset =
                    _selected.transform.position - ScreenToWorld(screenPos, _selectedZ);

                _selectedDesiredPos = _selected.transform.position;
            }
        }
    }

    private void UpdateDrag(Vector2 screenPos)
    {
        if (_selected == null || _selected.IsFlattened) return;

        _selectedDesiredPos = ScreenToWorld(screenPos, _selectedZ) + _selectedOffset;

        if(_selected.IsSnapped && _selected.SnapTarget != null)
        {
            Vector3 center = _selected.SnapTarget.position;
            Vector3 offset = _selectedDesiredPos - center;
            offset.z = 0;
            
            if(offset.magnitude > SnappedMoveRadius)
            {
                offset = offset.normalized * SnappedMoveRadius;
                _selectedDesiredPos = center + offset;
            }
        }

        _selected.transform.position = Vector3.Lerp(
            _selected.transform.position,
            _selectedDesiredPos,
            Time.deltaTime * MoveLerp
        );
    }

    private void ReleaseSelection()
    {
        if(_selected != null)
        {
            _selected.SetGhostVisible(false);
        }
        _selected = null;
        _flattenProgress = 0f;
    }

    private void UpdateSnapAndDepth()
    {
        if (_selected == null) return;

        // 视差深度
        Vector3 p = _selected.transform.position;
        //p.z = -_selected.LayerIndex * 0.1f;
        _selected.transform.position = p;

        // 吸附检测
        if (_selected.SnapTarget != null && !_selected.IsSnapped)
        {
            float d = Vector3.Distance(
                _selected.transform.position,
                _selected.SnapTarget.position
            );

            Debug.Log($"{d}");

            if (d <= SnapDistance)
            {
                StartCoroutine(SnapAndHaptics(_selected));

                if(_currentStep == GuideStep.Drag)
                {
                    _currentStep = GuideStep.Pinch;
                    UpdateGuideUI();
                }
            }
        }
    }

    private IEnumerator HandleRelease()
    {
        if(_selected != null)
        {
            if (_selected.IsSnapped && !_selected.IsFlattened && _selected.SnapTarget != null)
            {
                _isReturning = true; // 标记开始回弹
            
                Vector3 startPos = _selected.transform.position;
                Vector3 targetPos = _selected.SnapTarget.position;
                float t = 0f;

                // 在 0.2 ~ 0.3 秒内平滑回弹
                while (t < 1f)
                {
                    t += Time.deltaTime * (MoveLerp / 2f); // 使用 MoveLerp 的速度比例
                    if (_selected == null) yield break; // 防止异常销毁
                
                    // 执行回弹位移
                    _selected.transform.position = Vector3.Lerp(startPos, targetPos, t);
                    yield return null;
                }
            
                _selected.transform.position = targetPos; // 确保精准对齐
                _isReturning = false;
            }
        }
    }

    private void UpdateGuideUI()
    {
        if(_currentStep == GuideStep.Completed)
        {
            if (PressGuide) PressGuide.SetActive(false);
            if (DragGuide) DragGuide.SetActive(false);
            if (PinchGuide) PinchGuide.SetActive(false);
            return;
        }

        if (PressGuide) PressGuide.SetActive(_currentStep == GuideStep.Press);
        if (DragGuide) DragGuide.SetActive(_currentStep == GuideStep.Drag);
        if (PinchGuide) PinchGuide.SetActive(_currentStep == GuideStep.Pinch);
    }
}