using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

// Weapon slots at the bottom left: key, name and cooldown. The active slot is highlighted.
public class WeaponHud : MonoBehaviour
{
    private GUIStyle nameStyle;
    private GUIStyle keyStyle;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (FindAnyObjectByType<WeaponHud>() == null)
            new GameObject("Weapon HUD").AddComponent<WeaponHud>();
    }

    private void OnGUI()
    {
        if (!GameEcs.Ready(out EntityManager em) || !GameEcs.TryGetPlayer(out Entity player, out LocalTransform _))
            return;
        if (!em.HasBuffer<WeaponSlot>(player) || !em.HasComponent<ActiveWeapon>(player))
            return;

        if (nameStyle == null)
        {
            nameStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            nameStyle.normal.textColor = new Color(0.9f, 0.92f, 0.95f);
            keyStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
            keyStyle.normal.textColor = new Color(0.04f, 0.05f, 0.07f);
        }

        DynamicBuffer<WeaponSlot> slots = em.GetBuffer<WeaponSlot>(player);
        int active = em.GetComponentData<ActiveWeapon>(player).slot;
        float now = (float)em.World.Time.ElapsedTime;

        const float slotWidth = 150f, slotHeight = 34f, gap = 6f, margin = 16f;
        float width = slots.Length * slotWidth + (slots.Length - 1) * gap;
        var area = new Rect(margin, Screen.height - margin - slotHeight, width, slotHeight);
        GameInput.RegisterUiRect(area);

        for (int i = 0; i < slots.Length; i++)
        {
            WeaponSlot slot = slots[i];
            var r = new Rect(area.x + i * (slotWidth + gap), area.y, slotWidth, slotHeight);
            bool isActive = i == active;
            Fill(r, new Color(0.03f, 0.04f, 0.06f, isActive ? 0.9f : 0.7f));
            Color accent = isActive ? new Color(0.88f, 0.48f, 0.18f, 1f) : new Color(0.35f, 0.4f, 0.48f, 1f);
            Fill(new Rect(r.x, r.y, r.width, 2f), accent);

            // Cooldown: the slot fills back up from left to right.
            float remaining = Mathf.Max(0f, slot.readyTime - now);
            if (remaining > 0f && slot.interval > 0f)
                Fill(new Rect(r.x, r.yMax - 4f, r.width * (1f - remaining / slot.interval), 4f), new Color(accent.r, accent.g, accent.b, 0.7f));

            var key = new Rect(r.x + 6f, r.y + 8f, 20f, 20f);
            Fill(key, accent);
            GUI.Label(key, (i + 1).ToString(), keyStyle);
            GUI.Label(new Rect(r.x + 32f, r.y + 2f, r.width - 36f, r.height - 4f), WeaponData.Name(slot.id), nameStyle);
        }
    }

    private static void Fill(Rect r, Color c)
    {
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = Color.white;
    }
}
