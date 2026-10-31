using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(EngineEffectLibrary))]
public class EngineEffectLibraryEditor : Editor
{
    private static readonly Regex PackName = new Regex(@"^EngineEffect(\d+)(Blue|Green|Red|White|Yellow)$");

    [MenuItem("Tools/Spacey/Engine Effect Library")]
    private static void Open()
    {
        Selection.activeObject = FindOrCreate();
    }

    [InitializeOnLoadMethod]
    private static void EnsureAsset()
    {
        EditorApplication.delayCall += () => FindOrCreate();
    }

    public static EngineEffectLibrary FindOrCreate()
    {
        var library = AssetDatabase.LoadAssetAtPath<EngineEffectLibrary>(EngineEffectLibrary.DefaultPath);
        if (library != null)
            return library;
        if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            AssetDatabase.CreateFolder("Assets", "Settings");
        library = CreateInstance<EngineEffectLibrary>();
        Refresh(library);
        AssetDatabase.CreateAsset(library, EngineEffectLibrary.DefaultPath);
        AssetDatabase.SaveAssets();
        return library;
    }

    // Adds every EngineEffect<style><Colour> prefab from the pack that isn't listed yet.
    public static int Refresh(EngineEffectLibrary library)
    {
        if (!AssetDatabase.IsValidFolder(EngineEffectLibrary.PackFolder))
            return 0;
        int added = 0;
        foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { EngineEffectLibrary.PackFolder }))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;
            Match m = PackName.Match(prefab.name);
            if (!m.Success)
                continue;
            if (library.entries.Exists(e => e.prefab == prefab))
                continue;
            library.entries.Add(new EngineEffectLibrary.Entry
            {
                style = int.Parse(m.Groups[1].Value),
                colour = (FlameColour)System.Enum.Parse(typeof(FlameColour), m.Groups[2].Value),
                prefab = prefab
            });
            added++;
        }
        library.entries.Sort((a, b) => a.style != b.style ? a.style.CompareTo(b.style) : a.colour.CompareTo(b.colour));
        return added;
    }

    public override void OnInspectorGUI()
    {
        var library = (EngineEffectLibrary)target;
        EditorGUILayout.HelpBox(
            "Engine flames by style and colour. Ships pick a style and colour in their Propulsion component. " +
            "Styles 1-6 come from SimpleSpaceshipEngineEffects; add your own as 7+.", MessageType.None);
        if (GUILayout.Button("Refresh from pack"))
        {
            Undo.RecordObject(library, "Refresh Engine Effect Library");
            int added = Refresh(library);
            EditorUtility.SetDirty(library);
            Debug.Log($"Engine Effect Library: added {added} prefab(s).");
        }
        EditorGUILayout.Space(6f);
        DrawDefaultInspector();
    }
}
