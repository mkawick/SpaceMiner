using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

// Shared mouse helpers for the UI scripts: where the pointer is in GUI space, whether it's over a
// panel, and where it hits the play plane.
public static class GameInput
{
    // True while the radial build menu is open or a building is being placed.
    public static bool BuildMenuBusy;

    // A click used by the build menu this frame; selection should ignore it.
    public static int ClickConsumedFrame = -1;

    private static readonly List<Rect> uiRects = new List<Rect>();
    private static readonly List<Rect> nextUiRects = new List<Rect>();
    private static int rectFrame = -1;

    // Panels call this from OnGUI each frame with their screen rect (GUI space, origin top-left).
    public static void RegisterUiRect(Rect rect)
    {
        if (Time.frameCount != rectFrame)
        {
            uiRects.Clear();
            uiRects.AddRange(nextUiRects);
            nextUiRects.Clear();
            rectFrame = Time.frameCount;
        }
        nextUiRects.Add(rect);
    }

    public static Vector2 MouseGui()
    {
        Mouse mouse = Mouse.current;
        if (mouse == null)
            return new Vector2(-1f, -1f);
        Vector2 p = mouse.position.ReadValue();
        return new Vector2(p.x, Screen.height - p.y);
    }

    public static bool PointerOverUI()
    {
        if (MinimapOverlay.PointerOverMap)
            return true;
        Vector2 p = MouseGui();
        foreach (Rect r in uiRects)
            if (r.Contains(p)) return true;
        foreach (Rect r in nextUiRects)
            if (r.Contains(p)) return true;
        return false;
    }

    public static bool MouseToGround(out Vector3 point)
    {
        point = Vector3.zero;
        Camera cam = Camera.main;
        Mouse mouse = Mouse.current;
        if (cam == null || mouse == null)
            return false;
        Ray ray = cam.ScreenPointToRay(mouse.position.ReadValue());
        var plane = new Plane(Vector3.up, Vector3.zero);
        if (!plane.Raycast(ray, out float distance))
            return false;
        point = ray.GetPoint(distance);
        point.y = 0f;
        return true;
    }

    // World point -> GUI space. False when the point is behind the camera.
    public static bool WorldToGui(Vector3 world, out Vector2 gui)
    {
        gui = Vector2.zero;
        Camera cam = Camera.main;
        if (cam == null)
            return false;
        Vector3 s = cam.WorldToScreenPoint(world);
        if (s.z <= 0f)
            return false;
        gui = new Vector2(s.x, Screen.height - s.y);
        return true;
    }

    public static bool ShiftHeld()
    {
        Keyboard k = Keyboard.current;
        return k != null && (k.leftShiftKey.isPressed || k.rightShiftKey.isPressed);
    }
}
