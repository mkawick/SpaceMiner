using Unity.Entities;
using UnityEngine;

// Put one of these in the subscene (next to the Build Catalog is fine). It bakes the game-wide
// libraries so systems can spawn things at runtime: ship types and engine flames.
public class GameLibrary_Authoring : MonoBehaviour
{
    [Tooltip("Ship prefabs the game can spawn. GameSession's Starting Ship Type picks one by its position in this list (0 = first).")]
    public GameObject[] shipTypes = new GameObject[0];

    [Header("Pirates")]
    [Tooltip("Pirate ship prefabs (each needs Pirate_Authoring). Waves pick from these at random.")]
    public GameObject[] pirateShips = new GameObject[0];
    public bool piratesEnabled = true;
    [Tooltip("Seconds after the start before the first wave. F9 sends one immediately.")]
    public float firstWaveDelay = 90f;
    public float waveInterval = 180f;
    [Tooltip("Ships per wave: x = min, y = max")]
    public Vector2Int groupSize = new Vector2Int(2, 3);
    [Tooltip("How far from the player a wave appears")]
    public float spawnDistance = 350f;
    [Tooltip("No new waves while this many pirates are alive")]
    public int maxPirates = 8;

    [Tooltip("Empty = " + EngineEffectLibrary.DefaultPath + " (Tools > Spacey > Engine Effect Library)")]
    public EngineEffectLibrary engineEffects;

    private class Baker : Baker<GameLibrary_Authoring>
    {
        public override void Bake(GameLibrary_Authoring a)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent<GameLibrary>(entity);
            DynamicBuffer<ShipPrefab> ships = AddBuffer<ShipPrefab>(entity);
            if (a.shipTypes != null)
                foreach (GameObject ship in a.shipTypes)
                    if (ship != null)
                        ships.Add(new ShipPrefab { name = ship.name, prefab = GetEntity(ship, TransformUsageFlags.Dynamic) });

            AddComponent(entity, new PirateSpawnConfig
            {
                enabled = a.piratesEnabled,
                firstWaveDelay = Mathf.Max(0f, a.firstWaveDelay),
                waveInterval = Mathf.Max(5f, a.waveInterval),
                groupSize = new Unity.Mathematics.int2(Mathf.Max(1, a.groupSize.x), Mathf.Max(a.groupSize.x, a.groupSize.y)),
                spawnDistance = Mathf.Max(50f, a.spawnDistance),
                maxAlive = Mathf.Max(1, a.maxPirates)
            });
            DynamicBuffer<PiratePrefab> pirates = AddBuffer<PiratePrefab>(entity);
            if (a.pirateShips != null)
                foreach (GameObject ship in a.pirateShips)
                    if (ship != null)
                        pirates.Add(new PiratePrefab { prefab = GetEntity(ship, TransformUsageFlags.Dynamic) });

            DynamicBuffer<EngineEffectPrefab> buffer = AddBuffer<EngineEffectPrefab>(entity);

            EngineEffectLibrary library = a.engineEffects;
#if UNITY_EDITOR
            if (library == null)
                library = UnityEditor.AssetDatabase.LoadAssetAtPath<EngineEffectLibrary>(EngineEffectLibrary.DefaultPath);
#endif
            if (library == null)
            {
                Debug.LogWarning("GameLibrary: no Engine Effect Library found; ships will have no flames.");
                return;
            }
            DependsOn(library);
            foreach (EngineEffectLibrary.Entry e in library.entries)
            {
                if (e.prefab == null)
                    continue;
                buffer.Add(new EngineEffectPrefab
                {
                    style = (byte)Mathf.Clamp(e.style, 0, 255),
                    colour = e.colour,
                    prefab = GetEntity(e.prefab, TransformUsageFlags.Dynamic)
                });
            }
        }
    }
}
