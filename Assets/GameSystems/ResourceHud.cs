using Unity.Entities;
using UnityEngine;

// Top-left panel: the minerals in the player's cargo hold. It attaches itself when play starts.
public class ResourceHud : MonoBehaviour
{
    public float margin = 16f;
    public float width = 210f;
    public float rowHeight = 20f;

    private World queryWorld;
    private EntityQuery playerQuery;
    private GUIStyle textStyle;
    private Texture2D white;

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

        Entity player = playerQuery.GetSingletonEntity();
        DynamicBuffer<CargoItem> cargo = world.EntityManager.GetBuffer<CargoItem>(player, true);

        if (textStyle == null)
        {
            textStyle = new GUIStyle(GUI.skin.label) { fontSize = 13 };
            white = Texture2D.whiteTexture;
        }

        int rows = AsteroidFieldRules.ResourceCount;
        Rect panel = new Rect(margin, margin, width, 28f + rows * rowHeight + 8f);
        DrawRect(panel, new Color(0.03f, 0.04f, 0.06f, 0.75f));
        DrawRect(new Rect(panel.x, panel.y, panel.width, 2f), new Color(0.78f, 0.32f, 0.04f, 1f));

        textStyle.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
        textStyle.fontStyle = FontStyle.Bold;
        GUI.Label(new Rect(panel.x + 10f, panel.y + 6f, width - 20f, 20f), "Cargo", textStyle);
        textStyle.fontStyle = FontStyle.Normal;

        for (int i = 0; i < rows; i++)
        {
            var type = (ResourceType)i;
            float y = panel.y + 28f + i * rowHeight;
            DrawRect(new Rect(panel.x + 10f, y + 5f, 10f, 10f), ResourceInfo.Colour(type));
            textStyle.alignment = TextAnchor.UpperLeft;
            GUI.Label(new Rect(panel.x + 28f, y, 130f, rowHeight), ResourceInfo.Name(type), textStyle);
            textStyle.alignment = TextAnchor.UpperRight;
            float amount = i < cargo.Length ? cargo[i].amount : 0f;
            GUI.Label(new Rect(panel.x + 10f, y, width - 20f, rowHeight), amount.ToString("N0"), textStyle);
        }
    }

    private void DrawRect(Rect rect, Color colour)
    {
        Color previous = GUI.color;
        GUI.color = colour;
        GUI.DrawTexture(rect, white);
        GUI.color = previous;
    }

    private void OnDestroy()
    {
        if (queryWorld != null && queryWorld.IsCreated)
            playerQuery.Dispose();
    }
}
