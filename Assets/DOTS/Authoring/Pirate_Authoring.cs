using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

// Put this on a pirate ship prefab's root. Guns are the children whose names start with
// "FiringPoint". Health and hit radius come from a Targetable_Authoring if the ship has one,
// otherwise from the values here; either way the ship belongs to the Pirates faction.
// Add Propulsion_Authoring too for engine flames (pirates burn yellow).
public class Pirate_Authoring : MonoBehaviour
{
    [Header("Flight")]
    public float maxSpeed = 14f;
    public float acceleration = 10f;
    public float turnSpeedDegrees = 110f;

    [Header("Fighting")]
    public WeaponId weapon = WeaponId.PulseLaser;
    [Tooltip("Distance it circles its target at")]
    public float preferredRange = 45f;
    [Tooltip("How far it looks for targets")]
    public float sightRange = 400f;
    [Tooltip("Shoots when the target is within this many degrees of its nose")]
    public float fireArcDegrees = 25f;
    [Tooltip("Runs away below this fraction of its health, and escapes once clear")]
    [Range(0f, 1f)] public float fleeBelowHealth = 0.25f;

    [Header("Used when there is no Targetable component")]
    public float maxHealth = 80f;
    [Tooltip("0 = work it out from the renderers")]
    public float hitRadius = 0f;

    private class Baker : Baker<Pirate_Authoring>
    {
        public override void Bake(Pirate_Authoring a)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);

            AddComponent(entity, new PirateSettings
            {
                maxSpeed = Mathf.Max(0.1f, a.maxSpeed),
                acceleration = Mathf.Max(0.1f, a.acceleration),
                turnSpeed = math.radians(Mathf.Max(1f, a.turnSpeedDegrees)),
                preferredRange = Mathf.Max(1f, a.preferredRange),
                sightRange = Mathf.Max(a.preferredRange, a.sightRange),
                fireArc = math.radians(Mathf.Clamp(a.fireArcDegrees, 1f, 180f)),
                fleeBelow = Mathf.Clamp01(a.fleeBelowHealth),
                weapon = a.weapon
            });
            AddComponent(entity, new PirateAI { state = PirateState.Hunt, orbitSign = 1f });

            float radius = a.hitRadius > 0f ? a.hitRadius : Targetable_Authoring.RadiusFromRenderers(a.transform);
            DynamicBuffer<PirateGun> guns = AddBuffer<PirateGun>(entity);
            var children = new List<Transform>();
            GetComponentsInChildren(children);
            foreach (Transform t in children)
                if (t != a.transform && t.name.StartsWith("FiringPoint"))
                    guns.Add(new PirateGun { localPosition = a.transform.InverseTransformPoint(t.position) });
            if (guns.Length == 0)
                guns.Add(new PirateGun { localPosition = new float3(0f, 0f, radius) });

            if (GetComponent<Targetable_Authoring>() == null)
            {
                AddComponent(entity, new Targetable
                {
                    layer = CollisionLayer.Ship,
                    faction = CombatFaction.Pirates,
                    radius = radius,
                    density = 0f
                });
                float health = Mathf.Max(1f, a.maxHealth);
                AddComponent(entity, new Health { current = health, max = health });
            }
        }
    }
}
