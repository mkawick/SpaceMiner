using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Put one of these in the subscene. At startup the field is generated from the seed:
// the same seed gives the same field on every machine.
public class AsteroidField_Authoring : MonoBehaviour
{
    [Header("Session")]
    [Tooltip("Same seed = same field everywhere. Will come from the server later.")]
    public uint seed = 20261024;

    [Header("Map")]
    public float mapRadius = 10000f;
    [Tooltip("Hex radius, centre to corner")]
    public float hexSize = 200f;

    [Header("Homeworlds (x, z)")]
    public Vector2 grixHome = new Vector2(4500f, -6200f);
    public Vector2 postibuleHome = new Vector2(-7400f, 900f);
    public Vector2 fairwayHome = new Vector2(5400f, 5400f);
    public float newbieZoneRadius = 1800f;
    [Tooltip("Hexes between the newbie zone and this distance are player start hexes")]
    public float starterRingRadius = 4000f;

    [Header("Asteroids")]
    [Tooltip("Filled from Assets/Models/LowPolyAsteroids/Prefabs when the component is added")]
    public GameObject[] asteroidPrefabs;
    public int starterAsteroidsPerHex = 6;
    [Tooltip("x at the map edge, y at the centre")]
    public Vector2Int wildAsteroidsPerHex = new Vector2Int(2, 6);
    [Tooltip("Asteroid radius in world units")]
    public Vector2 radiusRange = new Vector2(1.5f, 6f);
    [Tooltip("Slow tumble, degrees per second")]
    public Vector2 spinDegreesPerSecond = new Vector2(2f, 8f);

    [Header("Mining")]
    [Tooltip("Chance per hit that a piece of ore pops out")]
    [Range(0f, 1f)] public float oreDropChance = 0.5f;
    [Tooltip("Ore pieces each asteroid gives before it breaks up")]
    public int orePerAsteroid = 100;
    [Tooltip("How far ore pops out beyond the asteroid's surface: x = min, y = max")]
    public Vector2 oreScatterDistance = new Vector2(1f, 2f);
    [Tooltip("Seconds for ore to pop out to its resting spot")]
    public float oreScatterTime = 0.4f;
    [Tooltip("How far from the direction of the shot ore may fly, in degrees")]
    public float oreSpreadDegrees = 70f;
    [Tooltip("One prefab per ore type, in order: Iron, Copper, Silicate, Ice, Carbon, Gold, Fermite. " +
             "Empty slots fall back to Assets/Prefabs/Ore/Ore_<Type>.prefab")]
    public GameObject[] orePrefabs = new GameObject[AsteroidFieldRules.ResourceCount];

    [Header("Player start")]
    public CombatFaction playerFaction = CombatFaction.PostibuleSanctuary;
    [Tooltip("Which starter hex to use; different numbers give different hexes")]
    public int playerStartSlot = 0;

#if UNITY_EDITOR
    private void Reset()
    {
        string[] guids = UnityEditor.AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Models/LowPolyAsteroids/Prefabs" });
        var list = new System.Collections.Generic.List<GameObject>();
        foreach (string guid in guids)
        {
            string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
            GameObject prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
                list.Add(prefab);
        }
        asteroidPrefabs = list.ToArray();
    }
#endif
}

class AsteroidField_Baker : Baker<AsteroidField_Authoring>
{
    public override void Bake(AsteroidField_Authoring a)
    {
        Entity entity = GetEntity(TransformUsageFlags.None);

        AddComponent(entity, new AsteroidFieldConfig
        {
            seed = a.seed == 0 ? 1u : a.seed,
            mapRadius = a.mapRadius,
            hexSize = math.max(10f, a.hexSize),
            grixHome = a.grixHome,
            postibuleHome = a.postibuleHome,
            fairwayHome = a.fairwayHome,
            newbieZoneRadius = a.newbieZoneRadius,
            starterRingRadius = a.starterRingRadius,
            starterAsteroidsPerHex = math.max(3, a.starterAsteroidsPerHex),
            wildAsteroidsPerHex = new int2(a.wildAsteroidsPerHex.x, a.wildAsteroidsPerHex.y),
            radiusRange = a.radiusRange,
            spinRange = a.spinDegreesPerSecond,
            playerFaction = a.playerFaction,
            playerStartSlot = a.playerStartSlot
        });

        AddComponent(entity, new MiningConfig
        {
            oreDropChance = math.saturate(a.oreDropChance),
            orePerAsteroid = math.max(1, a.orePerAsteroid),
            oreScatterDistance = new float2(math.max(0f, a.oreScatterDistance.x), math.max(a.oreScatterDistance.x, a.oreScatterDistance.y)),
            oreScatterTime = math.max(0.01f, a.oreScatterTime),
            oreSpreadDegrees = math.clamp(a.oreSpreadDegrees, 0f, 180f)
        });

        DynamicBuffer<OrePrefab> orePrefabs = AddBuffer<OrePrefab>(entity);
        for (int i = 0; i < AsteroidFieldRules.ResourceCount; i++)
        {
            GameObject ore = a.orePrefabs != null && i < a.orePrefabs.Length ? a.orePrefabs[i] : null;
#if UNITY_EDITOR
            if (ore == null)
                ore = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/Ore/Ore_{(ResourceType)i}.prefab");
#endif
            if (ore == null)
                Debug.LogWarning($"AsteroidField: no ore prefab for {(ResourceType)i}; that ore won't appear.");
            else
                DependsOn(ore);
            orePrefabs.Add(new OrePrefab { prefab = ore != null ? GetEntity(ore, TransformUsageFlags.Dynamic) : Entity.Null });
        }

        DynamicBuffer<AsteroidVisualPrefab> prefabs = AddBuffer<AsteroidVisualPrefab>(entity);
        if (a.asteroidPrefabs == null)
            return;

        foreach (GameObject go in a.asteroidPrefabs)
        {
            if (go == null)
                continue;

            float radius = 1f;
            MeshFilter meshFilter = go.GetComponentInChildren<MeshFilter>();
            if (meshFilter != null && meshFilter.sharedMesh != null)
            {
                DependsOn(meshFilter.sharedMesh);
                Vector3 extents = meshFilter.sharedMesh.bounds.extents;
                Vector3 scale = meshFilter.transform.lossyScale;
                radius = Mathf.Max(extents.x * scale.x, Mathf.Max(extents.y * scale.y, extents.z * scale.z));
            }

            prefabs.Add(new AsteroidVisualPrefab
            {
                prefab = GetEntity(go, TransformUsageFlags.Dynamic),
                meshRadius = math.max(0.01f, radius)
            });
        }
    }
}
