using System.IO;
using UnityEditor;
using UnityEngine;

// Entities Graphics can't deform skinned meshes with URP/Lit, so an animated model used as an
// entity prefab logs "does not support skinning" and won't animate anyway. This makes a static
// copy: every SkinnedMeshRenderer becomes a MeshFilter + MeshRenderer with the same mesh and
// materials, and Animators are removed. The copy is saved next to the original as "<name> (Static)".
public static class MakeStaticPrefab
{
    [MenuItem("Assets/Spacey/Make Static Copy (for Entities)", true)]
    private static bool Validate() =>
        Selection.activeObject is GameObject go && PrefabUtility.IsPartOfPrefabAsset(go);

    [MenuItem("Assets/Spacey/Make Static Copy (for Entities)")]
    private static void Make()
    {
        var source = (GameObject)Selection.activeObject;
        string sourcePath = AssetDatabase.GetAssetPath(source);
        string folder = "Assets/Prefabs";
        string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, source.name + " (Static).prefab").Replace('\\', '/'));

        GameObject copy = Object.Instantiate(source);
        copy.name = source.name + " (Static)";
        int converted = 0;
        foreach (var skinned in copy.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            GameObject go = skinned.gameObject;
            Mesh mesh = skinned.sharedMesh;
            Material[] materials = skinned.sharedMaterials;
            Object.DestroyImmediate(skinned);
            if (mesh == null)
                continue;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = materials;
            converted++;
        }
        foreach (var animator in copy.GetComponentsInChildren<Animator>(true))
            Object.DestroyImmediate(animator);
        foreach (var animation in copy.GetComponentsInChildren<Animation>(true))
            Object.DestroyImmediate(animation);

        GameObject saved = PrefabUtility.SaveAsPrefabAsset(copy, path);
        Object.DestroyImmediate(copy);
        Selection.activeObject = saved;
        EditorGUIUtility.PingObject(saved);
        Debug.Log($"Made static copy of {sourcePath} at {path} ({converted} skinned meshes converted).");
    }
}
