using Unity.Entities;
using UnityEngine;

// Top-left panel: one bar per resource showing how full that resource's storage is
// (stored / capacity). Right-click the panel to open the Storage screen.
public class ResourceHud : MonoBehaviour
{
    public float margin = 16f;
    public float width = 112f;
    public float rowHeight = 9f;

    private static readonly string[] Codes = { "Fe", "Cu", "Si", "Ice", "C", "Au", "Fm" };

    private World queryWorld;
    private EntityQuery playerQuery;
    private GUIStyle textStyle;
    private GUIStyle smallStyle;

    public static Rect PanelRect { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachOnPlay()
    {
        if (FindAnyObjectByType<ResourceHud>() == null)
            new GameObject("Resource HUD").AddComponent<ResourceHud>();
    }

    private void OnGUI()
    {
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return;
        if (queryWorld != world)
        {
            playerQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<InputData_Dots>(), ComponentType.ReadOnly<CargoItem>());
            queryWorld = world;
        }
        if (playerQuery.CalculateEntityCount() != 1)
            return;

        EntityManager em = world.EntityManager;
        Entity player = playerQuery.GetSingletonEntity();
        DynamicBuffer<CargoItem> cargo = em.GetBuffer<CargoItem>(player, true);
        bool hasCapacity = em.HasBuffer<StorageCapacity>(player);
        DynamicBuffer<StorageCapacity> capacity = hasCapacity ? em.GetBuffer<StorageCapacity>(player, true) : default;

        if (textStyle == null)
        {
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 8, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, padding = new RectOffset() };
            textStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
            smallStyle = new GUIStyle(GUI.skin.label) { fontSize = 8, alignment = TextAnchor.MiddleRight, padding = new RectOffset() };
            smallStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);
        }

        int rows = AsteroidFieldRules.ResourceCount;
        Rect panel = new Rect(margin, margin, width, 12f + rows * rowHeight + 4f);
        PanelRect = panel;
        GameInput.RegisterUiRect(panel);

        Event e = Event.current;
        if (e.type == EventType.MouseDown && e.button == 1 && panel.Contains(e.mousePosition))
        {
            StorageScreen.Toggle();
            e.Use();
        }

        Fill(panel, new Color(0.03f, 0.04f, 0.06f, 0.75f));
        Fill(new Rect(panel.x, panel.y, panel.width, 2f), new Color(0.78f, 0.32f, 0.04f, 1f));
        GUI.Label(new Rect(panel.x + 4f, panel.y + 2f, width - 8f, 10f), "Storage", textStyle);
        smallStyle.normal.textColor = new Color(0.6f, 0.6f, 0.6f);
        GUI.Label(new Rect(panel.x + 4f, panel.y + 2f, width - 8f, 10f), "right-click", smallStyle);
        smallStyle.normal.textColor = new Color(0.85f, 0.85f, 0.85f);

        for (int i = 0; i < rows; i++)
        {
            var type = (ResourceType)i;
            float y = panel.y + 12f + i * rowHeight;
            float amount = i < cargo.Length ? cargo[i].amount : 0f;
            float cap = hasCapacity && i < capacity.Length ? capacity[i].value : StorageRules.BaseCapacity;
            float fraction = cap > 0f ? Mathf.Clamp01(amount / cap) : 0f;

            Color colour = ResourceInfo.Colour(type);
            GUI.Label(new Rect(panel.x + 4f, y, 18f, rowHeight), Codes[i], textStyle);
            var bar = new Rect(panel.x + 22f, y + 2f, 44f, rowHeight - 3f);
            Fill(bar, new Color(1f, 1f, 1f, 0.1f));
            Fill(new Rect(bar.x, bar.y, bar.width * fraction, bar.height), fraction >= 0.999f ? Color.Lerp(colour, Color.white, 0.35f) : colour);
            GUI.Label(new Rect(bar.xMax + 1f, y, panel.xMax - bar.xMax - 4f, rowHeight), $"{amount:N0}/{cap:N0}", smallStyle);
        }
    }

    private static void Fill(Rect rect, Color colour)
    {
        Color previous = GUI.color;
        GUI.color = colour;
        GUI.DrawTexture(rect, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private void OnDestroy()
    {
        if (queryWorld != null && queryWorld.IsCreated)
            playerQuery.Dispose();
    }
}
