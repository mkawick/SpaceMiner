using UnityEngine;

// Display names and colours for each ResourceType (UI only).
public static class ResourceInfo
{
    public static string Name(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Iron: return "Iron";
            case ResourceType.Copper: return "Copper";
            case ResourceType.Silicate: return "Silicate";
            case ResourceType.Ice: return "Ice";
            case ResourceType.Carbon: return "Carbon (polymers)";
            case ResourceType.Gold: return "Gold";
            default: return "Fermite";
        }
    }

    public static Color Colour(ResourceType type)
    {
        switch (type)
        {
            case ResourceType.Iron: return new Color(0.62f, 0.67f, 0.74f);
            case ResourceType.Copper: return new Color(0.86f, 0.47f, 0.22f);
            case ResourceType.Silicate: return new Color(0.84f, 0.78f, 0.60f);
            case ResourceType.Ice: return new Color(0.55f, 0.86f, 1.00f);
            case ResourceType.Carbon: return new Color(0.68f, 0.46f, 0.95f);
            case ResourceType.Gold: return new Color(1.00f, 0.83f, 0.20f);
            default: return new Color(1.00f, 0.27f, 0.58f);
        }
    }
}
