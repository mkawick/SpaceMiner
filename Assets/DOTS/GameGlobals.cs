using System;
using Unity.Entities;
using UnityEngine;

[Flags]
public enum CombatFaction : byte
{
    None    = 0,
    Grix = 1 << 0, // 1          // green
    PostibuleSanctuary = 1 << 1, // 2   // Blue
    FairwayDominion  = 1 << 2, // 4   // red
    All = 0xFF   // Helper to target everything
}



public enum GameConstants : uint
{
    grixLayer = 1 << 12,
    postibuleSanctuaryLayer = 1 << 13,
    fairwayDominionLayer = 1 << 14,
    asteroidsLayer = 1 << 20,
    unitsLayer = 6u,
    layerMask = (uint)1 << (int)unitsLayer,
    allLayersMask = ~0u
}

public class GameUtils
{

    public static bool IsOnCoolDown(ref float nextScanTime, 
        float currentTime, 
        float coolDown)
    {
        if (nextScanTime > currentTime)
            return true;
        nextScanTime = currentTime + coolDown;
        return false;
    }
}