using UnityEngine;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

public class SpaceShipAuthoringComponent_Dots : MonoBehaviour
{
    [Tooltip("Top forward speed in units per second")]
    public float speed = 2f;
    [Tooltip("Units per second gained per second of thrust")]
    public float acceleration = 12f;
    [Tooltip("Turn rate in degrees per second")]
    public float turnSpeedDegrees = 120f;
    [Tooltip("How quickly the ship slows down with no thrust")]
    public float drag = 1.5f;
    [Tooltip("Top reverse speed as a fraction of top speed")]
    public float reverseSpeedFactor = 0.4f;
    [Tooltip("Projectile speed in units per second, on top of the ship's speed")]
    public float projectileSpeed = 60f;
    //public GameObject spaceship;
    public GameObject weapon;
}


public struct SpaceShipControllerData_Dots : IComponentData
{
    public float maxSpeed;
    public float acceleration;
    public float turnSpeed; // radians per second
    public float drag;
    public float reverseSpeedFactor;
    public float projectileSpeed;
    public Entity firingObject;
    public float3 firingPoint;
    public float maxHealth;
}

public struct SpaceShipStatus_Dots : IComponentData
{
    public float health;
    public float shield;
    public float speed;

    public float nextTimeToFire; // move to weapon
    public float weaponSpeed; // move to weapon
    public float fireRate; // move to weapon
}

// SpaceShipController_Authoring
[WorldSystemFilter(WorldSystemFilterFlags.BakingSystem)]
public class SpaceShipBaker_Dots : Baker<SpaceShipAuthoringComponent_Dots>
{
    public override void Bake(SpaceShipAuthoringComponent_Dots authoring)
    {
        if (authoring.weapon == null)// for in-editor errors
            return;

        var newPosition = authoring.transform;
        var spaceshipEntity = GetEntity(TransformUsageFlags.Dynamic);
        if (spaceshipEntity != null)
        {
            Entity weaponPrefab = CreateAdditionalEntity(TransformUsageFlags.Dynamic, false, "weaponPrefab");
            //Entity weaponPrefab = GetEntity(authoring.weapon, TransformUsageFlags.Dynamic);
            Vector3 firingPosition;
            if (weaponPrefab != null)
            {
                firingPosition = authoring.weapon.transform.localPosition;
                AddComponent(spaceshipEntity, new SpaceShipControllerData_Dots
                {
                    maxSpeed = authoring.speed,
                    acceleration = authoring.acceleration,
                    turnSpeed = math.radians(authoring.turnSpeedDegrees),
                    drag = authoring.drag,
                    reverseSpeedFactor = authoring.reverseSpeedFactor,
                    projectileSpeed = authoring.projectileSpeed,
                    firingPoint = firingPosition,
                    firingObject = weaponPrefab,
                    maxHealth = 100,
                });
                
                AddComponent(weaponPrefab, new Unity.Transforms.Parent()
                {
                    Value = spaceshipEntity
                });
            }
            
            // the position
            AddComponent(spaceshipEntity, LocalTransform.FromPosition(newPosition.position));
            AddComponent(spaceshipEntity, new InputData_Dots 
            { 
            });

            AddComponent(spaceshipEntity, new SpaceShipStatus_Dots
            {
                speed = 0,
                nextTimeToFire = 0,
                shield = 0,
                health = 100,
                fireRate = 0.35f
            });

          /*  go.transform.position = Vector3.zero;
            if (Application.isPlaying)// to ensure that the projectile is active when spawning
            {
                go.SetActive(true);
            }*/
        }
    }
}