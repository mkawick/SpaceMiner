using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

// Age of Empires style selection and orders.
//   Left-click a unit or building to select it; drag a box to select several units; Shift adds.
//   Right-click with workers selected: an asteroid = mine it, open space = move there.
//   With a factory selected, the panel at the bottom trains workers (or press T). Esc clears.
// Attaches itself when play starts.
public class SelectionController : MonoBehaviour
{
    public float dragThreshold = 6f;
    public float clickTolerance = 1.5f;

    private readonly List<Entity> selected = new List<Entity>();
    private bool dragging;
    private Vector2 dragStart;

    private Vector3 markerPosition;
    private float markerTime = -10f;
    private Color markerColour;

    private GUIStyle panelTitle, panelText, buttonStyle, panelSmall, iconLetter;
    private Rect panelRect;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachOnPlay()
    {
        if (FindAnyObjectByType<SelectionController>() == null)
            new GameObject("Selection Controller").AddComponent<SelectionController>();
    }

    private void Update()
    {
        if (!GameEcs.Ready(out EntityManager em))
            return;
        selected.RemoveAll(e => !em.Exists(e));

        Mouse mouse = Mouse.current;
        Keyboard keyboard = Keyboard.current;
        if (mouse == null || keyboard == null)
            return;

        bool menuOwnsClick = GameInput.BuildMenuBusy || GameInput.ClickConsumedFrame == Time.frameCount;
        if (menuOwnsClick)
        {
            dragging = false;
            return;
        }

        if (mouse.leftButton.wasPressedThisFrame && !GameInput.PointerOverUI())
        {
            dragging = true;
            dragStart = GameInput.MouseGui();
        }
        if (dragging && mouse.leftButton.wasReleasedThisFrame)
        {
            dragging = false;
            Vector2 end = GameInput.MouseGui();
            if (Vector2.Distance(end, dragStart) > dragThreshold)
                BoxSelect(em, Rect.MinMaxRect(Mathf.Min(dragStart.x, end.x), Mathf.Min(dragStart.y, end.y),
                                              Mathf.Max(dragStart.x, end.x), Mathf.Max(dragStart.y, end.y)));
            else
                ClickSelect(em);
        }

        if (mouse.rightButton.wasPressedThisFrame && !GameInput.PointerOverUI())
            IssueOrder(em);

        if (keyboard.tKey.wasPressedThisFrame)
            TrainFromSelectedFactory(em);
        if (keyboard.escapeKey.wasPressedThisFrame)
            selected.Clear();
    }

    private void ClickSelect(EntityManager em)
    {
        bool additive = GameInput.ShiftHeld();
        if (!GameInput.MouseToGround(out Vector3 ground))
            return;

        NativeArray<Entity> entities = GameEcs.SelectableQuery.ToEntityArray(Allocator.Temp);
        NativeArray<Selectable> selectables = GameEcs.SelectableQuery.ToComponentDataArray<Selectable>(Allocator.Temp);
        NativeArray<LocalTransform> transforms = GameEcs.SelectableQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        // Units win over buildings when both are under the cursor.
        Entity best = Entity.Null;
        float bestScore = float.MaxValue;
        for (int i = 0; i < entities.Length; i++)
        {
            float distance = Vector2.Distance(new Vector2(ground.x, ground.z),
                                              new Vector2(transforms[i].Position.x, transforms[i].Position.z));
            if (distance > selectables[i].radius + clickTolerance)
                continue;
            float score = distance + selectables[i].kind * 1000f;
            if (score < bestScore)
            {
                bestScore = score;
                best = entities[i];
            }
        }
        entities.Dispose();
        selectables.Dispose();
        transforms.Dispose();

        if (!additive)
            selected.Clear();
        if (best == Entity.Null)
            return;
        if (additive && selected.Contains(best))
            selected.Remove(best);
        else
            selected.Add(best);
    }

    private void BoxSelect(EntityManager em, Rect box)
    {
        if (!GameInput.ShiftHeld())
            selected.Clear();

        NativeArray<Entity> entities = GameEcs.SelectableQuery.ToEntityArray(Allocator.Temp);
        NativeArray<Selectable> selectables = GameEcs.SelectableQuery.ToComponentDataArray<Selectable>(Allocator.Temp);
        NativeArray<LocalTransform> transforms = GameEcs.SelectableQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);

        var buildingsInBox = new List<Entity>();
        bool anyUnit = false;
        for (int i = 0; i < entities.Length; i++)
        {
            if (!GameInput.WorldToGui(transforms[i].Position, out Vector2 p) || !box.Contains(p))
                continue;
            if (selectables[i].kind == 0)
            {
                anyUnit = true;
                if (!selected.Contains(entities[i]))
                    selected.Add(entities[i]);
            }
            else
            {
                buildingsInBox.Add(entities[i]);
            }
        }
        // Like AoE: a box picks units; it only picks buildings when no units are inside it.
        if (!anyUnit)
            foreach (Entity b in buildingsInBox)
                if (!selected.Contains(b))
                    selected.Add(b);

        entities.Dispose();
        selectables.Dispose();
        transforms.Dispose();
    }

    private void IssueOrder(EntityManager em)
    {
        var bots = new List<Entity>();
        foreach (Entity e in selected)
            if (em.HasComponent<WorkerBot>(e))
                bots.Add(e);
        if (bots.Count == 0 || !GameInput.MouseToGround(out Vector3 ground))
            return;

        Entity asteroid = AsteroidAt(ground);
        if (asteroid != Entity.Null)
        {
            foreach (Entity bot in bots)
            {
                WorkerBot w = em.GetComponentData<WorkerBot>(bot);
                w.state = BotState.Mining;
                w.target = asteroid;
                w.workSite = em.GetComponentData<LocalTransform>(asteroid).Position;
                w.hasWorkSite = true;
                w.fireTimer = 0f;
                em.SetComponentData(bot, w);
            }
            markerColour = new Color(1f, 0.6f, 0.15f, 1f);
            markerPosition = em.GetComponentData<LocalTransform>(asteroid).Position;
        }
        else
        {
            // Spread the group on a small grid around the clicked point.
            int columns = Mathf.CeilToInt(Mathf.Sqrt(bots.Count));
            const float spacing = 5f;
            for (int i = 0; i < bots.Count; i++)
            {
                float x = (i % columns - (columns - 1) * 0.5f) * spacing;
                float z = (i / columns - (columns - 1) * 0.5f) * spacing;
                WorkerBot w = em.GetComponentData<WorkerBot>(bots[i]);
                w.state = BotState.Moving;
                w.destination = new float3(ground.x + x, 0f, ground.z + z);
                em.SetComponentData(bots[i], w);
            }
            markerColour = new Color(0.3f, 1f, 0.45f, 1f);
            markerPosition = ground;
        }
        markerTime = Time.unscaledTime;
    }

    private static Entity AsteroidAt(Vector3 ground)
    {
        NativeArray<Entity> entities = GameEcs.AsteroidQuery.ToEntityArray(Allocator.Temp);
        NativeArray<LocalTransform> transforms = GameEcs.AsteroidQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        NativeArray<AsteroidHex> hexes = GameEcs.AsteroidQuery.ToComponentDataArray<AsteroidHex>(Allocator.Temp);
        Entity best = Entity.Null;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < entities.Length; i++)
        {
            float distance = Vector2.Distance(new Vector2(ground.x, ground.z),
                                              new Vector2(transforms[i].Position.x, transforms[i].Position.z));
            if (distance <= hexes[i].radius + 3f && distance < bestDistance)
            {
                bestDistance = distance;
                best = entities[i];
            }
        }
        entities.Dispose();
        transforms.Dispose();
        hexes.Dispose();
        return best;
    }

    private Entity SelectedFactory(EntityManager em)
    {
        if (selected.Count != 1 || !em.HasComponent<Building>(selected[0]))
            return Entity.Null;
        return em.GetComponentData<Building>(selected[0]).id == BuildingId.WorkerFactory ? selected[0] : Entity.Null;
    }

    private void TrainFromSelectedFactory(EntityManager em)
    {
        if (!BotConfig.Rules.Allows(UnitId.WorkerBot))
            return;
        Entity factory = SelectedFactory(em);
        if (factory == Entity.Null || !em.GetComponentData<Building>(factory).IsComplete)
            return;
        Entity request = em.CreateEntity();
        em.AddComponentData(request, new TrainRequest { building = factory, unit = UnitId.WorkerBot });
    }

    private void OnGUI()
    {
        if (!GameEcs.Ready(out EntityManager em))
            return;
        EnsureStyles();
        bool repaint = Event.current.type == EventType.Repaint;

        if (repaint)
        {
            GLDraw.Begin();
            foreach (Entity e in selected)
            {
                if (!em.Exists(e) || !em.HasComponent<Selectable>(e))
                    continue;
                Vector3 p = em.GetComponentData<LocalTransform>(e).Position;
                float r = em.GetComponentData<Selectable>(e).radius;
                GLDraw.WorldCircle(p, r + 0.8f, new Color(0.3f, 1f, 0.45f, 0.9f), 2f, r > 5f ? 48 : 24);
            }
            if (dragging)
            {
                Vector2 m = GameInput.MouseGui();
                Rect box = Rect.MinMaxRect(Mathf.Min(dragStart.x, m.x), Mathf.Min(dragStart.y, m.y),
                                           Mathf.Max(dragStart.x, m.x), Mathf.Max(dragStart.y, m.y));
                if (box.width > dragThreshold || box.height > dragThreshold)
                {
                    GLDraw.RectFill(box, new Color(0.3f, 1f, 0.45f, 0.08f));
                    GLDraw.RectOutline(box, new Color(0.3f, 1f, 0.45f, 0.8f));
                }
            }
            float age = Time.unscaledTime - markerTime;
            if (age < 0.6f)
            {
                Color c = markerColour;
                c.a = 1f - age / 0.6f;
                GLDraw.WorldCircle(markerPosition, 2f + age * 10f, c, 2f, 32);
            }
            GLDraw.End();
        }

        DrawPanel(em);
    }

    private void DrawPanel(EntityManager em)
    {
        CombatFaction faction = GameEcs.PlayerFaction;
        Entity storage = SelectedBuilding(em, BuildingId.Storage);
        if (storage != Entity.Null)
        {
            DrawStoragePanel(em, storage, faction);
            return;
        }
        Entity factory = SelectedFactory(em);
        int botCount = 0;
        foreach (Entity e in selected)
            if (em.Exists(e) && em.HasComponent<WorkerBot>(e))
                botCount++;
        if (factory == Entity.Null && botCount == 0)
            return;

        if (factory != Entity.Null)
        {
            DrawFactoryPanel(em, factory, faction);
            return;
        }

        float width = 420f, height = 58f;
        panelRect = new Rect((Screen.width - width) * 0.5f, Screen.height - height - 16f, width, height);
        GameInput.RegisterUiRect(panelRect);
        DrawPanelBackground(panelRect);
        float x = panelRect.x + 12f, y = panelRect.y + 8f, w = width - 24f;
        string botName = FactionNames.Name(UnitId.WorkerBot, faction);
        GUI.Label(new Rect(x, y, w, 20f), $"{botCount} × {botName} ({FactionNames.English(UnitId.WorkerBot)})", panelTitle);
        GUI.Label(new Rect(x, y + 22f, w, 18f), "Right-click an asteroid to mine it, or open space to move there.", panelText);
    }

    private static void DrawPanelBackground(Rect r)
    {
        GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.82f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = new Color(0.78f, 0.32f, 0.04f, 1f);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2f), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    // Icon colours (from the project palette) and letters for each unit type.
    private static Color UnitColour(UnitId unit)
    {
        switch (unit)
        {
            case UnitId.WorkerBot: return new Color(0xE1 / 255f, 0x76 / 255f, 0x33 / 255f);   // #E17633
            case UnitId.BuilderBot: return new Color(0xCC / 255f, 0xE8 / 255f, 0xCC / 255f);  // #CCE8CC
            case UnitId.DefenderBot: return new Color(0xCC / 255f, 0x8C / 255f, 0xC8 / 255f); // #CC8CC8
            case UnitId.AttackerBot: return new Color(0x67 / 255f, 0x13 / 255f, 0x51 / 255f); // #671351
            default: return new Color(0xF6 / 255f, 0xEF / 255f, 0xEE / 255f);                 // #F6EFEE
        }
    }

    private static string UnitLetter(UnitId unit)
    {
        switch (unit)
        {
            case UnitId.WorkerBot: return "M";
            case UnitId.BuilderBot: return "B";
            case UnitId.DefenderBot: return "D";
            case UnitId.AttackerBot: return "A";
            case UnitId.DefenceShip: return "DS";
            default: return "AS";
        }
    }

    private void DrawIcon(Rect r, UnitId unit, bool dim)
    {
        Color c = UnitColour(unit);
        if (dim) c = Color.Lerp(c, new Color(0.25f, 0.25f, 0.25f), 0.65f);
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
        float luminance = c.r * 0.3f + c.g * 0.59f + c.b * 0.11f;
        iconLetter.normal.textColor = luminance > 0.5f ? new Color(0.1f, 0.08f, 0.1f) : new Color(0.96f, 0.94f, 0.93f);
        iconLetter.fontSize = Mathf.Clamp((int)(r.height * 0.5f), 9, 18);
        GUI.Label(r, UnitLetter(unit), iconLetter);
    }

    // The factory panel: what it can build (with costs), its queue as icons, and its progress.
    // Click a unit to queue it (T queues a mining bot); click a waiting icon to remove it.
    private void DrawFactoryPanel(EntityManager em, Entity factory, CombatFaction faction)
    {
        Building building = em.GetComponentData<Building>(factory);
        GameEcs.TryGetCatalog(out BuildCatalog catalog);
        GameRules rules = BotConfig.Rules;
        Event e = Event.current;
        string tooltip = null;

        float width = 560f, height = building.IsComplete ? 178f : 72f;
        panelRect = new Rect((Screen.width - width) * 0.5f, Screen.height - height - 16f, width, height);
        GameInput.RegisterUiRect(panelRect);
        DrawPanelBackground(panelRect);
        float x = panelRect.x + 12f, y = panelRect.y + 8f, w = width - 24f;
        GUI.Label(new Rect(x, y, w, 20f), $"{FactionNames.Name(building.id, faction)}  ·  {FactionNames.English(building.id)}", panelTitle);

        if (!building.IsComplete)
        {
            float pct = building.progress / math.max(0.01f, building.buildTime);
            GUI.Label(new Rect(x, y + 24f, w, 18f), $"Under construction  {pct * 100f:0}%", panelText);
            DrawBar(new Rect(x, y + 46f, w, 8f), pct);
            return;
        }
        if (!em.HasBuffer<QueuedUnit>(factory))
            return;

        TrainQueue progress = em.GetComponentData<TrainQueue>(factory);
        DynamicBuffer<QueuedUnit> queue = em.GetBuffer<QueuedUnit>(factory, true);
        int queued = queue.Length;
        UnitId[] queueCopy = new UnitId[queued];
        for (int i = 0; i < queued; i++) queueCopy[i] = queue[i].unit;

        // Build buttons.
        y += 26f;
        float bw = (w - 3 * 8f) / 4f;
        for (int i = 0; i < UnitRules.FactoryUnits.Length; i++)
        {
            UnitId unit = UnitRules.FactoryUnits[i];
            bool implemented = UnitRules.Implemented(unit);
            bool allowed = rules.Allows(unit);
            bool usable = implemented && allowed && queued < UnitRules.MaxQueue;
            var r = new Rect(x + i * (bw + 8f), y, bw, 46f);
            bool hover = r.Contains(e.mousePosition);
            GUI.color = hover && usable ? new Color(0.2f, 0.26f, 0.32f, 1f) : new Color(0.12f, 0.14f, 0.17f, 1f);
            GUI.DrawTexture(r, Texture2D.whiteTexture);
            GUI.color = Color.white;
            DrawIcon(new Rect(r.x + 6f, r.y + 8f, 30f, 30f), unit, !usable);

            string cost = catalog.freeBuild ? "free (testing)" : GameEcs.CostText(catalog.UnitCost(unit));
            string state = !implemented ? "planned" : !allowed ? "off (Bot Config)" : cost;
            panelSmall.normal.textColor = usable ? Color.white : new Color(0.55f, 0.55f, 0.55f);
            panelSmall.fontStyle = FontStyle.Bold;
            GUI.Label(new Rect(r.x + 42f, r.y + 6f, r.width - 46f, 16f), FactionNames.Name(unit, faction) + (unit == UnitId.WorkerBot ? "  [T]" : ""), panelSmall);
            panelSmall.fontStyle = FontStyle.Normal;
            panelSmall.normal.textColor = usable ? new Color(0.8f, 0.8f, 0.8f) : new Color(0.5f, 0.5f, 0.5f);
            GUI.Label(new Rect(r.x + 42f, r.y + 24f, r.width - 46f, 16f), state, panelSmall);

            if (hover)
                tooltip = $"{FactionNames.Name(unit, faction)} ({FactionNames.English(unit)})\n{cost}  ·  {catalog.UnitTime(unit):0.#} s" + (implemented ? "" : "\nNot in the game yet");
            if (hover && usable && e.type == EventType.MouseDown && e.button == 0)
            {
                Entity request = em.CreateEntity();
                em.AddComponentData(request, new TrainRequest { building = factory, unit = unit });
                e.Use();
            }
        }

        // Queue.
        y += 56f;
        GUI.Label(new Rect(x, y + 8f, 80f, 18f), $"Queue {queued}/{UnitRules.MaxQueue}", panelText);
        float slot = 36f, qx = x + 86f;
        for (int i = 0; i < UnitRules.MaxQueue; i++)
        {
            var r = new Rect(qx + i * (slot + 6f), y, slot, slot);
            if (i >= queued)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.07f);
                GUI.DrawTexture(r, Texture2D.whiteTexture);
                GUI.color = Color.white;
                continue;
            }
            UnitId unit = queueCopy[i];
            bool current = i == 0;
            DrawIcon(r, unit, false);
            if (current)
            {
                if (progress.stalled)
                {
                    GUI.color = new Color(0.8f, 0.1f, 0.1f, 0.45f);
                    GUI.DrawTexture(r, Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
                float time = math.max(0.01f, catalog.UnitTime(unit));
                DrawBar(new Rect(r.x, r.yMax + 2f, r.width, 4f), progress.progress / time);
            }
            bool hover = r.Contains(e.mousePosition);
            if (hover)
            {
                GUI.color = Color.white;
                GUI.DrawTexture(new Rect(r.x, r.y, r.width, 2f), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(r.x, r.yMax - 2f, r.width, 2f), Texture2D.whiteTexture);
                string cost = catalog.freeBuild ? "free (testing)" : GameEcs.CostText(catalog.UnitCost(unit));
                tooltip = $"{FactionNames.Name(unit, faction)} ({FactionNames.English(unit)})\n{cost}"
                          + (current ? (progress.stalled ? "\nWaiting for resources" : "\nBuilding now") : "\nClick to remove");
            }
            if (hover && !current && e.type == EventType.MouseDown && e.button == 0)
            {
                Entity request = em.CreateEntity();
                em.AddComponentData(request, new QueueRemoveRequest { building = factory, index = i });
                e.Use();
            }
        }

        // Status.
        y += slot + 12f;
        string status = queued == 0 ? "Idle: click a unit to queue it."
            : progress.stalled ? (rules.Allows(queueCopy[0]) ? $"Stalled: waiting for {GameEcs.CostText(catalog.UnitCost(queueCopy[0]))}" : "Stalled: switched off in Bot Config")
            : $"Building {FactionNames.Name(queueCopy[0], faction)}";
        panelText.normal.textColor = progress.stalled && queued > 0 ? new Color(1f, 0.55f, 0.5f) : new Color(0.85f, 0.85f, 0.85f);
        GUI.Label(new Rect(x, y, w - 120f, 18f), status, panelText);
        panelText.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
        GUI.Label(new Rect(x + w - 110f, y, 110f, 18f), $"Built: {progress.trained}", panelText);

        if (tooltip != null)
        {
            Vector2 m = e.mousePosition;
            var tip = new Rect(m.x + 14f, m.y - 54f, 230f, 50f);
            GUI.color = new Color(0f, 0f, 0f, 0.88f);
            GUI.DrawTexture(tip, Texture2D.whiteTexture);
            GUI.color = Color.white;
            panelSmall.normal.textColor = Color.white;
            GUI.Label(new Rect(tip.x + 6f, tip.y + 3f, tip.width - 12f, tip.height - 6f), tooltip, panelSmall);
        }
    }

    private Entity SelectedBuilding(EntityManager em, BuildingId id)
    {
        if (selected.Count != 1 || !em.Exists(selected[0]) || !em.HasComponent<Building>(selected[0]))
            return Entity.Null;
        return em.GetComponentData<Building>(selected[0]).id == id ? selected[0] : Entity.Null;
    }

    private void DrawStoragePanel(EntityManager em, Entity storage, CombatFaction faction)
    {
        Building building = em.GetComponentData<Building>(storage);
        float width = 420f, height = 76f;
        panelRect = new Rect((Screen.width - width) * 0.5f, Screen.height - height - 16f, width, height);
        GameInput.RegisterUiRect(panelRect);
        GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.82f);
        GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
        GUI.color = new Color(0.78f, 0.32f, 0.04f, 1f);
        GUI.DrawTexture(new Rect(panelRect.x, panelRect.y, panelRect.width, 2f), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float x = panelRect.x + 12f, y = panelRect.y + 8f, w = width - 24f;
        GUI.Label(new Rect(x, y, w, 20f), $"{FactionNames.Name(building.id, faction)}  ·  {FactionNames.English(building.id)}", panelTitle);
        if (!building.IsComplete)
        {
            float pct = building.progress / math.max(0.01f, building.buildTime);
            GUI.Label(new Rect(x, y + 24f, w, 18f), $"Under construction  {pct * 100f:0}%", panelText);
            DrawBar(new Rect(x, y + 46f, w, 8f), pct);
            return;
        }
        if (GUI.Button(new Rect(x, y + 28f, 200f, 30f), "Open storage", buttonStyle))
            StorageScreen.Open();
        GUI.Label(new Rect(x + 210f, y + 30f, w - 210f, 36f), "Or right-click the cargo bars.", panelText);
    }

    private static void DrawBar(Rect r, float fraction)
    {
        GUI.color = new Color(1f, 1f, 1f, 0.12f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = new Color(0.3f, 1f, 0.45f, 0.9f);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width * Mathf.Clamp01(fraction), r.height), Texture2D.whiteTexture);
        GUI.color = Color.white;
    }

    private void EnsureStyles()
    {
        if (panelTitle != null)
            return;
        panelTitle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
        panelTitle.normal.textColor = Color.white;
        panelText = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        panelText.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
        buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 13 };
        panelSmall = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
        iconLetter = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
    }
}
