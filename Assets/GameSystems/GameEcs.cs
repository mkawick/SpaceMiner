using Unity.Collections;
using Unity.Entities;
using Unity.Transforms;

// Main-thread access to the ECS world for UI scripts. Queries are recreated when the world changes
// (entering play mode) and belong to the world, so they are freed with it.
public static class GameEcs
{
    private static World world;
    private static EntityQuery playerQuery;
    private static EntityQuery configQuery;
    private static EntityQuery catalogQuery;
    private static EntityQuery selectableQuery;
    private static EntityQuery asteroidQuery;
    private static EntityQuery botQuery;
    private static EntityQuery buildingQuery;

    public static bool Ready(out EntityManager em)
    {
        em = default;
        World w = World.DefaultGameObjectInjectionWorld;
        if (w == null || !w.IsCreated)
            return false;
        if (w != world)
        {
            world = w;
            EntityManager m = w.EntityManager;
            playerQuery = m.CreateEntityQuery(ComponentType.ReadOnly<InputData_Dots>(), ComponentType.ReadOnly<LocalTransform>(), ComponentType.ReadOnly<CargoItem>());
            configQuery = m.CreateEntityQuery(ComponentType.ReadOnly<AsteroidFieldConfig>());
            catalogQuery = m.CreateEntityQuery(ComponentType.ReadOnly<BuildCatalog>());
            selectableQuery = m.CreateEntityQuery(ComponentType.ReadOnly<Selectable>(), ComponentType.ReadOnly<LocalTransform>());
            asteroidQuery = new EntityQueryBuilder(Allocator.Temp)
                .WithAll<AsteroidHex, LocalTransform>()
                .WithNone<DestroyedEntityTag_Dots>()
                .Build(m);
            botQuery = m.CreateEntityQuery(ComponentType.ReadOnly<WorkerBot>());
            buildingQuery = m.CreateEntityQuery(ComponentType.ReadOnly<Building>(), ComponentType.ReadOnly<LocalTransform>());
        }
        em = w.EntityManager;
        return true;
    }

    public static EntityQuery SelectableQuery => selectableQuery;
    public static EntityQuery AsteroidQuery => asteroidQuery;
    public static EntityQuery BotQuery => botQuery;
    public static EntityQuery BuildingQuery => buildingQuery;

    public static bool TryGetPlayer(out Entity player, out LocalTransform transform)
    {
        player = Entity.Null;
        transform = default;
        if (!Ready(out EntityManager em) || playerQuery.CalculateEntityCount() != 1)
            return false;
        player = playerQuery.GetSingletonEntity();
        transform = em.GetComponentData<LocalTransform>(player);
        return true;
    }

    public static bool TryGetConfig(out AsteroidFieldConfig config)
    {
        config = default;
        if (!Ready(out _) || configQuery.CalculateEntityCount() != 1)
            return false;
        config = configQuery.GetSingleton<AsteroidFieldConfig>();
        return true;
    }

    public static bool TryGetCatalog(out BuildCatalog catalog)
    {
        catalog = default;
        if (!Ready(out _) || catalogQuery.CalculateEntityCount() != 1)
            return false;
        catalog = catalogQuery.GetSingleton<BuildCatalog>();
        return true;
    }

    public static CombatFaction PlayerFaction => TryGetConfig(out AsteroidFieldConfig c) ? c.playerFaction : CombatFaction.None;

    // Short cost text such as "60 Fe  40 Si  20 Cu".
    public static string CostText(ResourceCost cost)
    {
        string[] codes = { "Fe", "Cu", "Si", "Ice", "C", "Au", "Fm" };
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < codes.Length; i++)
        {
            float amount = cost.Get(i);
            if (amount <= 0f) continue;
            if (sb.Length > 0) sb.Append("  ");
            sb.Append(amount.ToString("0")).Append(' ').Append(codes[i]);
        }
        return sb.Length > 0 ? sb.ToString() : "free";
    }
}
