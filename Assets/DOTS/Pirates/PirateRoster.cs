using System.Collections.Generic;
using UnityEngine;

// Which pirate parties can appear, and when. Lives at Assets/Settings/Pirates/PirateRoster.asset
// (Tools > Spacey > Pirate Roster). Baked by GameLibrary_Authoring.
[CreateAssetMenu(menuName = "Spacey/Pirate Roster", fileName = "PirateRoster")]
public class PirateRoster : ScriptableObject
{
    public const string DefaultPath = "Assets/Settings/Pirates/PirateRoster.asset";

    public bool enabled = true;
    [Tooltip("Seconds after the start before the first wave. F9 sends one immediately.")]
    [Min(0f)] public float firstWaveDelay = 90f;
    [Min(5f)] public float waveInterval = 180f;
    [Tooltip("How far from the player a wave appears")]
    [Min(50f)] public float spawnDistance = 350f;
    [Tooltip("No new waves while this many pirates are alive")]
    [Min(1)] public int maxAlive = 8;

    [Tooltip("Each wave picks one of these at random, weighted by its Chance")]
    public List<PirateParty> parties = new List<PirateParty>();
}
