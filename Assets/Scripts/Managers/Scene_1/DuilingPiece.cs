using UnityEngine;

public class PieceBehaviour : MonoBehaviour
{
    public enum PieceType { Base, Water, Cloud, Treasure }
    public PieceType Type = PieceType.Water;

    [Header("Snap")]
    public Transform SnapTarget;
    public float SnapRadius = 1.0f; // 增大感应半径便于测试
    [HideInInspector] public bool IsSnapped = false;

    [Header("Flatten / Stitches")]
    public GameObject Stitches; 
    [HideInInspector] public bool IsFlattened = false;

    [Header("Visual")]
    public int LayerIndex = 0; 

    private void Start()
    {
        if(Stitches != null) Stitches.SetActive(false);
        transform.localScale = Vector3.one * 1.05f;
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
        if (Stitches != null) Stitches.SetActive(flat);
        // 视觉反馈：彻底固定感
        transform.localScale = flat ? Vector3.one : Vector3.one * 1.05f;
    }
}