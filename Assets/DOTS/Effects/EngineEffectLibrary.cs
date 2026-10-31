using System;
using System.Collections.Generic;
using UnityEngine;

// Game-wide list of engine flame prefabs, looked up at runtime by style and colour.
// Lives at Assets/Settings/EngineEffectLibrary.asset (Tools > Spacey > Engine Effect Library);
// "Refresh from pack" fills it from the SimpleSpaceshipEngineEffects prefabs. Add your own
// flames with style 7 and up. Baked into the subscene by GameLibrary_Authoring.
[CreateAssetMenu(menuName = "Spacey/Engine Effect Library", fileName = "EngineEffectLibrary")]
public class EngineEffectLibrary : ScriptableObject
{
    public const string DefaultPath = "Assets/Settings/EngineEffectLibrary.asset";
    public const string PackFolder = "Assets/Models/SimpleSpaceshipEngineEffects/Prefabs";

    [Serializable]
    public struct Entry
    {
        [Range(1, 32)] public int style;
        public FlameColour colour;
        public GameObject prefab;
    }

    public List<Entry> entries = new List<Entry>();
}
