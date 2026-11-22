using Unity.Entities;

// What's switched on for this session: which unit and building types exist and act, pirates,
// and test hotkeys. Written every frame by the Bot Config component on the GameSession object,
// read by the systems and the UI. Without a Bot Config everything is on (GameRules.Default).
public struct GameRules : IComponentData
{
    // Pirates
    public bool pirates;              // off = no waves, and pirates already out there freeze
    public bool pirateHotkey;         // F9 sends a wave
    public float pirateFirstWave;     // seconds before the first wave; < 0 = use the Pirate Roster
    public float pirateWaveInterval;  // < 0 = use the Pirate Roster

    // The player's units. Off = can't be built or trained, and existing ones stop acting.
    public bool allBots;
    public bool miners;
    public bool builders;
    public bool defenders;
    public bool attackers;
    public bool defenceShips;
    public bool attackShips;

    // Buildings. Off = greyed out in the build menu and refused if requested.
    public bool workerFactory;
    public bool storageDepot;
    public bool refinery;

    // Test tools
    public bool trainingDrones; // F8

    public static GameRules Default => new GameRules
    {
        pirates = true, pirateHotkey = true, pirateFirstWave = -1f, pirateWaveInterval = -1f,
        allBots = true, miners = true, builders = true, defenders = true, attackers = true,
        defenceShips = true, attackShips = true,
        workerFactory = true, storageDepot = true, refinery = true,
        trainingDrones = true
    };

    public bool Allows(UnitId unit)
    {
        if (!allBots)
            return false;
        switch (unit)
        {
            case UnitId.WorkerBot: return miners;
            case UnitId.BuilderBot: return builders;
            case UnitId.DefenderBot: return defenders;
            case UnitId.AttackerBot: return attackers;
            case UnitId.DefenceShip: return defenceShips;
            case UnitId.AttackShip: return attackShips;
            default: return true;
        }
    }

    public bool Allows(BuildingId building)
    {
        switch (building)
        {
            case BuildingId.WorkerFactory: return workerFactory;
            case BuildingId.Storage: return storageDepot;
            case BuildingId.Refinery: return refinery;
            default: return true;
        }
    }
}
