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

    private GUIStyle panelTitle, panelText, buttonStyle;
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
        Entity factory = SelectedFactory(em);
        if (factory == Entity.Null || !em.GetComponentData<Building>(factory).IsComplete)
            return;
        Entity request = em.CreateEntity();
        em.AddComponentData(request, new TrainRequest { building = factory });
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
        Entity factory = SelectedFactory(em);
        int botCount = 0;
        foreach (Entity e in selected)
            if (em.Exists(e) && em.HasComponent<WorkerBot>(e))
                botCount++;
        if (factory == Entity.Null && botCount == 0)
            return;

        float width = 420f, height = factory != Entity.Null ? 104f : 58f;
        panelRect = new Rect((Screen.width - width) * 0.5f, Screen.height - height - 16f, width, height);
        GameInput.RegisterUiRect(panelRect);
        GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.82f);
        GUI.DrawTexture(panelRect, Texture2D.whiteTexture);
        GUI.color = new Color(0.78f, 0.32f, 0.04f, 1f);
        GUI.DrawTexture(new Rect(panelRect.x, panelRect.y, panelRect.width, 2f), Texture2D.whiteTexture);
        GUI.color = Color.white;

        float x = panelRect.x + 12f, y = panelRect.y + 8f, w = width - 24f;
        if (factory != Entity.Null)
        {
            Building building = em.GetComponentData<Building>(factory);
            TrainQueue queue = em.GetComponentData<TrainQueue>(factory);
            GameEcs.TryGetCatalog(out BuildCatalog catalog);
            string botName = FactionNames.Name(UnitId.WorkerBot, faction);

            GUI.Label(new Rect(x, y, w, 20f), $"{FactionNames.Name(building.id, faction)}  ·  {FactionNames.English(building.id)}", panelTitle);
            if (!building.IsComplete)
            {
                float pct = building.progress / math.max(0.01f, building.buildTime);
                GUI.Label(new Rect(x, y + 24f, w, 18f), $"Under construction  {pct * 100f:0}%", panelText);
                DrawBar(new Rect(x, y + 46f, w, 8f), pct);
                return;
            }

            string cost = catalog.freeBuild ? "free (testing)" : GameEcs.CostText(catalog.workerBotCost);
            if (GUI.Button(new Rect(x, y + 26f, 230f, 30f), $"Train {botName}  [T]", buttonStyle))
                TrainFromSelectedFactory(em);
            GUI.Label(new Rect(x + 240f, y + 26f, w - 240f, 18f), cost, panelText);
            GUI.Label(new Rect(x + 240f, y + 42f, w - 240f, 18f), $"Queued: {queue.queued}   Trained: {queue.trained}", panelText);
            float trainTime = catalog.freeBuild ? 0.5f : catalog.workerBotTrainTime;
            DrawBar(new Rect(x, y + 66f, w, 8f), queue.queued > 0 ? queue.progress / math.max(0.01f, trainTime) : 0f);
        }
        else
        {
            string botName = FactionNames.Name(UnitId.WorkerBot, faction);
            GUI.Label(new Rect(x, y, w, 20f), $"{botCount} × {botName} ({FactionNames.English(UnitId.WorkerBot)})", panelTitle);
            GUI.Label(new Rect(x, y + 22f, w, 18f), "Right-click an asteroid to mine it, or open space to move there.", panelText);
        }
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
    }
}
