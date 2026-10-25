using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

// Hold B: a ring of buildings appears around the cursor. Release over one to pick it, then a
// ghost follows the mouse: green where it can go, red where it can't. Left-click to place
// (hold Shift to place several), right-click or Esc to cancel. Attaches itself when play starts.
[DefaultExecutionOrder(-10)]
public class RadialBuildMenu : MonoBehaviour
{
    public float ringRadius = 110f;
    public float slotRadius = 34f;
    public float deadZone = 30f;

    private enum Mode { None, Radial, Placing }

    private struct Slot
    {
        public string title;
        public string subtitle;
        public bool enabled;
        public BuildingId id;
    }

    private Mode mode;
    private Vector2 menuCentre;
    private int hovered = -1;
    private Slot[] slots;
    private BuildingId placingId;

    private Vector3 ghostPosition;
    private bool ghostOnGround;
    private bool ghostValid;
    private string ghostProblem;

    private Vector2[] asteroidPositions = new Vector2[0];
    private float[] asteroidRadii = new float[0];
    private int asteroidCount;
    private float nextAsteroidRefresh;

    private GUIStyle titleStyle, smallStyle, tipStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachOnPlay()
    {
        if (FindAnyObjectByType<RadialBuildMenu>() == null)
            new GameObject("Radial Build Menu").AddComponent<RadialBuildMenu>();
    }

    private void BuildSlots()
    {
        CombatFaction faction = GameEcs.PlayerFaction;
        slots = new[]
        {
            new Slot { title = FactionNames.Name(BuildingId.WorkerFactory, faction), subtitle = FactionNames.English(BuildingId.WorkerFactory), enabled = true, id = BuildingId.WorkerFactory },
            new Slot { title = "Station", subtitle = "coming soon" },
            new Slot { title = "Refinery", subtitle = "coming soon" },
            new Slot { title = "Defense Tower", subtitle = "coming soon" },
            new Slot { title = "Radar", subtitle = "coming soon" },
        };
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        Mouse mouse = Mouse.current;
        if (keyboard == null || mouse == null)
            return;

        switch (mode)
        {
            case Mode.None:
                if (keyboard.bKey.wasPressedThisFrame && GameEcs.TryGetCatalog(out _))
                {
                    BuildSlots();
                    mode = Mode.Radial;
                    Vector2 m = GameInput.MouseGui();
                    float edge = ringRadius + slotRadius + 30f;
                    menuCentre = new Vector2(Mathf.Clamp(m.x, edge, Screen.width - edge), Mathf.Clamp(m.y, edge, Screen.height - edge));
                }
                break;

            case Mode.Radial:
                hovered = SlotUnderPointer();
                if (keyboard.escapeKey.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
                {
                    mode = Mode.None;
                    GameInput.ClickConsumedFrame = Time.frameCount;
                }
                else if (keyboard.bKey.wasReleasedThisFrame)
                {
                    if (hovered >= 0 && slots[hovered].enabled)
                    {
                        placingId = slots[hovered].id;
                        mode = Mode.Placing;
                    }
                    else
                    {
                        mode = Mode.None;
                    }
                }
                break;

            case Mode.Placing:
                UpdateGhost();
                if (keyboard.escapeKey.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame)
                {
                    mode = Mode.None;
                    GameInput.ClickConsumedFrame = Time.frameCount;
                }
                else if (mouse.leftButton.wasPressedThisFrame && !GameInput.PointerOverUI())
                {
                    GameInput.ClickConsumedFrame = Time.frameCount;
                    if (ghostValid)
                    {
                        RequestBuilding();
                        if (!GameInput.ShiftHeld())
                            mode = Mode.None;
                    }
                }
                break;
        }

        GameInput.BuildMenuBusy = mode != Mode.None;
    }

    private int SlotUnderPointer()
    {
        Vector2 d = GameInput.MouseGui() - menuCentre;
        if (d.magnitude < deadZone)
            return -1;
        int best = -1;
        float bestDistance = float.MaxValue;
        for (int i = 0; i < slots.Length; i++)
        {
            float distance = Vector2.Distance(GameInput.MouseGui(), SlotCentre(i));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    private Vector2 SlotCentre(int i)
    {
        float angle = -Mathf.PI * 0.5f + i * Mathf.PI * 2f / slots.Length; // first slot at the top
        return menuCentre + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * ringRadius;
    }

    private void UpdateGhost()
    {
        ghostOnGround = GameInput.MouseToGround(out ghostPosition);
        ghostValid = false;
        ghostProblem = null;
        if (!ghostOnGround)
            return;
        if (!GameEcs.Ready(out EntityManager em) || !GameEcs.TryGetCatalog(out BuildCatalog catalog) ||
            !GameEcs.TryGetPlayer(out Entity player, out LocalTransform ship))
        {
            ghostProblem = "Waiting for the game to load";
            return;
        }

        float radius = catalog.workerFactoryRadius;
        Vector2 at = new Vector2(ghostPosition.x, ghostPosition.z);

        if (Vector2.Distance(at, new Vector2(ship.Position.x, ship.Position.z)) > catalog.placementRange)
        {
            ghostProblem = "Too far from your ship";
            return;
        }

        RefreshAsteroids();
        for (int i = 0; i < asteroidCount; i++)
        {
            if (Vector2.Distance(at, asteroidPositions[i]) < radius + asteroidRadii[i])
            {
                ghostProblem = "Too close to an asteroid";
                return;
            }
        }

        NativeArray<Building> buildings = GameEcs.BuildingQuery.ToComponentDataArray<Building>(Allocator.Temp);
        NativeArray<LocalTransform> places = GameEcs.BuildingQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        for (int i = 0; i < buildings.Length; i++)
        {
            if (Vector2.Distance(at, new Vector2(places[i].Position.x, places[i].Position.z)) < radius + buildings[i].radius)
            {
                ghostProblem = "Too close to another building";
                break;
            }
        }
        buildings.Dispose();
        places.Dispose();
        if (ghostProblem != null)
            return;

        if (!catalog.freeBuild)
        {
            DynamicBuffer<CargoItem> cargo = em.GetBuffer<CargoItem>(player, true);
            if (!catalog.workerFactoryCost.CanAfford(cargo))
            {
                ghostProblem = "Not enough resources";
                return;
            }
        }

        ghostValid = true;
    }

    private void RefreshAsteroids()
    {
        if (Time.unscaledTime < nextAsteroidRefresh)
            return;
        nextAsteroidRefresh = Time.unscaledTime + 0.5f;

        NativeArray<LocalTransform> transforms = GameEcs.AsteroidQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        NativeArray<AsteroidHex> hexes = GameEcs.AsteroidQuery.ToComponentDataArray<AsteroidHex>(Allocator.Temp);
        asteroidCount = transforms.Length;
        if (asteroidPositions.Length < asteroidCount)
        {
            asteroidPositions = new Vector2[asteroidCount];
            asteroidRadii = new float[asteroidCount];
        }
        for (int i = 0; i < asteroidCount; i++)
        {
            asteroidPositions[i] = new Vector2(transforms[i].Position.x, transforms[i].Position.z);
            asteroidRadii[i] = hexes[i].radius;
        }
        transforms.Dispose();
        hexes.Dispose();
    }

    private void RequestBuilding()
    {
        if (!GameEcs.Ready(out EntityManager em) || !GameEcs.TryGetPlayer(out _, out LocalTransform ship))
            return;
        // Face the new building toward the ship.
        float3 toShip = ship.Position - (float3)ghostPosition;
        toShip.y = 0f;
        Entity request = em.CreateEntity();
        em.AddComponentData(request, new BuildRequest
        {
            id = placingId,
            position = ghostPosition,
            rotation = quaternion.LookRotationSafe(toShip, math.up())
        });
    }

    private void OnGUI()
    {
        if (mode == Mode.None || Event.current.type != EventType.Repaint)
            return;
        EnsureStyles();

        if (mode == Mode.Radial)
            DrawRadial();
        else
            DrawGhost();
    }

    private void DrawRadial()
    {
        Color orange = new Color(0.78f, 0.32f, 0.04f, 1f);
        GLDraw.Begin();
        GLDraw.Disc(menuCentre, ringRadius + slotRadius + 14f, new Color(0.03f, 0.04f, 0.06f, 0.55f), 64);
        GLDraw.Ring(menuCentre, ringRadius - 1f, ringRadius + 1f, new Color(0.8f, 0.8f, 0.8f, 0.25f), 64);
        for (int i = 0; i < slots.Length; i++)
        {
            Vector2 c = SlotCentre(i);
            bool hot = i == hovered;
            Color fill = slots[i].enabled
                ? (hot ? new Color(0.78f, 0.32f, 0.04f, 0.9f) : new Color(0.12f, 0.18f, 0.24f, 0.95f))
                : new Color(0.15f, 0.15f, 0.15f, hot ? 0.9f : 0.7f);
            GLDraw.Disc(c, slotRadius, fill);
            GLDraw.Ring(c, slotRadius, slotRadius + 2f, slots[i].enabled ? orange : new Color(0.4f, 0.4f, 0.4f, 1f));
        }
        GLDraw.End();

        GUI.Label(new Rect(menuCentre.x - 60f, menuCentre.y - 10f, 120f, 20f), "Build", titleStyle);
        for (int i = 0; i < slots.Length; i++)
        {
            Vector2 c = SlotCentre(i);
            titleStyle.normal.textColor = slots[i].enabled ? Color.white : new Color(0.6f, 0.6f, 0.6f);
            GUI.Label(new Rect(c.x - 70f, c.y - 16f, 140f, 18f), slots[i].title, titleStyle);
            GUI.Label(new Rect(c.x - 70f, c.y + 1f, 140f, 16f), slots[i].subtitle, smallStyle);
        }
        titleStyle.normal.textColor = Color.white;
    }

    private void DrawGhost()
    {
        if (!ghostOnGround || !GameEcs.TryGetCatalog(out BuildCatalog catalog))
            return;
        Color colour = ghostValid ? new Color(0.3f, 1f, 0.45f, 0.95f) : new Color(1f, 0.3f, 0.25f, 0.95f);

        GLDraw.Begin();
        if (GameEcs.TryGetPlayer(out _, out LocalTransform ship))
            GLDraw.WorldCircle(ship.Position, catalog.placementRange, new Color(1f, 1f, 1f, 0.35f), 1.5f, 72, dashed: true);
        GLDraw.WorldCircle(ghostPosition, catalog.workerFactoryRadius, colour, 3f, 48);
        GLDraw.WorldCircle(ghostPosition, catalog.workerFactoryRadius * 0.15f, colour, 2f, 16);
        GLDraw.End();

        CombatFaction faction = GameEcs.PlayerFaction;
        string cost = catalog.freeBuild ? "Free (testing)" : GameEcs.CostText(catalog.workerFactoryCost);
        string text = $"{FactionNames.Name(placingId, faction)} ({FactionNames.English(placingId)})\n{cost}";
        if (!ghostValid && ghostProblem != null)
            text += "\n" + ghostProblem;
        Vector2 m = GameInput.MouseGui();
        tipStyle.normal.textColor = ghostValid ? Color.white : new Color(1f, 0.6f, 0.55f);
        GUI.Label(new Rect(m.x + 18f, m.y + 12f, 260f, 60f), text, tipStyle);
    }

    private void EnsureStyles()
    {
        if (titleStyle != null)
            return;
        titleStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 13, fontStyle = FontStyle.Bold };
        titleStyle.normal.textColor = Color.white;
        smallStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 10 };
        smallStyle.normal.textColor = new Color(0.8f, 0.8f, 0.8f, 0.9f);
        tipStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
    }

    private void OnDisable()
    {
        GameInput.BuildMenuBusy = false;
    }
}
