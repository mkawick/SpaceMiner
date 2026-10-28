using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Inspector for the Layer Matrix: a checkbox grid like Unity's Layer Collision Matrix,
// plus a second grid for who can target what.
[CustomEditor(typeof(LayerMatrixSettings))]
public class LayerMatrixEditor : Editor
{
    private const float LabelWidth = 110f;
    private const float Cell = 18f;
    private const float HeaderHeight = 86f;
    private static bool showSpare;

    [MenuItem("Tools/Spacey/Layer Matrix")]
    private static void Open()
    {
        Selection.activeObject = FindOrCreate();
    }

    // Make sure the project has the asset, so new checkouts get one without any setup.
    [InitializeOnLoadMethod]
    private static void EnsureAsset()
    {
        EditorApplication.delayCall += () => FindOrCreate();
    }

    public static LayerMatrixSettings FindOrCreate()
    {
        var asset = AssetDatabase.LoadAssetAtPath<LayerMatrixSettings>(LayerMatrixSettings.DefaultPath);
        if (asset != null)
            return asset;
        if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            AssetDatabase.CreateFolder("Assets", "Settings");
        asset = CreateInstance<LayerMatrixSettings>();
        asset.FromMatrix(LayerMatrix.Default());
        AssetDatabase.CreateAsset(asset, LayerMatrixSettings.DefaultPath);
        AssetDatabase.SaveAssets();
        return asset;
    }

    public override void OnInspectorGUI()
    {
        var settings = (LayerMatrixSettings)target;
        settings.EnsureSize();

        EditorGUILayout.HelpBox(
            "Spacey's own layers (not Unity physics). Set an object's layer with the Targetable component, " +
            "and a weapon's projectile layer in its WeaponDef. Friendly fire is blocked separately by faction.",
            MessageType.None);
        showSpare = EditorGUILayout.Toggle("Show spare layers", showSpare);

        var layers = new List<int>();
        for (int i = 0; i < LayerMatrix.LayerCount; i++)
            if (showSpare || !((CollisionLayer)i).ToString().StartsWith("Spare"))
                layers.Add(i);

        EditorGUILayout.Space(8f);
        EditorGUILayout.LabelField("Collision: which layers hit each other", EditorStyles.boldLabel);
        DrawGrid(settings, settings.collides, layers, true);

        EditorGUILayout.Space(12f);
        EditorGUILayout.LabelField("Targeting: the row can target the column", EditorStyles.boldLabel);
        DrawGrid(settings, settings.targets, layers, false);

        EditorGUILayout.Space(12f);
        if (GUILayout.Button("Reset to defaults"))
        {
            Undo.RecordObject(settings, "Reset Layer Matrix");
            settings.FromMatrix(LayerMatrix.Default());
            EditorUtility.SetDirty(settings);
        }
    }

    private static void DrawGrid(LayerMatrixSettings settings, int[] rows, List<int> layers, bool symmetric)
    {
        int n = layers.Count;
        Rect area = GUILayoutUtility.GetRect(LabelWidth + Cell * n + 8f, HeaderHeight + Cell * n + 4f);

        // Column headers, written upwards.
        for (int c = 0; c < n; c++)
        {
            var pivot = new Vector2(area.x + LabelWidth + c * Cell + Cell * 0.5f, area.y + HeaderHeight - 2f);
            Matrix4x4 saved = GUI.matrix;
            GUIUtility.RotateAroundPivot(-90f, pivot);
            GUI.Label(new Rect(pivot.x, pivot.y - Cell * 0.5f, HeaderHeight, Cell), Label(layers[c]));
            GUI.matrix = saved;
        }

        for (int r = 0; r < n; r++)
        {
            int row = layers[r];
            float y = area.y + HeaderHeight + r * Cell;
            GUI.Label(new Rect(area.x, y, LabelWidth - 4f, Cell), Label(row));
            for (int c = 0; c < n; c++)
            {
                int col = layers[c];
                var cell = new Rect(area.x + LabelWidth + c * Cell + 2f, y + 1f, Cell, Cell);
                bool on = (rows[row] & (1 << col)) != 0;
                bool next = EditorGUI.Toggle(cell, on);
                if (next == on)
                    continue;
                Undo.RecordObject(settings, "Edit Layer Matrix");
                rows[row] = next ? rows[row] | (1 << col) : rows[row] & ~(1 << col);
                if (symmetric)
                    rows[col] = next ? rows[col] | (1 << row) : rows[col] & ~(1 << row);
                EditorUtility.SetDirty(settings);
            }
        }
    }

    private static string Label(int layer)
    {
        return ObjectNames.NicifyVariableName(((CollisionLayer)layer).ToString());
    }
}
