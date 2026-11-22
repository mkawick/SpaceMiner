using System.IO;
using UnityEditor;
using UnityEngine;

// Tools > Spacey > Build Grix Prefabs
// Points the Grix models at the URP materials in Assets/Models/Grix/Materials, then saves a prefab
// for each into Assets/Prefabs/Grix. Each prefab is an empty root (rotation always zero; the game moves
// and turns it) with the model as a child named "Model". The child stays linked to the FBX, so
// re-exporting from Blender updates it; if a model ever faces the wrong way, rotate the child.
// Existing prefabs are kept (your edits to them survive), except an older prefab at the same path that was
// not made by this tool (no "Model" child made from the matching Grix model, e.g. the first Tvornica): that one is rebuilt in place, keeping
// its asset ID so scene references to it (Build Catalog) still work.
// The player ship's root also gets the gameplay components from Assets/Prefabs/Ships/PlayerStartShip.prefab,
// with the same settings (speed, health, propulsion...).
public static class GrixPrefabBuilder
{
    private const string ModelFolder = "Assets/Models/Grix/";
    private const string PrefabFolder = "Assets/Prefabs/Grix/";
    private const string ShipTemplate = "Assets/Prefabs/Ships/PlayerStartShip.prefab";

    // model, prefab, copy ship components from ShipTemplate?
    private static readonly (string model, string prefab, bool ship)[] Items =
    {
        ("Grix_Rudar.fbx", "Rudar - Mining Bot.prefab", false),
        ("Grix_Prijevoznik.fbx", "Prijevoznik - Transport.prefab", false),
        ("Grix_Skladiste.fbx", "Skladiste - Storage Depot.prefab", false),
        ("Grix_VilinKonjic.fbx", "Vilin Konjic - Player Ship.prefab", true),
        ("Grix_Tvornica.fbx", "Tvornica - Worker Factory.prefab", false),
        ("Grix_Rafinerija.fbx", "Rafinerija - Refinery.prefab", false),
    };

    [MenuItem("Tools/Spacey/Build Grix Prefabs")]
    public static void Build()
    {
        Directory.CreateDirectory(PrefabFolder);
        int created = 0, kept = 0, replaced = 0;

        foreach (var item in Items)
        {
            string modelPath = ModelFolder + item.model;
            string prefabPath = PrefabFolder + item.prefab;

            ModelImporter importer = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (importer == null)
            {
                Debug.LogWarning("Grix prefabs: model not found at " + modelPath);
                continue;
            }

            // Same as the Materials tab: Search and Remap, by material name, in the local Materials folder.
            importer.bakeAxisConversion = false; // the export already puts the models in Unity's axes
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.SearchAndRemapMaterials(ModelImporterMaterialName.BasedOnMaterialName, ModelImporterMaterialSearch.Local);
            importer.SaveAndReimport();

            GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (existing != null && BuiltByThisTool(existing, modelPath))
            {
                kept++;
                continue;
            }
            if (existing != null)
                replaced++;

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(modelPath);
            GameObject root = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
            GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, root.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one;
            if (item.ship)
                CopyShipComponents(root);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath); // overwriting keeps the asset ID
            Object.DestroyImmediate(root);
            created++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log("Grix prefabs: " + created + " built (" + replaced + " of them replaced an older prefab), " + kept +
                  " already existed (materials remapped on all). Folder: " + PrefabFolder);
    }

    // True if the prefab's "Model" child is an instance of this Grix model (i.e. this tool made it).
    private static bool BuiltByThisTool(GameObject prefab, string modelPath)
    {
        Transform model = prefab.transform.Find("Model");
        if (model == null)
            return false;
        Object source = PrefabUtility.GetCorrespondingObjectFromOriginalSource(model.gameObject);
        return source != null && AssetDatabase.GetAssetPath(source) == modelPath;
    }

    // Copies every script on the template ship's root (controller, propulsion, targetable...) with its settings.
    private static void CopyShipComponents(GameObject target)
    {
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(ShipTemplate);
        if (template == null)
        {
            Debug.LogWarning("Grix prefabs: " + ShipTemplate + " not found; add the ship components by hand.");
            return;
        }
        foreach (MonoBehaviour source in template.GetComponents<MonoBehaviour>())
        {
            if (source == null)
                continue;
            Component copy = target.GetComponent(source.GetType());
            if (copy == null)
                copy = target.AddComponent(source.GetType());
            EditorUtility.CopySerialized(source, copy);
        }
    }
}
