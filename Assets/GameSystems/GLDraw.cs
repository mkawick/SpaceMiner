using UnityEngine;

// Immediate-mode 2D drawing in GUI space (origin top-left) for OnGUI repaint events.
public static class GLDraw
{
    private static Material material;

    public static void Begin()
    {
        if (material == null)
        {
            material = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            material.SetInt("_ZWrite", 0);
            material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }
        GL.PushMatrix();
        material.SetPass(0);
        GL.LoadPixelMatrix(0, Screen.width, Screen.height, 0);
    }

    public static void End()
    {
        GL.PopMatrix();
    }

    public static void Disc(Vector2 centre, float radius, Color colour, int segments = 48)
    {
        GL.Begin(GL.TRIANGLES);
        GL.Color(colour);
        for (int i = 0; i < segments; i++)
        {
            Vector2 a = centre + Polar(i, segments, radius);
            Vector2 b = centre + Polar(i + 1, segments, radius);
            GL.Vertex3(centre.x, centre.y, 0f);
            GL.Vertex3(a.x, a.y, 0f);
            GL.Vertex3(b.x, b.y, 0f);
        }
        GL.End();
    }

    public static void Ring(Vector2 centre, float inner, float outer, Color colour, int segments = 48)
    {
        GL.Begin(GL.QUADS);
        GL.Color(colour);
        for (int i = 0; i < segments; i++)
        {
            Vector2 a0 = centre + Polar(i, segments, inner), a1 = centre + Polar(i + 1, segments, inner);
            Vector2 b0 = centre + Polar(i, segments, outer), b1 = centre + Polar(i + 1, segments, outer);
            GL.Vertex3(a0.x, a0.y, 0f);
            GL.Vertex3(b0.x, b0.y, 0f);
            GL.Vertex3(b1.x, b1.y, 0f);
            GL.Vertex3(a1.x, a1.y, 0f);
        }
        GL.End();
    }

    public static void RectOutline(Rect r, Color colour)
    {
        GL.Begin(GL.LINES);
        GL.Color(colour);
        GL.Vertex3(r.xMin, r.yMin, 0f); GL.Vertex3(r.xMax, r.yMin, 0f);
        GL.Vertex3(r.xMax, r.yMin, 0f); GL.Vertex3(r.xMax, r.yMax, 0f);
        GL.Vertex3(r.xMax, r.yMax, 0f); GL.Vertex3(r.xMin, r.yMax, 0f);
        GL.Vertex3(r.xMin, r.yMax, 0f); GL.Vertex3(r.xMin, r.yMin, 0f);
        GL.End();
    }

    public static void RectFill(Rect r, Color colour)
    {
        GL.Begin(GL.QUADS);
        GL.Color(colour);
        GL.Vertex3(r.xMin, r.yMin, 0f);
        GL.Vertex3(r.xMax, r.yMin, 0f);
        GL.Vertex3(r.xMax, r.yMax, 0f);
        GL.Vertex3(r.xMin, r.yMax, 0f);
        GL.End();
    }

    // A circle lying on the play plane, drawn as a screen-space outline of the given pixel width.
    public static void WorldCircle(Vector3 centre, float radius, Color colour, float width = 2f, int segments = 40, bool dashed = false)
    {
        GL.Begin(GL.QUADS);
        GL.Color(colour);
        for (int i = 0; i < segments; i++)
        {
            if (dashed && (i & 1) == 1)
                continue;
            float a0 = i * Mathf.PI * 2f / segments, a1 = (i + 1) * Mathf.PI * 2f / segments;
            Vector3 w0 = centre + new Vector3(Mathf.Cos(a0), 0f, Mathf.Sin(a0)) * radius;
            Vector3 w1 = centre + new Vector3(Mathf.Cos(a1), 0f, Mathf.Sin(a1)) * radius;
            if (!GameInput.WorldToGui(w0, out Vector2 p0) || !GameInput.WorldToGui(w1, out Vector2 p1))
                continue;
            Vector2 n = (p1 - p0).sqrMagnitude > 0f ? new Vector2(-(p1 - p0).y, (p1 - p0).x).normalized * (width * 0.5f) : Vector2.zero;
            GL.Vertex3(p0.x - n.x, p0.y - n.y, 0f);
            GL.Vertex3(p1.x - n.x, p1.y - n.y, 0f);
            GL.Vertex3(p1.x + n.x, p1.y + n.y, 0f);
            GL.Vertex3(p0.x + n.x, p0.y + n.y, 0f);
        }
        GL.End();
    }

    private static Vector2 Polar(int i, int segments, float r)
    {
        float a = i * Mathf.PI * 2f / segments;
        return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
    }
}
