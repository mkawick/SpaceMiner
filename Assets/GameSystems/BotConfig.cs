using Unity.Collections;
using Unity.Entities;
using UnityEngine;

// Testing switches for the whole game: which kinds of units and buildings are available and
// active, pirates, and the test hotkeys. Add it to the GameSession object in the game scene.
// Changes take effect immediately, also in Play mode.
//
// A unit type that's switched off can't be built or trained, and the ones already in the world
// freeze where they are. Use the ⋮ menu on this component to remove existing pirates or bots.
// Entries marked (planned) are for unit types that don't exist yet; their switches are ready.
[DisallowMultipleComponent]
public class BotConfig : MonoBehaviour
{
    [Header("Pirates")]
    public bool pirates = true;
    [Tooltip("F9 sends a pirate wave")]
    public bool pirateHotkey = true;
    [Tooltip("Use the timing below instead of the Pirate Roster's")]
    public bool overridePirateTiming = true;
    [Tooltip("Seconds of peace before the first wave")]
    [Min(0f)] public float firstWaveDelay = 600f;
    [Tooltip("Seconds between waves after that")]
    [Min(5f)] public float waveInterval = 300f;

    [Header("Your bots and ships")]
    [Tooltip("Master switch for all of the player's bots and ships")]
    public bool allBots = true;
    [Tooltip("Mining bots (Rudar / Schürfer / Grävare), trained at the Worker Factory")]
    public bool miners = true;
    [Tooltip("(planned) Builder bots (Graditelj / Baumeister / Byggare)")]
    public bool builders = true;
    [Tooltip("(planned) Defender bots (Čuvar / Verteidiger / Försvarare)")]
    public bool defenders = true;
    [Tooltip("(planned) Attacker bots: raid other factions, follow you into combat, fly in formation (Napadač / Angreifer / Anfallare)")]
    public bool attackers = true;
    [Tooltip("(planned) Defence ships (Stražar / Wächter / Väktare)")]
    public bool defenceShips = true;
    [Tooltip("(planned) Attack ships (Jurišnik / Stürmer / Stormare)")]
    public bool attackShips = true;

    [Header("Buildings")]
    public bool workerFactory = true;
    public bool storageDepot = true;

    [Header("Test tools")]
    [Tooltip("F8 spawns hostile training drones")]
    public bool trainingDrones = true;

    private static BotConfig current;
    private World world;
    private EntityQuery rulesQuery;

    // The rules the UI should follow (everything on when there's no Bot Config).
    public static GameRules Rules => current != null ? current.Build() : GameRules.Default;

    private void OnEnable() => current = this;

    private void OnDisable()
    {
        if (current == this)
            current = null;
    }

    public GameRules Build()
    {
        return new GameRules
        {
            pirates = pirates,
            pirateHotkey = pirateHotkey,
            pirateFirstWave = overridePirateTiming ? firstWaveDelay : -1f,
            pirateWaveInterval = overridePirateTiming ? waveInterval : -1f,
            allBots = allBots,
            miners = miners,
            builders = builders,
            defenders = defenders,
            attackers = attackers,
            defenceShips = defenceShips,
            attackShips = attackShips,
            workerFactory = workerFactory,
            storageDepot = storageDepot,
            trainingDrones = trainingDrones
        };
    }

    private void Update()
    {
        World w = World.DefaultGameObjectInjectionWorld;
        if (w == null || !w.IsCreated)
            return;
        EntityManager em = w.EntityManager;
        if (world != w)
        {
            rulesQuery = em.CreateEntityQuery(ComponentType.ReadWrite<GameRules>());
            world = w;
        }
        if (rulesQuery.IsEmpty)
            em.CreateEntity(typeof(GameRules));
        rulesQuery.SetSingleton(Build());
    }

    [ContextMenu("Remove all pirates")]
    private void RemovePirates() => RemoveAll(ComponentType.ReadOnly<PirateAI>(), "pirate");

    [ContextMenu("Remove all mining bots")]
    private void RemoveMiners() => RemoveAll(ComponentType.ReadOnly<WorkerBot>(), "mining bot");

    [ContextMenu("Remove all training drones")]
    private void RemoveDrones() => RemoveAll(ComponentType.ReadOnly<TrainingDrone>(), "training drone");

    private static void RemoveAll(ComponentType type, string what)
    {
        World w = World.DefaultGameObjectInjectionWorld;
        if (!Application.isPlaying || w == null || !w.IsCreated)
        {
            Debug.Log("Bot Config: removing only works in Play mode.");
            return;
        }
        EntityManager em = w.EntityManager;
        using EntityQuery query = em.CreateEntityQuery(type);
        using NativeArray<Entity> entities = query.ToEntityArray(Allocator.Temp);
        foreach (Entity e in entities)
            em.AddComponent<DestroyedEntityTag_Dots>(e);
        Debug.Log($"Bot Config: removed {entities.Length} {what}{(entities.Length == 1 ? "" : "s")}.");
    }
}
