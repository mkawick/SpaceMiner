using System;
using System.Collections.Generic;
using UnityEngine;

// One kind of pirate party a wave can bring. Create with Assets > Create > Spacey > Pirate Party,
// then add it to the Pirate Roster. Each wave picks a party at random, weighted by Chance: the
// chances are compared with each other, so they don't need to add up to 100.
[CreateAssetMenu(menuName = "Spacey/Pirate Party", fileName = "PirateParty")]
public class PirateParty : ScriptableObject
{
    [Serializable]
    public struct ShipEntry
    {
        [Tooltip("A pirate ship prefab (needs Pirate_Authoring)")]
        public GameObject prefab;
        [Min(0)] public int min;
        [Min(0)] public int max;
    }

    [Tooltip("How likely this party is compared with the others in the roster")]
    [Min(0f)] public float chance = 25f;

    [Header("What they want")]
    public PirateGoal goal = PirateGoal.Raid;
    [Tooltip("0 = cautious (shorter sight, keeps its distance, runs early), 1 = reckless")]
    [Range(0f, 1f)] public float aggression = 0.5f;
    [Tooltip("Scavengers and slavers leave once they have this much: 1 per piece of ore, 5 per captured bot. 0 = stay until beaten")]
    [Min(0f)] public float lootToLeave = 10f;

    [Header("Ships")]
    public List<ShipEntry> ships = new List<ShipEntry>();
    public PartyFormation formation = PartyFormation.Vee;
    [Tooltip("Gap between ships in units. 0 = work it out from the ships' size")]
    [Min(0f)] public float spacing = 0f;

    [TextArea(2, 5)] public string notes;
}
