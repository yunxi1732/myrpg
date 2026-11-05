using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(DialogueAsset))]
public sealed class DialogueAssetEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();
        var dialogue = (DialogueAsset)target;
        var errors = dialogue.GetValidationErrors();
        EditorGUILayout.Space();
        EditorGUILayout.HelpBox("索引从 0 开始；ID 区分大小写。跳转 ID 留空表示结束，允许循环对话。", MessageType.Info);
        if (errors.Count == 0)
            EditorGUILayout.HelpBox("对话配置检查通过。", MessageType.Info);
        foreach (string error in errors)
            EditorGUILayout.HelpBox(error, MessageType.Error);
    }
}
