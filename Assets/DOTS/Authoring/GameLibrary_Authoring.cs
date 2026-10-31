using Unity.Entities;
using UnityEngine;

// Put one of these in the subscene (next to the Build Catalog is fine). It bakes the game-wide
// libraries so systems can spawn things at runtime: ship types, pirate parties and engine flames.
public class GameLibrary_Authoring : MonoBehaviour
{
    [Tooltip("Ship prefabs the game can spawn. GameSession's Starting Ship Type picks one by its position in this list (0 = first).")]
    public GameObject[] shipTypes = new GameObject[0];

    [Header("Pirates")]
    [Tooltip("Which pirate parties appear and when. Empty = " + PirateRoster.DefaultPath + " (Tools > Spacey > Pirate Roster)")]
    public PirateRoster pirateRoster;

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

            BakePirates(a, entity);

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

        private void BakePirates(GameLibrary_Authoring a, Entity entity)
        {
            DynamicBuffer<PiratePartyDef> parties = AddBuffer<PiratePartyDef>(entity);
            DynamicBuffer<PiratePartyShip> partyShips = AddBuffer<PiratePartyShip>(entity);

            PirateRoster roster = a.pirateRoster;
#if UNITY_EDITOR
            if (roster == null)
                roster = UnityEditor.AssetDatabase.LoadAssetAtPath<PirateRoster>(PirateRoster.DefaultPath);
#endif
            if (roster == null)
            {
                AddComponent(entity, new PirateSpawnConfig { enabled = false });
                return;
            }
            DependsOn(roster);
            AddComponent(entity, new PirateSpawnConfig
            {
                enabled = roster.enabled,
                firstWaveDelay = Mathf.Max(0f, roster.firstWaveDelay),
                waveInterval = Mathf.Max(5f, roster.waveInterval),
                spawnDistance = Mathf.Max(50f, roster.spawnDistance),
                maxAlive = Mathf.Max(1, roster.maxAlive)
            });

            foreach (PirateParty party in roster.parties)
            {
                if (party == null)
                    continue;
                DependsOn(party);
                int start = partyShips.Length;
                foreach (PirateParty.ShipEntry ship in party.ships)
                {
                    if (ship.prefab == null)
                        continue;
                    if (ship.prefab.GetComponent<Pirate_Authoring>() == null)
                        Debug.LogWarning($"Pirate party {party.name}: {ship.prefab.name} has no Pirate_Authoring, so it won't fly or fight.");
                    partyShips.Add(new PiratePartyShip
                    {
                        prefab = GetEntity(ship.prefab, TransformUsageFlags.Dynamic),
                        min = Mathf.Max(0, ship.min),
                        max = Mathf.Max(ship.min, ship.max)
                    });
                }
                if (partyShips.Length == start)
                    continue; // no ships: can't appear
                parties.Add(new PiratePartyDef
                {
                    name = party.name,
                    weight = Mathf.Max(0f, party.chance),
                    goal = party.goal,
                    aggression = Mathf.Clamp01(party.aggression),
                    lootGoal = Mathf.Max(0f, party.lootToLeave),
                    formation = party.formation,
                    spacing = Mathf.Max(0f, party.spacing),
                    shipStart = start,
                    shipCount = partyShips.Length - start
                });
            }
        }
    }
}
