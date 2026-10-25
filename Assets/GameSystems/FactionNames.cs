// What each faction calls the same building or unit. Code uses the IDs; the UI shows these names.
// Grix speak Croatian, the Fairway Dominion German, the Postibule Sanctuary Swedish.
public static class FactionNames
{
    public static string Name(BuildingId id, CombatFaction faction)
    {
        switch (id)
        {
            case BuildingId.WorkerFactory:
                return Pick(faction, grix: "Tvornica", fairway: "Roboterstube", postibule: "Robotkammare", english: "Worker Factory");
            default:
                return id.ToString();
        }
    }

    public static string Name(UnitId id, CombatFaction faction)
    {
        switch (id)
        {
            case UnitId.WorkerBot:
                // Placeholder names, to be confirmed.
                return Pick(faction, grix: "Radnik", fairway: "Arbeiter", postibule: "Arbetare", english: "Worker");
            default:
                return id.ToString();
        }
    }

    public static string English(BuildingId id)
    {
        switch (id)
        {
            case BuildingId.WorkerFactory: return "Worker Factory";
            default: return id.ToString();
        }
    }

    public static string English(UnitId id)
    {
        switch (id)
        {
            case UnitId.WorkerBot: return "Worker";
            default: return id.ToString();
        }
    }

    private static string Pick(CombatFaction faction, string grix, string fairway, string postibule, string english)
    {
        switch (faction)
        {
            case CombatFaction.Grix: return grix;
            case CombatFaction.FairwayDominion: return fairway;
            case CombatFaction.PostibuleSanctuary: return postibule;
            default: return english;
        }
    }
}
