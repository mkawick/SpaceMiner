using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

// Health bars over targetable things, and the player's target.
//   Bars show over every ship, bot and building near the player, and over asteroids once they're
//   damaged, under the mouse, or targeted. Green = friendly, red = hostile, amber = neutral.
//   Tab cycles through nearby targets (hostiles first, never friendlies); left-click on anything
//   that isn't friendly targets it. What the ship may target comes from the Layer Matrix. The target gets a ring and a panel at the top of the screen.
//   F8 (testing) spawns three hostile training drones in front of the ship.
public class TargetBars : MonoBehaviour
{
    public float showRange = 300f;      // bars only within this distance of the player
    public float targetRange = 1000f;   // Tab picks targets within this distance
    public int maxBars = 200;

    private struct Bar
    {
        public Entity entity;
        public Vector3 position;
        public float radius;
        public float fraction;
        public Color colour;
    }

    private readonly List<Bar> bars = new List<Bar>();
    private Entity hovered = Entity.Null;
    private Entity target = Entity.Null;
    private Vector3 targetPosition;
    private float targetRadius;
    private string targetName = "";
    private float targetHealth, targetMax;
    private Color targetColour;
    private EntityQuery targetQuery;
    private EntityQuery matrixQuery;
    private World queryWorld;

    private static readonly Color Friendly = new Color(0.36f, 0.85f, 0.48f, 1f);
    private static readonly Color Hostile = new Color(0.95f, 0.33f, 0.3f, 1f);
    private static readonly Color Neutral = new Color(0.95f, 0.68f, 0.25f, 1f);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindAnyObjectByType<TargetBars>() == null)
            new GameObject("Target Bars").AddComponent<TargetBars>();
    }

    private void Update()
    {
        bars.Clear();
        hovered = Entity.Null;
        if (!GameEcs.Ready(out EntityManager em) || !GameEcs.TryGetPlayer(out Entity player, out LocalTransform playerTransform))
            return;
        if (queryWorld != em.World)
        {
            targetQuery = em.CreateEntityQuery(
                ComponentType.ReadOnly<Targetable>(),
                ComponentType.ReadOnly<Health>(),
                ComponentType.ReadOnly<LocalTransform>(),
                ComponentType.Exclude<DestroyedEntityTag_Dots>());
            matrixQuery = em.CreateEntityQuery(ComponentType.ReadOnly<LayerMatrix>());
            queryWorld = em.World;
        }

        Targetable self = em.HasComponent<Targetable>(player) ? em.GetComponentData<Targetable>(player) : new Targetable { layer = CollisionLayer.Ship };
        CombatFaction us = self.faction != CombatFaction.None ? self.faction : GameEcs.PlayerFaction;
        CollisionLayer myLayer = self.layer;
        LayerMatrix matrix = matrixQuery.CalculateEntityCount() == 1 ? matrixQuery.GetSingleton<LayerMatrix>() : LayerMatrix.Default();
        if (em.HasComponent<PlayerTarget>(player))
            target = em.GetComponentData<PlayerTarget>(player).target;

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.f8Key.wasPressedThisFrame)
            SpawnTrainingDrones(em, playerTransform, us);

        using NativeArray<Entity> entities = targetQuery.ToEntityArray(Allocator.Temp);
        using NativeArray<Targetable> targetables = targetQuery.ToComponentDataArray<Targetable>(Allocator.Temp);
        using NativeArray<Health> healths = targetQuery.ToComponentDataArray<Health>(Allocator.Temp);
        using NativeArray<LocalTransform> transforms = targetQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        // What's under the mouse.
        bool overUi = GameInput.PointerOverUI() || GameInput.BuildMenuBusy;
        float2 mouse = float2.zero;
        bool mouseOnGround = GameInput.MouseToGround(out Vector3 ground);
        bool haveMouse = !overUi && mouseOnGround;
        if (haveMouse)
            mouse = new float2(ground.x, ground.z);
        float hoverBest = float.MaxValue;

        float2 me = playerTransform.Position.xz;
        bool targetAlive = false;
        var candidates = new List<(Entity entity, float distance, bool hostile)>();

        for (int i = 0; i < entities.Length; i++)
        {
            Targetable tg = targetables[i];
            float2 p = transforms[i].Position.xz;
            float distance = math.distance(p, me);
            bool friendly = Combat.IsFriendly(tg.faction, us);

            if (haveMouse)
            {
                float d = math.distance(p, mouse);
                if (d <= tg.radius + 1f && d < hoverBest)
                {
                    hoverBest = d;
                    hovered = entities[i];
                }
            }

            if (entities[i] == target)
            {
                targetAlive = true;
                targetPosition = transforms[i].Position;
                targetRadius = tg.radius;
                targetHealth = healths[i].current;
                targetMax = healths[i].max;
                targetColour = Colour(tg.faction, us);
                targetName = NameOf(em, entities[i], tg);
            }

            if (!friendly && entities[i] != player && distance <= targetRange && matrix.CanTarget(myLayer, tg.layer))
                candidates.Add((entities[i], distance, tg.faction != CombatFaction.None));

            if (distance > showRange || bars.Count >= maxBars)
                continue;
            bool damaged = healths[i].current < healths[i].max - 0.001f;
            bool show = tg.layer != CollisionLayer.Asteroid || damaged || entities[i] == target;
            if (!show)
                continue;
            bars.Add(new Bar
            {
                entity = entities[i],
                position = transforms[i].Position,
                radius = tg.radius,
                fraction = healths[i].Fraction,
                colour = entities[i] == player ? Friendly : Colour(tg.faction, us)
            });
        }

        // The hovered thing gets a bar even if it wouldn't otherwise.
        if (hovered != Entity.Null && !bars.Exists(b => b.entity == hovered))
        {
            Targetable tg = em.GetComponentData<Targetable>(hovered);
            Health h = em.GetComponentData<Health>(hovered);
            bars.Add(new Bar
            {
                entity = hovered,
                position = em.GetComponentData<LocalTransform>(hovered).Position,
                radius = tg.radius,
                fraction = h.Fraction,
                colour = Colour(tg.faction, us)
            });
        }

        Entity newTarget = targetAlive ? target : Entity.Null;

        // Tab: next target by distance, hostiles before neutral rocks.
        if (keyboard != null && keyboard.tabKey.wasPressedThisFrame && candidates.Count > 0)
        {
            candidates.Sort((a, b) => a.hostile != b.hostile ? (a.hostile ? -1 : 1) : a.distance.CompareTo(b.distance));
            int index = candidates.FindIndex(c => c.entity == newTarget);
            newTarget = candidates[(index + 1) % candidates.Count].entity;
        }

        // Left-click on something that isn't friendly targets it.
        Mouse m = Mouse.current;
        if (m != null && m.leftButton.wasPressedThisFrame && hovered != Entity.Null
            && Time.frameCount != GameInput.ClickConsumedFrame)
        {
            Targetable tg = em.GetComponentData<Targetable>(hovered);
            if (!Combat.IsFriendly(tg.faction, us) && hovered != player && matrix.CanTarget(myLayer, tg.layer))
                newTarget = hovered;
        }

        if (newTarget != target && em.HasComponent<PlayerTarget>(player))
            em.SetComponentData(player, new PlayerTarget { target = newTarget });
        if (newTarget != target && newTarget != Entity.Null)
        {
            Targetable tg = em.GetComponentData<Targetable>(newTarget);
            Health h = em.GetComponentData<Health>(newTarget);
            targetPosition = em.GetComponentData<LocalTransform>(newTarget).Position;
            targetRadius = tg.radius;
            targetHealth = h.current;
            targetMax = h.max;
            targetColour = Colour(tg.faction, us);
            targetName = NameOf(em, newTarget, tg);
        }
        target = newTarget;
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint)
            return;

        GLDraw.Begin();
        foreach (Bar bar in bars)
        {
            Vector3 top = bar.position + Vector3.up * (bar.radius * 1.2f + 1.5f);
            if (!GameInput.WorldToGui(top, out Vector2 g))
                continue;
            float width = Mathf.Clamp(bar.radius * 8f, 36f, 90f);
            var back = new Rect(g.x - width * 0.5f - 1f, g.y - 12f, width + 2f, 7f);
            GLDraw.RectFill(back, new Color(0f, 0f, 0f, 0.65f));
            GLDraw.RectFill(new Rect(back.x + 1f, back.y + 1f, width * bar.fraction, 5f), bar.colour);
            if (bar.entity == target || bar.entity == hovered)
                GLDraw.RectOutline(back, new Color(1f, 1f, 1f, 0.8f));
        }
        if (target != Entity.Null)
            GLDraw.WorldCircle(targetPosition, targetRadius + 1.5f, targetColour, 2f, 40, true);
        GLDraw.End();

        if (target != Entity.Null)
        {
            var panel = new Rect((Screen.width - 260f) * 0.5f, 12f, 260f, 46f);
            GameInput.RegisterUiRect(panel);
            GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.8f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = targetColour;
            GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 2f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(panel.x + 10f, panel.y + 5f, panel.width - 20f, 20f), "Target: " + targetName);
            float fraction = targetMax > 0f ? Mathf.Clamp01(targetHealth / targetMax) : 0f;
            var barRect = new Rect(panel.x + 10f, panel.y + 28f, panel.width - 80f, 8f);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(barRect, Texture2D.whiteTexture);
            GUI.color = targetColour;
            GUI.DrawTexture(new Rect(barRect.x, barRect.y, barRect.width * fraction, barRect.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(barRect.xMax + 8f, panel.y + 22f, 70f, 20f), $"{Mathf.CeilToInt(targetHealth)}/{Mathf.CeilToInt(targetMax)}");
        }
    }

    private static Color Colour(CombatFaction faction, CombatFaction us)
    {
        if (faction == CombatFaction.None) return Neutral;
        return Combat.IsFriendly(faction, us) ? Friendly : Hostile;
    }

    private static string NameOf(EntityManager em, Entity e, Targetable tg)
    {
        if (em.HasComponent<AsteroidResource>(e))
            return "Asteroid · " + ResourceInfo.Name(em.GetComponentData<AsteroidResource>(e).type);
        if (em.HasComponent<Building>(e))
        {
            Building b = em.GetComponentData<Building>(e);
            return FactionNames.Name(b.id, b.faction);
        }
        if (em.HasComponent<WorkerBot>(e))
            return FactionNames.Name(UnitId.WorkerBot, tg.faction);
        if (em.HasComponent<TrainingDrone>(e))
            return "Training drone";
        return tg.layer.ToString();
    }

    // Testing: three hostile, motionless drones 60 units ahead, using the worker bot model.
    private static void SpawnTrainingDrones(EntityManager em, LocalTransform player, CombatFaction us)
    {
        using EntityQuery prefabQuery = em.CreateEntityQuery(ComponentType.ReadOnly<WorkerBotPrefab>());
        if (prefabQuery.CalculateEntityCount() != 1)
        {
            Debug.LogWarning("Training drones need the Build Catalog (it holds the worker bot prefab).");
            return;
        }
        Entity prefab = prefabQuery.GetSingleton<WorkerBotPrefab>().prefab;
        if (prefab == Entity.Null)
            return;

        CombatFaction enemy = us == CombatFaction.Grix ? CombatFaction.FairwayDominion : CombatFaction.Grix;
        float3 forward = math.normalizesafe(new float3(player.Forward().x, 0f, player.Forward().z), new float3(0f, 0f, 1f));
        float3 right = math.cross(math.up(), forward);
        for (int i = -1; i <= 1; i++)
        {
            Entity drone = em.Instantiate(prefab);
            float3 position = player.Position + forward * 60f + right * (i * 12f);
            em.SetComponentData(drone, LocalTransform.FromPositionRotation(position, quaternion.LookRotationSafe(-forward, math.up())));
            Combat.ApplyTargetable(em, drone, new Targetable { layer = CollisionLayer.Ship, radius = 2.5f, density = 0f }, 60f, enemy);
            em.AddComponent<TrainingDrone>(drone);
        }
    }
}
