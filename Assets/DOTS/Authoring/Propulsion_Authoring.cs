using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

// Put this on a ship's root. It finds the attachment points by convention:
//
//   Ship
//   └─ Propulsion
//      ├─ Engines      ← every child is a main engine (1 to 5 is fine; names are free)
//      └─ Thrusters    ← every child is a manoeuvring jet (Rear, Front, Left_Front, ...)
//
// Aim each point so its green arrow (local +Y) points where the flame shoots, out of the hull. The game
// works out from that direction and position which moves and turns each nozzle helps with, and
// puts a flame from the Engine Effect Library on every point when the ship spawns.
public class Propulsion_Authoring : MonoBehaviour
{
    [Header("Look")]
    [Tooltip("Faction = blue for Postibule, red for Fairway, green for Grix, white otherwise")]
    public FlameColourChoice colour = FlameColourChoice.Faction;
    [Range(1, 32)] public int engineStyle = 1;
    [Tooltip("Flame shown instead at speed. 0 = none")]
    [Range(0, 32)] public int boostStyle = 3;
    [Range(1, 32)] public int thrusterStyle = 5;
    [Tooltip("Engine flame scale at full throttle; each point's own scale multiplies it")]
    public float engineSize = 4f;
    public float thrusterSize = 2f;

    [Header("Behaviour")]
    [Tooltip("Ship speed (units per second) that shows a full-size flame")]
    public float fullThrustSpeed = 8f;
    [Tooltip("Above this ship speed engines switch to the boost flame. 0 = never")]
    public float boostSpeed = 13f;

    private void OnDrawGizmosSelected()
    {
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            if (t.parent == null) continue;
            bool engine = t.parent.name == "Engines";
            if (!engine && t.parent.name != "Thrusters") continue;
            Gizmos.color = engine ? new Color(1f, 0.55f, 0.1f) : new Color(0.4f, 0.8f, 1f);
            Vector3 exhaust = t.up * (engine ? 1.5f : 0.8f);
            Gizmos.DrawLine(t.position, t.position + exhaust);
            Gizmos.DrawWireSphere(t.position, engine ? 0.2f : 0.12f);
        }
    }

    private class Baker : Baker<Propulsion_Authoring>
    {
        public override void Bake(Propulsion_Authoring a)
        {
            Entity ship = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(ship, new Propulsion
            {
                colour = a.colour,
                engineStyle = (byte)Mathf.Clamp(a.engineStyle, 1, 255),
                boostStyle = (byte)Mathf.Clamp(a.boostStyle, 0, 255),
                thrusterStyle = (byte)Mathf.Clamp(a.thrusterStyle, 1, 255),
                engineSize = Mathf.Max(0.01f, a.engineSize),
                thrusterSize = Mathf.Max(0.01f, a.thrusterSize),
                fullThrustSpeed = Mathf.Max(0.1f, a.fullThrustSpeed),
                boostSpeed = Mathf.Max(0f, a.boostSpeed),
                spawned = false
            });

            DynamicBuffer<PropulsionSocket> sockets = AddBuffer<PropulsionSocket>(ship);
            var all = new List<Transform>();
            GetComponentsInChildren(all);
            foreach (Transform t in all)
            {
                if (t.parent == null)
                    continue;
                PropulsionKind kind;
                if (t.parent.name == "Engines") kind = PropulsionKind.Engine;
                else if (t.parent.name == "Thrusters") kind = PropulsionKind.Thruster;
                else continue;
                sockets.Add(new PropulsionSocket { socket = GetEntity(t, TransformUsageFlags.Dynamic), kind = kind });
            }
        }
    }
}
