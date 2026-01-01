using System.Runtime.CompilerServices;
using UnityEngine;
using System.Collections;

public class PieceBehaviour : MonoBehaviour
{
    public enum PieceType { Base, Water, Cloud, Grass }
    public PieceType Type = PieceType.Water;

    public SpriteRenderer TargetGhost;
    public float FlashSpeed = 2f;

    private Coroutine _flashCoroutine;

    [Header("Snap")]
    public Transform SnapTarget;
    public float SnapRadius = 1.0f; // 增大感应半径便于测试
    [HideInInspector] public bool IsSnapped = false;

    [Header("Flatten / Stitches")]
    public GameObject Stitches; 
    [HideInInspector] public bool IsFlattened = false;
    public Vector3 _looseScale;

    [Header("Visual")]
    public int LayerIndex = 0; 

    private void Start()
    {
        _looseScale = transform.localScale;
        if(Stitches != null) Stitches.SetActive(false);
    }

    public void SnapToTarget(bool instant = false)
    {
        if (SnapTarget == null) return;
        if (instant) transform.position = SnapTarget.position;
        IsSnapped = true;
        // 注意：吸附时先不显示针脚，抚平后再显示
    }

    public void UnSnap()
    {
        if (IsFlattened) return; // 彻底锁定后不可解除吸附
        IsSnapped = false;
    }

    public void SetFlattened(bool flat)
    {
        IsFlattened = flat;
        SetGhostVisible(false);
        if (Stitches != null) Stitches.SetActive(flat);
        // 视觉反馈：彻底固定感
        transform.localScale = flat ? Vector3.one * 180f : _looseScale;

        Collider col = GetComponent<Collider>();
        if(col != null)
        {
            col.enabled = !flat;
        }
    }

    public void SetGhostVisible(bool visible)
    {
        if(TargetGhost == null || IsFlattened)
        {
            if (TargetGhost != null)
            {
                TargetGhost.gameObject.SetActive(false);
            }
            return;
        }

        if (visible)
        {
            TargetGhost.gameObject.SetActive(true);

            if (_flashCoroutine == null)
            {
                _flashCoroutine = StartCoroutine(DoFlash());
            }
        }
        else
        {
            if(_flashCoroutine != null)
            {
                StopCoroutine(_flashCoroutine);
                _flashCoroutine = null;
            }
            TargetGhost.gameObject.SetActive(false);
        }
    }

    private IEnumerator DoFlash()
    {
        while (true)
        {
            // 使用 Sin 函数控制 Alpha 值在 0.2 到 0.6 之间变化
            float alpha = 0.4f + Mathf.Sin(Time.time * FlashSpeed) * 0.2f;
            Color c = TargetGhost.color;
            c.a = alpha;
            TargetGhost.color = c;
            yield return null;
        }
    }
}