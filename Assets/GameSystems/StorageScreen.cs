using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

// The Storage screen. Opened by right-clicking the resource bars (or "Open storage" on a selected
// depot); closed with the X, Esc, or a click outside it.
//
//   Brush row     pick a resource (or Disorganized) to reserve blocks for.
//   Block grid    one square per 200 of storage, laid out as a rectangle. Click or drag across
//                 empty blocks to reserve them with the brush; blocks that hold something can't
//                 be re-reserved. Click a block that holds something to dump it (asks first).
//                 Colour = what's inside (filled from the bottom), outline = what it's reserved for.
//   Totals        stored / capacity per resource, reserved blocks, disorganized space.
//   Upgrades      4k, 7k, 12k, 17k, 25k; each adds blocks.
public class StorageScreen : MonoBehaviour
{
    private static StorageScreen instance;

    private static readonly Color WindowColour = new Color(0.17f, 0.17f, 0.18f, 0.97f);
    private static readonly Color BorderColour = new Color(0.55f, 0.80f, 1.00f, 1f);
    private static readonly Color BlockColour = new Color(0.80f, 0.80f, 0.82f, 1f);
    private static readonly Color TextColour = new Color(0.92f, 0.92f, 0.92f, 1f);
    private static readonly Color DimText = new Color(0.65f, 0.65f, 0.68f, 1f);
    private static readonly string[] TierNames = { "", "2k", "4k", "7k", "12k", "17k", "25k" };

    private bool open;
    private int brush = -1;          // resource type to reserve for; -1 = disorganized
    private bool dragging;
    private int dragStart = -1, dragEnd = -1;
    private int confirmBlock = -1;   // block waiting for "dump?" confirmation
    private int hoveredBlock = -1;

    private GUIStyle title, label, small, centred, blockText;

    public static bool IsOpen => instance != null && instance.open;

    public static void Open()
    {
        Ensure();
        instance.open = true;
        instance.confirmBlock = -1;
        instance.dragging = false;
    }

    public static void Close()
    {
        if (instance != null)
            instance.open = false;
    }

    public static void Toggle()
    {
        if (IsOpen) Close(); else Open();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Ensure()
    {
        if (instance == null)
            instance = FindAnyObjectByType<StorageScreen>() ?? new GameObject("Storage Screen").AddComponent<StorageScreen>();
    }

    private void Update()
    {
        Keyboard k = Keyboard.current;
        if (!open || k == null || !k.escapeKey.wasPressedThisFrame)
            return;
        if (confirmBlock >= 0)
            confirmBlock = -1;
        else
            open = false;
    }

    private void OnGUI()
    {
        if (!open)
            return;
        if (!GameEcs.Ready(out EntityManager em) || !GameEcs.TryGetPlayer(out Entity player, out LocalTransform _)
            || !em.HasComponent<StorageBank>(player))
            return;

        GUI.depth = -50; // above the other HUD panels, and gets clicks first
        EnsureStyles();
        GameInput.RegisterUiRect(new Rect(0f, 0f, Screen.width, Screen.height)); // modal: the world ignores clicks

        StorageBank bank = em.GetComponentData<StorageBank>(player);
        DynamicBuffer<StorageBlock> blocks = em.GetBuffer<StorageBlock>(player);
        DynamicBuffer<CargoItem> cargo = em.GetBuffer<CargoItem>(player);
        DynamicBuffer<StorageCapacity> capacity = em.GetBuffer<StorageCapacity>(player);
        GameEcs.TryGetCatalog(out BuildCatalog catalog);
        int types = AsteroidFieldRules.ResourceCount;

        // ---- Layout ----
        int n = blocks.Length;
        int cols = StorageRules.Columns(n);
        int rows = n > 0 ? (n + cols - 1) / cols : 0;
        const float pad = 18f, gap = 4f, header = 40f, brushRow = 26f, totalsH = 104f, upgradesH = 60f, footer = 20f;
        float winW = Mathf.Clamp(Screen.width * 0.8f, 660f, 1120f);
        float gridAreaW = winW - pad * 2f;
        float maxGridH = Mathf.Max(80f, Screen.height * 0.92f - (header + brushRow + totalsH + upgradesH + footer + 30f));
        float size = 56f;
        if (n > 0)
        {
            size = Mathf.Min(size, (gridAreaW - (cols - 1) * gap) / cols);
            size = Mathf.Min(size, (maxGridH - (rows - 1) * gap) / rows);
            size = Mathf.Max(12f, size);
        }
        float gridW = n > 0 ? cols * size + (cols - 1) * gap : gridAreaW;
        float gridH = n > 0 ? rows * size + (rows - 1) * gap : 80f;
        float winH = header + brushRow + gridH + 16f + totalsH + upgradesH + footer;
        var win = new Rect((Screen.width - winW) * 0.5f, (Screen.height - winH) * 0.5f, winW, winH);

        Event e = Event.current;

        // Click outside closes.
        if (e.type == EventType.MouseDown && !win.Contains(e.mousePosition) && confirmBlock < 0)
        {
            open = false;
            e.Use();
            return;
        }

        // ---- Window ----
        Fill(new Rect(win.x - 2f, win.y - 2f, win.width + 4f, win.height + 4f), BorderColour);
        Fill(win, WindowColour);

        float x = win.x + pad, y = win.y + 10f;
        float blockTotal = StorageRules.TierCapacity(bank.tier);
        float inBlocks = 0f;
        for (int i = 0; i < n; i++) inBlocks += blocks[i].amount;
        GUI.Label(new Rect(x, y, 300f, 26f), "Storage", title);
        GUI.Label(new Rect(x + 110f, y + 6f, winW - 200f, 20f),
                  bank.tier > 0 ? $"Depot {TierNames[bank.tier]}  ·  {inBlocks:N0} / {blockTotal:N0} in blocks" : "No Storage Depot yet", small);

        var closeRect = new Rect(win.xMax - 34f, win.y + 8f, 26f, 26f);
        Fill(closeRect, closeRect.Contains(e.mousePosition) ? new Color(0.75f, 0.25f, 0.25f, 1f) : new Color(0.3f, 0.3f, 0.32f, 1f));
        GUI.Label(closeRect, "X", centred);
        if (e.type == EventType.MouseDown && closeRect.Contains(e.mousePosition))
        {
            open = false;
            e.Use();
            return;
        }
        y += header;

        // ---- Brush row ----
        GUI.Label(new Rect(x, y + 4f, 80f, 16f), "Reserve for:", tiny);
        float bx = x + 62f;
        for (int b = -1; b < types; b++)
        {
            string name = b < 0 ? "Disorganized" : ResourceInfo.Name((ResourceType)b).Split(' ')[0];
            float w = b < 0 ? 62f : 46f;
            var r = new Rect(bx, y + 3f, w, 18f);
            bool selected = brush == b;
            if (selected) Fill(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, r.height + 4f), BorderColour);
            Fill(r, r.Contains(e.mousePosition) ? new Color(0.3f, 0.3f, 0.33f, 1f) : new Color(0.24f, 0.24f, 0.26f, 1f));
            Fill(new Rect(r.x + 3f, r.y + 5f, 8f, 8f), b < 0 ? BlockColour : ResourceInfo.Colour((ResourceType)b));
            GUI.Label(new Rect(r.x + 13f, r.y, r.width - 14f, r.height), name, tiny);
            if (e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition) && confirmBlock < 0)
            {
                brush = b;
                e.Use();
            }
            bx += w + 3f;
        }
        y += brushRow;

        // ---- Grid ----
        var grid = new Rect(x + (gridAreaW - gridW) * 0.5f, y, gridW, gridH);
        hoveredBlock = -1;
        if (n == 0)
        {
            Fill(new Rect(x, y, gridAreaW, gridH), new Color(1f, 1f, 1f, 0.04f));
            GUI.Label(new Rect(x, y, gridAreaW, gridH),
                      "Build a Storage Depot (hold B) to unlock 2,000 of block storage.\nUntil then each resource has 200 of built-in space.", centred);
        }
        else
        {
            int selMinR = 0, selMaxR = -1, selMinC = 0, selMaxC = -1;
            if (dragging && dragStart >= 0 && dragEnd >= 0)
            {
                selMinR = Mathf.Min(dragStart / cols, dragEnd / cols); selMaxR = Mathf.Max(dragStart / cols, dragEnd / cols);
                selMinC = Mathf.Min(dragStart % cols, dragEnd % cols); selMaxC = Mathf.Max(dragStart % cols, dragEnd % cols);
            }
            blockText.fontSize = Mathf.Clamp((int)(size * 0.26f), 8, 13);

            for (int i = 0; i < n; i++)
            {
                int r = i / cols, c = i % cols;
                var cell = new Rect(grid.x + c * (size + gap), grid.y + r * (size + gap), size, size);
                StorageBlock k = blocks[i];
                bool hover = cell.Contains(e.mousePosition) && confirmBlock < 0;
                if (hover) hoveredBlock = i;

                Fill(cell, BlockColour);
                if (k.content >= 0 && k.amount > 0f)
                {
                    float f = Mathf.Clamp01(k.amount / StorageRules.BlockSize);
                    float h = Mathf.Max(2f, cell.height * f);
                    Fill(new Rect(cell.x, cell.yMax - h, cell.width, h), ResourceInfo.Colour((ResourceType)k.content));
                }
                if (k.designation >= 0)
                    Outline(cell, ResourceInfo.Colour((ResourceType)k.designation), Mathf.Max(2f, size * 0.07f));
                bool inSelection = r >= selMinR && r <= selMaxR && c >= selMinC && c <= selMaxC;
                if (inSelection)
                    Fill(cell, k.amount > 0f ? new Color(1f, 0.3f, 0.3f, 0.25f) : new Color(0.55f, 0.80f, 1f, 0.45f));
                if (hover)
                    Outline(cell, Color.white, 2f);
                if (k.amount > 0f && size >= 30f)
                    GUI.Label(cell, k.amount.ToString("0"), blockText);
            }

            // Mouse: reserve empty blocks by click or drag; click a filled block to dump it.
            if (confirmBlock < 0)
            {
                int under = BlockAt(e.mousePosition, grid, cols, n, size, gap);
                if (e.type == EventType.MouseDown && e.button == 0 && under >= 0)
                {
                    if (blocks[under].amount > 0f)
                        confirmBlock = under;
                    else
                    {
                        dragging = true;
                        dragStart = dragEnd = under;
                    }
                    e.Use();
                }
                else if (e.type == EventType.MouseDrag && dragging)
                {
                    int clamped = BlockAtClamped(e.mousePosition, grid, cols, rows, n, size, gap);
                    if (clamped >= 0) dragEnd = clamped;
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && dragging)
                {
                    ApplyBrush(blocks, cols, dragStart, dragEnd);
                    dragging = false;
                    dragStart = dragEnd = -1;
                    e.Use();
                }
            }
        }
        y += gridH + 16f;

        // ---- Totals ----
        int looseBlocks = 0;
        float looseFree = 0f;
        for (int i = 0; i < n; i++)
            if (blocks[i].designation < 0)
            {
                looseBlocks++;
                looseFree += StorageRules.BlockSize - blocks[i].amount;
            }
        float colW = (winW - pad * 2f) / 2f;
        for (int t = 0; t < types; t++)
        {
            int reserved = 0;
            for (int i = 0; i < n; i++) if (blocks[i].designation == t) reserved++;
            float tx = x + (t % 2) * colW, ty = y + (t / 2) * 16f;
            float stored = t < cargo.Length ? cargo[t].amount : 0f;
            float cap = t < capacity.Length ? capacity[t].value : StorageRules.BaseCapacity;
            Fill(new Rect(tx, ty + 4f, 8f, 8f), ResourceInfo.Colour((ResourceType)t));
            GUI.Label(new Rect(tx + 12f, ty, 110f, 16f), ResourceInfo.Name((ResourceType)t), tiny);
            GUI.Label(new Rect(tx + 122f, ty, 100f, 16f), $"{stored:N0} / {cap:N0}", tiny);
            GUI.Label(new Rect(tx + 222f, ty, colW - 222f, 16f), reserved > 0 ? $"{reserved} reserved" : "", tiny);
        }
        float ly = y + 3 * 16f;
        Fill(new Rect(x + colW, ly + 4f, 8f, 8f), BlockColour);
        GUI.Label(new Rect(x + colW + 12f, ly, colW - 12f, 16f), $"Disorganized: {looseBlocks} blocks, {looseFree:N0} free", tiny);
        GUI.Label(new Rect(x, ly + 22f, winW - pad * 2f, 16f),
                  $"Every resource also has {StorageRules.BaseCapacity:N0} of built-in space. Disorganized blocks take whatever comes in.", small);
        y += totalsH;

        // ---- Upgrades ----
        GUI.Label(new Rect(x, y, 200f, 16f), "Upgrades", tiny);
        float ux = x;
        for (int tier = 2; tier <= StorageRules.MaxTier; tier++)
        {
            var r = new Rect(ux, y + 18f, 118f, 34f);
            bool done = bank.tier >= tier;
            bool next = bank.tier >= 1 && tier == bank.tier + 1;
            ResourceCost cost = StorageRules.UpgradeCost(tier);
            bool free = catalog.freeBuild;
            bool affordable = next && (free || cost.CanAfford(cargo));
            Fill(r, done ? new Color(0.2f, 0.35f, 0.25f, 1f) : affordable ? new Color(0.24f, 0.30f, 0.38f, 1f) : new Color(0.22f, 0.22f, 0.24f, 1f));
            if (next) Outline(r, BorderColour, 1f);
            GUI.Label(new Rect(r.x, r.y + 1f, r.width, 16f), $"{TierNames[tier]}  ({StorageRules.Blocks(tier)} blocks)", tinyCentred);
            string sub = done ? "built" : !next ? "locked" : free ? "free (testing)" : GameEcs.CostText(cost);
            GUI.Label(new Rect(r.x + 2f, r.y + 17f, r.width - 4f, 16f), sub, tinyCentred);
            if (affordable && e.type == EventType.MouseDown && e.button == 0 && r.Contains(e.mousePosition) && confirmBlock < 0)
            {
                if (!free) cost.Pay(cargo);
                bank.tier = tier;
                em.SetComponentData(player, bank);
                e.Use();
            }
            ux += 124f;
        }
        y += upgradesH;

        // ---- Footer: what's under the mouse, or how it works ----
        string hint = "Click or drag over empty blocks to reserve them  ·  click a filled block to dump it  ·  Esc or click outside to close";
        if (hoveredBlock >= 0)
        {
            StorageBlock k = blocks[hoveredBlock];
            string inside = k.content >= 0 ? $"{ResourceInfo.Name((ResourceType)k.content)} {k.amount:N0}/{StorageRules.BlockSize:N0}" : "empty";
            string reservedFor = k.designation >= 0 ? $"reserved for {ResourceInfo.Name((ResourceType)k.designation)}" : "disorganized";
            hint = $"Block {hoveredBlock + 1}: {inside}, {reservedFor}" + (k.amount > 0f ? "  ·  click to dump" : "");
        }
        GUI.Label(new Rect(x, win.yMax - footer, winW - pad * 2f, 16f), hint, tiny);

        // ---- Dump confirmation ----
        if (confirmBlock >= 0 && confirmBlock < n)
        {
            StorageBlock k = blocks[confirmBlock];
            if (k.amount <= 0f || k.content < 0)
            {
                confirmBlock = -1;
                return;
            }
            var box = new Rect(win.center.x - 170f, grid.center.y - 55f, 340f, 110f);
            Fill(new Rect(box.x - 2f, box.y - 2f, box.width + 4f, box.height + 4f), BorderColour);
            Fill(box, new Color(0.12f, 0.12f, 0.13f, 1f));
            GUI.Label(new Rect(box.x + 12f, box.y + 10f, box.width - 24f, 44f),
                      $"Dump {k.amount:N0} {ResourceInfo.Name((ResourceType)k.content)} from block {confirmBlock + 1}?\nIt can't be recovered.", label);
            var dump = new Rect(box.x + 40f, box.yMax - 42f, 110f, 30f);
            var cancel = new Rect(box.xMax - 150f, box.yMax - 42f, 110f, 30f);
            Fill(dump, dump.Contains(e.mousePosition) ? new Color(0.75f, 0.25f, 0.25f, 1f) : new Color(0.5f, 0.2f, 0.2f, 1f));
            Fill(cancel, cancel.Contains(e.mousePosition) ? new Color(0.35f, 0.35f, 0.38f, 1f) : new Color(0.26f, 0.26f, 0.28f, 1f));
            GUI.Label(dump, "Dump", centred);
            GUI.Label(cancel, "Cancel", centred);
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                if (dump.Contains(e.mousePosition))
                    Dump(em, player, confirmBlock);
                confirmBlock = -1;
                e.Use();
            }
        }
    }

    private void ApplyBrush(DynamicBuffer<StorageBlock> blocks, int cols, int a, int b)
    {
        if (a < 0 || b < 0)
            return;
        int r0 = Mathf.Min(a / cols, b / cols), r1 = Mathf.Max(a / cols, b / cols);
        int c0 = Mathf.Min(a % cols, b % cols), c1 = Mathf.Max(a % cols, b % cols);
        for (int r = r0; r <= r1; r++)
            for (int c = c0; c <= c1; c++)
            {
                int i = r * cols + c;
                if (i >= blocks.Length)
                    continue;
                StorageBlock k = blocks[i];
                if (k.amount > 0f)
                    continue; // blocks that hold something keep their reservation
                k.designation = (sbyte)brush;
                k.content = -1;
                blocks[i] = k;
            }
    }

    // Empties one block: its resources are removed from the player's totals.
    private static void Dump(EntityManager em, Entity player, int index)
    {
        DynamicBuffer<StorageBlock> blocks = em.GetBuffer<StorageBlock>(player);
        StorageBlock k = blocks[index];
        int type = k.content;
        if (type < 0)
            return;
        DynamicBuffer<CargoItem> cargo = em.GetBuffer<CargoItem>(player);
        DynamicBuffer<StorageLedger> ledger = em.GetBuffer<StorageLedger>(player);
        float amount = k.amount;
        if (type < cargo.Length)
            cargo[type] = new CargoItem { amount = Mathf.Max(0f, cargo[type].amount - amount) };
        if (type < ledger.Length)
        {
            StorageLedger l = ledger[type];
            l.lastTotal = Mathf.Max(0f, l.lastTotal - amount); // so the storage system doesn't take it out twice
            ledger[type] = l;
        }
        k.amount = 0f;
        k.content = -1;
        blocks[index] = k;
    }

    private static int BlockAt(Vector2 p, Rect grid, int cols, int n, float size, float gap)
    {
        if (!grid.Contains(p))
            return -1;
        int c = Mathf.FloorToInt((p.x - grid.x) / (size + gap));
        int r = Mathf.FloorToInt((p.y - grid.y) / (size + gap));
        float inX = (p.x - grid.x) - c * (size + gap), inY = (p.y - grid.y) - r * (size + gap);
        if (inX > size || inY > size)
            return -1; // in the gap
        int i = r * cols + c;
        return c < cols && i < n ? i : -1;
    }

    private static int BlockAtClamped(Vector2 p, Rect grid, int cols, int rows, int n, float size, float gap)
    {
        int c = Mathf.Clamp(Mathf.FloorToInt((p.x - grid.x) / (size + gap)), 0, cols - 1);
        int r = Mathf.Clamp(Mathf.FloorToInt((p.y - grid.y) / (size + gap)), 0, rows - 1);
        int i = r * cols + c;
        return i < n ? i : -1;
    }

    private GUIStyle smallCentred, tiny, tinyCentred;

    private void EnsureStyles()
    {
        if (title != null)
            return;
        title = new GUIStyle(GUI.skin.label) { fontSize = 16, fontStyle = FontStyle.Bold };
        title.normal.textColor = TextColour;
        label = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft, wordWrap = true };
        label.normal.textColor = TextColour;
        small = new GUIStyle(GUI.skin.label) { fontSize = 11, alignment = TextAnchor.MiddleLeft };
        small.normal.textColor = DimText;
        centred = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleCenter, wordWrap = true };
        centred.normal.textColor = TextColour;
        smallCentred = new GUIStyle(small) { alignment = TextAnchor.MiddleCenter };
        tiny = new GUIStyle(GUI.skin.label) { fontSize = 9, alignment = TextAnchor.MiddleLeft, padding = new RectOffset() };
        tiny.normal.textColor = TextColour;
        tinyCentred = new GUIStyle(tiny) { alignment = TextAnchor.MiddleCenter };
        blockText = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
        blockText.normal.textColor = new Color(0.08f, 0.08f, 0.1f, 1f);
    }

    private static void Fill(Rect r, Color c)
    {
        Color previous = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        GUI.color = previous;
    }

    private static void Outline(Rect r, Color c, float w)
    {
        Fill(new Rect(r.x, r.y, r.width, w), c);
        Fill(new Rect(r.x, r.yMax - w, r.width, w), c);
        Fill(new Rect(r.x, r.y, w, r.height), c);
        Fill(new Rect(r.xMax - w, r.y, w, r.height), c);
    }
}
