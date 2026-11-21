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
            case BuildingId.Storage:
                return Pick(faction, grix: "Skladište", fairway: "Lager", postibule: "Förråd", english: "Storage Depot");
            default:
                return id.ToString();
        }
    }

    public static string Name(UnitId id, CombatFaction faction)
    {
        switch (id)
        {
            // Proposed names, to be confirmed.
            case UnitId.WorkerBot:   return Pick(faction, grix: "Rudar", fairway: "Schürfer", postibule: "Grävare", english: "Miner");
            case UnitId.BuilderBot:  return Pick(faction, grix: "Graditelj", fairway: "Baumeister", postibule: "Byggare", english: "Builder");
            case UnitId.DefenderBot: return Pick(faction, grix: "Čuvar", fairway: "Verteidiger", postibule: "Försvarare", english: "Defender");
            case UnitId.AttackerBot: return Pick(faction, grix: "Napadač", fairway: "Angreifer", postibule: "Anfallare", english: "Attacker");
            case UnitId.DefenceShip: return Pick(faction, grix: "Stražar", fairway: "Wächter", postibule: "Väktare", english: "Defence ship");
            case UnitId.AttackShip:  return Pick(faction, grix: "Jurišnik", fairway: "Stürmer", postibule: "Stormare", english: "Attack ship");
            default:
                return id.ToString();
        }
    }

    public static string English(BuildingId id)
    {
        switch (id)
        {
            case BuildingId.WorkerFactory: return "Bot Factory";
            case BuildingId.Storage: return "Storage Depot";
            default: return id.ToString();
        }
    }

    public static string English(UnitId id)
    {
        switch (id)
        {
            case UnitId.WorkerBot: return "Miner";
            case UnitId.BuilderBot: return "Builder";
            case UnitId.DefenderBot: return "Defender";
            case UnitId.AttackerBot: return "Attacker";
            case UnitId.DefenceShip: return "Defence ship";
            case UnitId.AttackShip: return "Attack ship";
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
