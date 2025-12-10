using UnityEditor; // 引入编辑器相关的API
using UnityEngine;
using System.Collections.Generic; // 使用List需要引入
using System.Linq; // 用于使用一些方便的扩展方法

public class BatchMaterialReplacer : EditorWindow // 继承自EditorWindow，表示这是一个编辑器窗口类
{
    // 定义一个列表，用于存储选中的游戏对象
    private List<GameObject> selectedObjects = new List<GameObject>();
    private Material targetMaterial; // 用于在界面中选择的目标材质
    private Vector2 scrollPosition; // 用于实现滚动视图

    // 添加一个菜单项，路径为"Tools/Batch Material Replacer"
    [MenuItem("Tools/Batch Material Replacer")]
    public static void ShowWindow()
    {
        // 获取或创建一个窗口实例，并显示
        GetWindow<BatchMaterialReplacer>("材质批量替换工具");
    }

    // 这是绘制窗口界面的函数
    private void OnGUI()
    {
        // 创建一个滚动视图开始区域
        scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

        GUILayout.Label("材质批量替换工具", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        // -- 区域1: 对象选择 --
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("1. 选择对象", EditorStyles.boldLabel);

        // 按钮：将当前在Hierarchy中选中的对象添加到列表
        if (GUILayout.Button("添加当前选中的对象"))
        {
            AddSelectedObjects();
        }

        // 按钮：清空已添加的对象列表
        if (GUILayout.Button("清空列表"))
        {
            selectedObjects.Clear();
        }

        // 显示已添加的对象数量
        GUILayout.Label($"已添加对象数量: {selectedObjects.Count}");

        // 显示已添加对象的列表，并可手动移除
        for (int i = 0; i < selectedObjects.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            selectedObjects[i] = (GameObject)EditorGUILayout.ObjectField(selectedObjects[i], typeof(GameObject), true);
            // 为每个对象添加一个"移除"按钮
            if (GUILayout.Button("移除", GUILayout.Width(60)))
            {
                selectedObjects.RemoveAt(i);
                i--; // 移除后索引要减1
            }
            EditorGUILayout.EndHorizontal();
        }
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();

        // -- 区域2: 材质选择 --
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("2. 选择目标材质", EditorStyles.boldLabel);
        // 创建一个对象选择字段，用于选择材质
        targetMaterial = (Material)EditorGUILayout.ObjectField("目标材质", targetMaterial, typeof(Material), false);
        EditorGUILayout.EndVertical();

        EditorGUILayout.Space();

        // -- 区域3: 执行操作 --
        EditorGUILayout.BeginVertical("box");
        GUILayout.Label("3. 执行操作", EditorStyles.boldLabel);
        // 只有当选择了材质并且对象列表不为空时，按钮才可点击
        EditorGUI.BeginDisabledGroup(targetMaterial == null || selectedObjects.Count == 0);
        if (GUILayout.Button("一键替换材质（包含子对象）", GUILayout.Height(30)))
        {
            // 调用替换材质的方法
            ReplaceMaterials();
        }
        EditorGUI.EndDisabledGroup();
        EditorGUILayout.EndVertical();

        // 提示信息
        if (selectedObjects.Count == 0)
        {
            EditorGUILayout.HelpBox("请先点击上方按钮，添加一些游戏对象到列表中。", MessageType.Info);
        }
        else if (targetMaterial == null)
        {
            EditorGUILayout.HelpBox("请选择一个目标材质。", MessageType.Warning);
        }

        EditorGUILayout.EndScrollView(); // 结束滚动视图
    }

    // 将当前选中的对象添加到列表的方法
    private void AddSelectedObjects()
    {
        // 获取当前在Unity编辑器中选中的所有游戏对象
        foreach (GameObject obj in Selection.gameObjects)
        {
            // 如果对象不在列表中，则添加
            if (!selectedObjects.Contains(obj))
            {
                selectedObjects.Add(obj);
            }
        }
    }

    // 核心方法：替换材质
    private void ReplaceMaterials()
    {
        if (targetMaterial == null) return;

        // 记录操作步骤，使其可以撤销（Ctrl+Z）
        Undo.RecordObjects(selectedObjects.Where(obj => obj != null).ToArray(), "Batch Replace Materials");

        int replacedCount = 0; // 计数器

        // 遍历所有选中的对象
        foreach (GameObject obj in selectedObjects)
        {
            if (obj == null) continue;

            // 获取当前对象及其所有子对象上的Renderer组件（包括MeshRenderer和SkinnedMeshRenderer等）[4](@ref)
            Renderer[] renderers = obj.GetComponentsInChildren<Renderer>();

            foreach (Renderer renderer in renderers)
            {
                if (renderer != null)
                {
                    // 创建一个新的材质数组，长度与原有数组相同
                    Material[] newMaterials = new Material[renderer.sharedMaterials.Length];
                    // 将数组中的每个元素都设置为目标材质
                    for (int i = 0; i < newMaterials.Length; i++)
                    {
                        newMaterials[i] = targetMaterial;
                    }
                    // 将新的材质数组赋给渲染器
                    renderer.sharedMaterials = newMaterials;
                    replacedCount++;
                }
            }
        }

        // 在控制台输出结果
        Debug.Log($"批量材质替换完成！成功处理了 {replacedCount} 个渲染器。");
    }
}