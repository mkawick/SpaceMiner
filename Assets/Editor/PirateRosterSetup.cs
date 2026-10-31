using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

// Creates the Pirate Roster with three example parties the first time, and opens it from the menu.
public static class PirateRosterSetup
{
    private const string Folder = "Assets/Settings/Pirates";
    private const string DefaultShip = "Assets/Prefabs/Ships/PirateShip.prefab";

    [MenuItem("Tools/Spacey/Pirate Roster")]
    private static void Open()
    {
        Selection.activeObject = FindOrCreate();
    }

    [InitializeOnLoadMethod]
    private static void EnsureAsset()
    {
        EditorApplication.delayCall += () => FindOrCreate();
    }

    public static PirateRoster FindOrCreate()
    {
        var roster = AssetDatabase.LoadAssetAtPath<PirateRoster>(PirateRoster.DefaultPath);
        if (roster != null)
            return roster;

        if (!AssetDatabase.IsValidFolder("Assets/Settings"))
            AssetDatabase.CreateFolder("Assets", "Settings");
        if (!AssetDatabase.IsValidFolder(Folder))
            AssetDatabase.CreateFolder("Assets/Settings", "Pirates");

        var ship = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultShip);
        roster = ScriptableObject.CreateInstance<PirateRoster>();
        roster.parties = new List<PirateParty>
        {
            MakeParty("Raiders", 50f, PirateGoal.Raid, 0.6f, 0f, PartyFormation.Vee, ship, 2, 3,
                      "Hit stations and worker bots, fight anyone who shows up."),
            MakeParty("Scavengers", 30f, PirateGoal.Scavenge, 0.25f, 12f, PartyFormation.Line, ship, 1, 2,
                      "Shadow miners and steal loose ore. Only fight back when shot."),
            MakeParty("Slavers", 20f, PirateGoal.Slavers, 0.5f, 10f, PartyFormation.Column, ship, 2, 2,
                      "Disable worker bots and carry them off; leave after two captures."),
        };
        AssetDatabase.CreateAsset(roster, PirateRoster.DefaultPath);
        AssetDatabase.SaveAssets();
        return roster;
    }

    private static PirateParty MakeParty(string name, float chance, PirateGoal goal, float aggression, float loot,
                                         PartyFormation formation, GameObject ship, int min, int max, string notes)
    {
        var party = ScriptableObject.CreateInstance<PirateParty>();
        party.chance = chance;
        party.goal = goal;
        party.aggression = aggression;
        party.lootToLeave = loot;
        party.formation = formation;
        party.notes = notes;
        if (ship != null)
            party.ships.Add(new PirateParty.ShipEntry { prefab = ship, min = min, max = max });
        AssetDatabase.CreateAsset(party, $"{Folder}/{name}.asset");
        return party;
    }
}
