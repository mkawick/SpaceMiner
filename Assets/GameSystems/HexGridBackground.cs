using Unity.Entities;
using UnityEngine;
using UnityEngine.Rendering;

// Endless hex grid under the play plane. Put this on an empty GameObject in the main scene and give it
// Assets/Mats/HexGrid.mat. It keeps a big flat quad centred under the camera; the Spacey/HexGrid shader
// draws the hexes from world position, so the grid looks infinite and lines up with the minimap.
[ExecuteAlways]
public class HexGridBackground : MonoBehaviour
{
    [Tooltip("A material using the Spacey/HexGrid shader (Assets/Mats/HexGrid.mat). Colours, line width and fade live there.")]
    public Material material;
    [Tooltip("0 = use the asteroid field's hex size, so cells match the minimap")]
    public float hexSize = 0f;
    [Tooltip("Height of the grid. Slightly below 0 keeps it under ships and bots")]
    public float height = -2f;
    [Tooltip("Width of the quad that follows the camera; anything past the shader's Fade End is invisible anyway")]
    public float quadSize = 12000f;

    private GameObject quad;
    private MaterialPropertyBlock block;
    private static readonly int HexSizeId = Shader.PropertyToID("_HexSize");
    private static readonly int HomeHexId = Shader.PropertyToID("_HomeHex");
    private static readonly int HomeColorId = Shader.PropertyToID("_HomeColor");
    private static readonly int HasHomeId = Shader.PropertyToID("_HasHome");
    private EntityQuery fieldReadyQuery;
    private World queryWorld;

    private void OnEnable()
    {
        quad = new GameObject("HexGridQuad") { hideFlags = HideFlags.HideAndDontSave };
        quad.transform.SetParent(transform, false);
        quad.AddComponent<MeshFilter>().sharedMesh = BuildQuad();
        MeshRenderer renderer = quad.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = LightProbeUsage.Off;
        renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        block = new MaterialPropertyBlock();
    }

    private void OnDisable()
    {
        if (quad == null)
            return;
        if (Application.isPlaying) Destroy(quad);
        else DestroyImmediate(quad);
        quad = null;
    }

    private void LateUpdate()
    {
        if (quad == null)
            return;
        MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
        if (renderer.sharedMaterial != material)
            renderer.sharedMaterial = material;

        Camera cam = Camera.main;
#if UNITY_EDITOR
        if (!Application.isPlaying && UnityEditor.SceneView.lastActiveSceneView != null)
            cam = UnityEditor.SceneView.lastActiveSceneView.camera;
#endif
        Vector3 centre = cam != null ? cam.transform.position : Vector3.zero;
        quad.transform.position = new Vector3(centre.x, height, centre.z);
        quad.transform.rotation = Quaternion.identity;
        quad.transform.localScale = new Vector3(quadSize, 1f, quadSize);

        float size = hexSize;
        if (size <= 0f && Application.isPlaying && GameEcs.TryGetConfig(out AsteroidFieldConfig config))
            size = config.hexSize;
        block.Clear();
        if (size > 0f)
            block.SetFloat(HexSizeId, size);

        // The player's starting hex, drawn in their colour.
        bool hasHome = TryGetHomeHex(out Vector2 homeHex);
        block.SetFloat(HasHomeId, hasHome ? 1f : 0f);
        block.SetVector(HomeHexId, new Vector4(homeHex.x, homeHex.y, 0f, 0f));
        block.SetColor(HomeColorId, GameSession.PlayerColour);
        renderer.SetPropertyBlock(block);
    }

    private bool TryGetHomeHex(out Vector2 hex)
    {
        hex = Vector2.zero;
        if (!Application.isPlaying)
            return false;
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;
        if (queryWorld != world)
        {
            fieldReadyQuery = world.EntityManager.CreateEntityQuery(ComponentType.ReadOnly<AsteroidFieldReady>());
            queryWorld = world;
        }
        if (fieldReadyQuery.CalculateEntityCount() != 1)
            return false;
        AsteroidFieldReady ready = fieldReadyQuery.GetSingleton<AsteroidFieldReady>();
        hex = new Vector2(ready.playerHex.x, ready.playerHex.y);
        return true;
    }

    // A 1x1 quad lying flat on XZ, facing up.
    private static Mesh BuildQuad()
    {
        var mesh = new Mesh { name = "HexGridQuad", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = new[] { new Vector3(-0.5f, 0, -0.5f), new Vector3(-0.5f, 0, 0.5f), new Vector3(0.5f, 0, 0.5f), new Vector3(0.5f, 0, -0.5f) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.normals = new[] { Vector3.up, Vector3.up, Vector3.up, Vector3.up };
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(1f, 1f, 1f));
        return mesh;
    }
}
