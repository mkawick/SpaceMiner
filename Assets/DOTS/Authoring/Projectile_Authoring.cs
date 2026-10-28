using Unity.Entities;
using UnityEngine;

// On a projectile prefab. Weapons overwrite these values when they fire (see WeaponDef).
class Projectile_Authoring : MonoBehaviour
{
    public float speed;
    public int damage;
    public CollisionLayer layer = CollisionLayer.Projectile;
    class Projectile_AuthoringBaker : Baker<Projectile_Authoring>
    {
        public override void Bake(Projectile_Authoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Projectile
            {
                speed = authoring.speed,
                hullDamage = authoring.damage,
                rockDamage = authoring.damage,
                owner = Entity.Null,
                faction = CombatFaction.None,
                layer = authoring.layer
            });
        }
    }
}

public struct Projectile : IComponentData
{
    public float speed;
    public float hullDamage;
    public float rockDamage;
    public Entity owner;           // never hits its own shooter
    public CombatFaction faction;  // never hits this faction
    public CollisionLayer layer;   // the Layer Matrix decides what it hits
}
