using Unity.Entities;
using Unity.Mathematics;
using UnityEngine;

class Projectile_Authoring : MonoBehaviour
{
    public float speed;
    public int damage;
    class Projectile_AuthoringBaker : Baker<Projectile_Authoring>
    {
        public override void Bake(Projectile_Authoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Projectile
            {
                speed = authoring.speed,
                damage = authoring.damage,
                owner = Entity.Null
            });
        }
    }
}

public struct Projectile : IComponentData
{
    public float speed;
    public int damage;
    public Entity owner;
}

/*
public struct ShootAttack : IComponentData
{
    public float interval;
    public float nextFireTime;
    
    public int attackDamage;
    public float attackRange;
    public float3 projectileSpawnOffset;
    public OnShootEvent onShoot;
    
    public struct OnShootEvent
    {
        public bool isTriggered;
        public float3 projectileSpawnPosition;
        
    }
}*/