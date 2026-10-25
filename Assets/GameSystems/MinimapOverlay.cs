using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

// Upper-right radar map. A hex grid over the field, rotated so the ship's heading points up.
// Asteroids show only inside the ship's radar range. Zoom with the mouse wheel over the map,
// or with + and -. It attaches itself when play starts.
public class MinimapOverlay : MonoBehaviour
{
    public static bool PointerOverMap { get; private set; }

    [Header("Layout")]
    [Range(0.1f, 0.5f)] public float sizeOfScreenHeight = 0.32f;
    public float margin = 16f;

    [Header("Zoom: world units from the centre to the rim")]
    public float viewRadius = 1500f;
    public float minViewRadius = 300f;
    public float maxViewRadius = 6000f;
    [Range(0.05f, 0.5f)] public float zoomStep = 0.15f;

    [Header("Colours")]
    public Color gridColour = new Color(0.82f, 0.82f, 0.82f, 0.45f);
    [Tooltip("Grid lines are 1 pixel wide; this fades them so they read lighter. 1 = full gridColour alpha")]
    [Range(0.05f, 1f)] public float gridOpacity = 0.6f;
    [Tooltip("Below this hex size on screen (pixels), the grid fades out further so it doesn't turn into a solid mesh")]
    public float gridFadeBelowPixels = 24f;
    public Color borderColour = new Color(0.78f, 0.32f, 0.04f, 1f); // dark orange
    public Color backgroundColour = new Color(0.03f, 0.04f, 0.06f, 0.80f);
    public Color radarColour = new Color(0.10f, 0.16f, 0.22f, 0.70f);
    public Color shipColour = new Color(1f, 1f, 1f, 0.95f);
    public float borderWidth = 3f;

    private const int CircleSegments = 96;

    private Material material;
    private World queryWorld;
    private EntityQuery playerQuery;
    private EntityQuery asteroidQuery;
    private EntityQuery configQuery;

    private Vector2[] asteroidPositions = new Vector2[0];
    private float[] asteroidRadii = new float[0];
    private ResourceType[] asteroidTypes = new ResourceType[0];
    private int asteroidCount;
    private float nextAsteroidRefresh;

    private GUIStyle labelStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachOnPlay()
    {
        if (FindAnyObjectByType<MinimapOverlay>() == null)
            new GameObject("Minimap Overlay").AddComponent<MinimapOverlay>();
    }

    private bool EnsureQueries()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;
        if (queryWorld == world)
            return true;

        EntityManager em = world.EntityManager;
        playerQuery = em.CreateEntityQuery(ComponentType.ReadOnly<InputData_Dots>(),
                                           ComponentType.ReadOnly<LocalTransform>(),
                                           ComponentType.ReadOnly<ShipRadar>());
        asteroidQuery = new EntityQueryBuilder(Allocator.Temp)
            .WithAll<AsteroidHex, AsteroidResource, LocalTransform>()
            .WithNone<DestroyedEntityTag_Dots>()
            .Build(em);
        configQuery = em.CreateEntityQuery(ComponentType.ReadOnly<AsteroidFieldConfig>());
        queryWorld = world;
        return true;
    }

    private void Update()
    {
        // Same layout maths as OnGUI, but in Input System screen space (origin bottom-left).
        float radius = Screen.height * sizeOfScreenHeight * 0.5f;
        Vector2 centre = new Vector2(Screen.width - margin - radius, Screen.height - margin - radius);

        Mouse mouse = Mouse.current;
        PointerOverMap = mouse != null && Vector2.Distance(mouse.position.ReadValue(), centre) <= radius;

        if (PointerOverMap)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f)
                Zoom(-Mathf.Sign(scroll));
        }

        Keyboard keyboard = Keyboard.current;
        if (keyboard != null)
        {
            if (keyboard.equalsKey.wasPressedThisFrame || keyboard.numpadPlusKey.wasPressedThisFrame)
                Zoom(-1f);
            if (keyboard.minusKey.wasPressedThisFrame || keyboard.numpadMinusKey.wasPressedThisFrame)
                Zoom(1f);
        }
    }

    // direction -1 = zoom in, +1 = zoom out
    private void Zoom(float direction)
    {
        viewRadius = Mathf.Clamp(viewRadius * (1f + zoomStep * direction), minViewRadius, maxViewRadius);
    }

    private void RefreshAsteroids()
    {
        if (Time.unscaledTime < nextAsteroidRefresh)
            return;
        nextAsteroidRefresh = Time.unscaledTime + 0.25f;

        NativeArray<LocalTransform> transforms = asteroidQuery.ToComponentDataArray<LocalTransform>(Allocator.Temp);
        NativeArray<AsteroidHex> hexes = asteroidQuery.ToComponentDataArray<AsteroidHex>(Allocator.Temp);
        NativeArray<AsteroidResource> resources = asteroidQuery.ToComponentDataArray<AsteroidResource>(Allocator.Temp);

        asteroidCount = transforms.Length;
        if (asteroidPositions.Length < asteroidCount)
        {
            asteroidPositions = new Vector2[asteroidCount];
            asteroidRadii = new float[asteroidCount];
            asteroidTypes = new ResourceType[asteroidCount];
        }
        for (int i = 0; i < asteroidCount; i++)
        {
            float3 p = transforms[i].Position;
            asteroidPositions[i] = new Vector2(p.x, p.z);
            asteroidRadii[i] = hexes[i].radius;
            asteroidTypes[i] = resources[i].type;
        }

        transforms.Dispose();
        hexes.Dispose();
        resources.Dispose();
    }

    private void OnGUI()
    {
        if (Event.current.type != EventType.Repaint || !EnsureQueries())
            return;
        if (playerQuery.CalculateEntityCount() != 1 || configQuery.CalculateEntityCount() != 1)
            return;

        LocalTransform ship = playerQuery.GetSingleton<LocalTransform>();
        float radarRange = playerQuery.GetSingleton<ShipRadar>().range;
        AsteroidFieldConfig cfg = configQuery.GetSingleton<AsteroidFieldConfig>();
        RefreshAsteroids();
        EnsureMaterial();

        // Layout in GUI space (origin top-left).
        float radius = Screen.height * sizeOfScreenHeight * 0.5f;
        Vector2 centre = new Vector2(Screen.width - margin - radius, margin + radius);
        float scale = radius / viewRadius; // pixels per world unit

        Vector2 shipPos = new Vector2(ship.Position.x, ship.Position.z);
        float3 forward = ship.Forward();
        float heading = math.atan2(forward.x, forward.z); // 0 = facing +Z
        float cosH = Mathf.Cos(heading), sinH = Mathf.Sin(heading);

        // World (x, z) -> map pixels, rotated so the ship's heading points up.
        Vector2 ToMap(Vector2 world)
        {
            Vector2 d = world - shipPos;
            float x = d.x * cosH - d.y * sinH;
            float y = d.x * sinH + d.y * cosH;
            return centre + new Vector2(x, -y) * scale;
        }

        GL.PushMatrix();
        material.SetPass(0);
        GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);

        // Background, then the area the radar covers.
        DrawDisc(centre, radius, backgroundColour);
        float radarPixels = radarRange * scale;
        DrawDisc(centre, Mathf.Min(radarPixels, radius), radarColour);

        // Hex grid. Each edge is drawn once: edges 0-2 from every hex, plus the outer edges at the map rim.
        GL.Begin(GL.LINES);
        float hexPixels = cfg.hexSize * scale;
        Color grid = gridColour;
        grid.a *= gridOpacity * Mathf.Clamp01(hexPixels / Mathf.Max(1f, gridFadeBelowPixels));
        GL.Color(grid);
        int2 shipHex = HexGrid.WorldToHex(new float2(shipPos.x, shipPos.y), cfg.hexSize);
        int rings = Mathf.CeilToInt(viewRadius / (1.5f * cfg.hexSize)) + 1;
        for (int dq = -rings; dq <= rings; dq++)
        {
            for (int dr = Mathf.Max(-rings, -dq - rings); dr <= Mathf.Min(rings, -dq + rings); dr++)
            {
                int2 hex = shipHex + new int2(dq, dr);
                if (!AsteroidFieldRules.IsInsideMap(cfg, hex))
                    continue;
                float2 c = HexGrid.HexToWorld(hex, cfg.hexSize);
                if (Vector2.Distance(new Vector2(c.x, c.y), shipPos) > viewRadius + cfg.hexSize * 2f)
                    continue;

                for (int edge = 0; edge < 6; edge++)
                {
                    if (edge >= 3 && AsteroidFieldRules.IsInsideMap(cfg, hex + HexGrid.EdgeNeighbour(edge)))
                        continue;
                    float2 c0 = c + HexGrid.Corner(edge, cfg.hexSize);
                    float2 c1 = c + HexGrid.Corner((edge + 1) % 6, cfg.hexSize);
                    Vector2 a = ToMap(new Vector2(c0.x, c0.y));
                    Vector2 b = ToMap(new Vector2(c1.x, c1.y));
                    if (ClipToCircle(centre, radius, ref a, ref b))
                    {
                        GL.Vertex3(a.x, a.y, 0f);
                        GL.Vertex3(b.x, b.y, 0f);
                    }
                }
            }
        }

        // The map's outer barrier, in the border colour, when it's in view.
        GL.Color(borderColour);
        Vector2 worldOrigin = ToMap(Vector2.zero);
        float barrierPixels = cfg.mapRadius * scale;
        if (Vector2.Distance(worldOrigin, centre) + radius > barrierPixels)
        {
            for (int i = 0; i < 360; i++)
            {
                float a0 = i * Mathf.PI * 2f / 360f, a1 = (i + 1) * Mathf.PI * 2f / 360f;
                Vector2 p0 = worldOrigin + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * barrierPixels;
                Vector2 p1 = worldOrigin + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * barrierPixels;
                if (ClipToCircle(centre, radius, ref p0, ref p1))
                {
                    GL.Vertex3(p0.x, p0.y, 0f);
                    GL.Vertex3(p1.x, p1.y, 0f);
                }
            }
        }

        // Radar range ring.
        if (radarPixels < radius)
        {
            GL.Color(new Color(0.55f, 0.80f, 1f, 0.35f));
            for (int i = 0; i < CircleSegments; i++)
            {
                Vector2 p0 = centre + Polar(i, radarPixels);
                Vector2 p1 = centre + Polar(i + 1, radarPixels);
                GL.Vertex3(p0.x, p0.y, 0f);
                GL.Vertex3(p1.x, p1.y, 0f);
            }
        }
        GL.End();

        // Asteroids inside radar range, coloured by resource.
        GL.Begin(GL.QUADS);
        float visible = Mathf.Min(radarRange, viewRadius);
        float visibleSqr = visible * visible;
        for (int i = 0; i < asteroidCount; i++)
        {
            if ((asteroidPositions[i] - shipPos).sqrMagnitude > visibleSqr)
                continue;
            Vector2 p = ToMap(asteroidPositions[i]);
            float h = Mathf.Max(1.5f, asteroidRadii[i] * scale);
            GL.Color(ResourceInfo.Colour(asteroidTypes[i]));
            GL.Vertex3(p.x - h, p.y - h, 0f);
            GL.Vertex3(p.x + h, p.y - h, 0f);
            GL.Vertex3(p.x + h, p.y + h, 0f);
            GL.Vertex3(p.x - h, p.y + h, 0f);
        }
        GL.End();

        // The ship, always at the centre, pointing up.
        GL.Begin(GL.TRIANGLES);
        GL.Color(shipColour);
        GL.Vertex3(centre.x, centre.y - 8f, 0f);
        GL.Vertex3(centre.x + 5f, centre.y + 6f, 0f);
        GL.Vertex3(centre.x - 5f, centre.y + 6f, 0f);
        GL.End();

        // Dark orange rim.
        DrawRing(centre, radius, radius + borderWidth, borderColour);

        // North marker on the rim (world +Z).
        Vector2 north = ToMap(shipPos + new Vector2(0f, 1f)) - centre;
        north = north.sqrMagnitude > 0f ? north.normalized : Vector2.down;
        Vector2 tip = centre + north * (radius + borderWidth);
        Vector2 side = new Vector2(-north.y, north.x) * 6f;
        GL.Begin(GL.TRIANGLES);
        GL.Color(borderColour);
        GL.Vertex3(tip.x + north.x * 10f, tip.y + north.y * 10f, 0f);
        GL.Vertex3(tip.x + side.x, tip.y + side.y, 0f);
        GL.Vertex3(tip.x - side.x, tip.y - side.y, 0f);
        GL.End();

        GL.PopMatrix();

        if (labelStyle == null)
            labelStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontSize = 12 };
        labelStyle.normal.textColor = borderColour;
        Vector2 nLabel = tip + north * 20f;
        GUI.Label(new Rect(nLabel.x - 10f, nLabel.y - 10f, 20f, 20f), "N", labelStyle);
        labelStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f, 0.9f);
        GUI.Label(new Rect(centre.x - radius, centre.y + radius + borderWidth + 2f, radius * 2f, 18f),
                  $"Radar {radarRange:N0}   View {viewRadius:N0}   Hex ({shipHex.x}, {shipHex.y})", labelStyle);
    }

    private static Vector2 Polar(int i, float r)
    {
        float a = i * Mathf.PI * 2f / CircleSegments;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
    }

    private static void DrawDisc(Vector2 centre, float r, Color colour)
    {
        GL.Begin(GL.TRIANGLES);
        GL.Color(colour);
        for (int i = 0; i < CircleSegments; i++)
        {
            Vector2 p0 = centre + Polar(i, r);
            Vector2 p1 = centre + Polar(i + 1, r);
            GL.Vertex3(centre.x, centre.y, 0f);
            GL.Vertex3(p0.x, p0.y, 0f);
            GL.Vertex3(p1.x, p1.y, 0f);
        }
        GL.End();
    }

    private static void DrawRing(Vector2 centre, float inner, float outer, Color colour)
    {
        GL.Begin(GL.QUADS);
        GL.Color(colour);
        for (int i = 0; i < CircleSegments; i++)
        {
            Vector2 a0 = centre + Polar(i, inner), a1 = centre + Polar(i + 1, inner);
            Vector2 b0 = centre + Polar(i, outer), b1 = centre + Polar(i + 1, outer);
            GL.Vertex3(a0.x, a0.y, 0f);
            GL.Vertex3(b0.x, b0.y, 0f);
            GL.Vertex3(b1.x, b1.y, 0f);
            GL.Vertex3(a1.x, a1.y, 0f);
        }
        GL.End();
    }

    // Clips the segment a-b to a circle. Returns false when nothing of it is inside.
    private static bool ClipToCircle(Vector2 centre, float r, ref Vector2 a, ref Vector2 b)
    {
        Vector2 d = b - a, f = a - centre;
        float c = Vector2.Dot(f, f) - r * r;
        bool aInside = c <= 0f;
        bool bInside = (b - centre).sqrMagnitude <= r * r;
        if (aInside && bInside)
            return true;

        float aa = Vector2.Dot(d, d);
        if (aa < 1e-6f)
            return false;
        float bb = 2f * Vector2.Dot(f, d);
        float disc = bb * bb - 4f * aa * c;
        if (disc <= 0f)
            return false;

        float sq = Mathf.Sqrt(disc);
        float t0 = Mathf.Max((-bb - sq) / (2f * aa), 0f);
        float t1 = Mathf.Min((-bb + sq) / (2f * aa), 1f);
        if (t0 >= t1)
            return false;

        Vector2 start = a;
        a = start + d * t0;
        b = start + d * t1;
        return true;
    }

    private void EnsureMaterial()
    {
        if (material != null)
            return;
        material = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
        material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
        material.SetInt("_ZWrite", 0);
        material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
    }

    private void OnDestroy()
    {
        PointerOverMap = false;
        if (material != null)
            Destroy(material);
        if (queryWorld != null && queryWorld.IsCreated)
        {
            playerQuery.Dispose();
            asteroidQuery.Dispose();
            configQuery.Dispose();
        }
    }
}
