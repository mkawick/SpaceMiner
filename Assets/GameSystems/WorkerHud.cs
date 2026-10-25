using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.InputSystem;

// Under the cargo panel: how many workers are busy (green) and idle (grey).
// Press O to expand it into a list of every worker and what it's doing. Attaches itself on play.
public class WorkerHud : MonoBehaviour
{
    public float margin = 16f;
    public float top = 200f; // just under the cargo panel
    public float width = 210f;
    public int maxRows = 16;

    private bool expanded;
    private GUIStyle label, busyStyle, idleStyle, rowStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachOnPlay()
    {
        if (FindAnyObjectByType<WorkerHud>() == null)
            new GameObject("Worker HUD").AddComponent<WorkerHud>();
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null && keyboard.oKey.wasPressedThisFrame)
            expanded = !expanded;
    }

    private void OnGUI()
    {
        if (!GameEcs.Ready(out EntityManager em))
            return;
        EnsureStyles();

        NativeArray<Entity> bots = GameEcs.BotQuery.ToEntityArray(Allocator.Temp);
        int busy = 0;
        for (int i = 0; i < bots.Length; i++)
            if (em.GetComponentData<WorkerBot>(bots[i]).state != BotState.Idle)
                busy++;
        int idle = bots.Length - busy;

        int rows = expanded ? Mathf.Min(bots.Length, maxRows) + (bots.Length > maxRows ? 1 : 0) : 0;
        Rect panel = new Rect(margin, top, width, 30f + (expanded ? 6f + rows * 18f : 0f));
        GameInput.RegisterUiRect(panel);

        GUI.color = new Color(0.03f, 0.04f, 0.06f, 0.75f);
        GUI.DrawTexture(panel, Texture2D.whiteTexture);
        GUI.color = new Color(0.78f, 0.32f, 0.04f, 1f);
        GUI.DrawTexture(new Rect(panel.x, panel.y, panel.width, 2f), Texture2D.whiteTexture);
        GUI.color = Color.white;

        CombatFaction faction = GameEcs.PlayerFaction;
        string title = FactionNames.Name(UnitId.WorkerBot, faction);
        GUI.Label(new Rect(panel.x + 10f, panel.y + 6f, 110f, 20f), title, label);
        GUI.Label(new Rect(panel.x + 110f, panel.y + 6f, 40f, 20f), busy.ToString(), busyStyle);
        GUI.Label(new Rect(panel.x + 145f, panel.y + 6f, 30f, 20f), idle.ToString(), idleStyle);
        GUI.Label(new Rect(panel.x + 172f, panel.y + 6f, 30f, 20f), "[O]", rowStyle);

        if (expanded)
        {
            float y = panel.y + 32f;
            for (int i = 0; i < bots.Length && i < maxRows; i++)
            {
                WorkerBot bot = em.GetComponentData<WorkerBot>(bots[i]);
                float held = 0f;
                if (em.HasBuffer<CargoItem>(bots[i]))
                {
                    DynamicBuffer<CargoItem> cargo = em.GetBuffer<CargoItem>(bots[i], true);
                    for (int c = 0; c < cargo.Length; c++)
                        held += cargo[c].amount;
                }
                rowStyle.normal.textColor = bot.state == BotState.Idle ? new Color(0.65f, 0.65f, 0.65f) : new Color(0.45f, 1f, 0.55f);
                GUI.Label(new Rect(panel.x + 10f, y, width - 20f, 18f), $"#{bot.number}  {Describe(bot.state)}   {held:0} ore", rowStyle);
                y += 18f;
            }
            if (bots.Length > maxRows)
            {
                rowStyle.normal.textColor = new Color(0.65f, 0.65f, 0.65f);
                GUI.Label(new Rect(panel.x + 10f, y, width - 20f, 18f), $"… and {bots.Length - maxRows} more", rowStyle);
            }
        }
        bots.Dispose();
    }

    private static string Describe(BotState state)
    {
        switch (state)
        {
            case BotState.Moving: return "Moving";
            case BotState.Mining: return "Mining";
            case BotState.Returning: return "Returning";
            default: return "Idle";
        }
    }

    private void EnsureStyles()
    {
        if (label != null)
            return;
        label = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold };
        label.normal.textColor = new Color(0.9f, 0.9f, 0.9f);
        busyStyle = new GUIStyle(label) { alignment = TextAnchor.UpperRight };
        busyStyle.normal.textColor = new Color(0.45f, 1f, 0.55f);
        idleStyle = new GUIStyle(label) { alignment = TextAnchor.UpperRight };
        idleStyle.normal.textColor = new Color(0.65f, 0.65f, 0.65f);
        rowStyle = new GUIStyle(GUI.skin.label) { fontSize = 12 };
        rowStyle.normal.textColor = new Color(0.65f, 0.65f, 0.65f);
    }
}
