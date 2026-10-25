using Unity.Mathematics;

// Pointy-top hex grid in axial coordinates (q, r) on the XZ play plane.
// World positions are float2(x, z). Burst-friendly: no managed data.
public static class HexGrid
{
    public const float Sqrt3 = 1.7320508f;

    public static float2 HexToWorld(int2 hex, float size)
    {
        return new float2(size * Sqrt3 * (hex.x + hex.y * 0.5f), size * 1.5f * hex.y);
    }

    public static int2 WorldToHex(float2 position, float size)
    {
        float q = (Sqrt3 / 3f * position.x - position.y / 3f) / size;
        float r = (2f / 3f * position.y) / size;
        return Round(q, r);
    }

    public static int2 Round(float q, float r)
    {
        float s = -q - r;
        float rq = math.round(q), rr = math.round(r), rs = math.round(s);
        float dq = math.abs(rq - q), dr = math.abs(rr - r), ds = math.abs(rs - s);
        if (dq > dr && dq > ds) rq = -rr - rs;
        else if (dr > ds) rr = -rq - rs;
        return new int2((int)rq, (int)rr);
    }

    public static int Distance(int2 a, int2 b)
    {
        int2 d = a - b;
        return (math.abs(d.x) + math.abs(d.y) + math.abs(d.x + d.y)) / 2;
    }

    // Corner i (0..5) relative to the hex centre. Corner 0 is at -30 degrees.
    public static float2 Corner(int i, float size)
    {
        float angle = math.radians(60f * i - 30f);
        return new float2(math.cos(angle), math.sin(angle)) * size;
    }

    // The neighbour across edge i (the edge from corner i to corner i + 1).
    public static int2 EdgeNeighbour(int i)
    {
        switch (i)
        {
            case 0: return new int2(1, 0);
            case 1: return new int2(0, 1);
            case 2: return new int2(-1, 1);
            case 3: return new int2(-1, 0);
            case 4: return new int2(0, -1);
            default: return new int2(1, -1);
        }
    }

    // Five-colour pattern: every hex has a different colour from all six neighbours.
    public static int Colour5(int2 hex)
    {
        return ((hex.x + 2 * hex.y) % 5 + 5) % 5;
    }

    // Deterministic, never zero (Unity.Mathematics.Random needs a non-zero seed).
    public static uint Hash(uint seed, int2 hex)
    {
        return math.hash(new uint3(seed, (uint)hex.x, (uint)hex.y)) | 1u;
    }
}
