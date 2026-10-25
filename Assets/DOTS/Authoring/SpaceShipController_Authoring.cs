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
    [Tooltip("Top strafe (A/D) speed as a fraction of top speed")]
    public float strafeSpeedFactor = 0.6f;
    [Tooltip("Projectile speed in units per second, on top of the ship's speed")]
    public float projectileSpeed = 60f;
    [Tooltip("How far this ship's radar reaches, in units; the minimap shows this far")]
    public float radarRange = 1200f;

    [Header("Ore collector")]
    [Tooltip("Ore within this distance starts flying to the ship")]
    public float collectorRadius = 12f;
    [Tooltip("Ore this close to the ship is picked up")]
    public float collectorPickupDistance = 1.5f;
    [Tooltip("How quickly pulled-in ore speeds up, units per second squared")]
    public float collectorPullAcceleration = 60f;
    [Tooltip("Top speed of pulled-in ore; keep it above the ship's top speed")]
    public float collectorMaxPullSpeed = 40f;
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
    public float strafeSpeedFactor;
    public float projectileSpeed;
    public Entity firingObject;
    public float3 firingPoint;
    public float maxHealth;
}

public struct SpaceShipStatus_Dots : IComponentData
{
    public float health;
    public float shield;
    public float speed;       // forward speed
    public float strafeSpeed; // sideways speed, + is right

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
                    strafeSpeedFactor = authoring.strafeSpeedFactor,
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

            AddComponent(spaceshipEntity, new ShipRadar { range = authoring.radarRange });
            AddComponent(spaceshipEntity, new OreCollector
            {
                radius = authoring.collectorRadius,
                pickupDistance = authoring.collectorPickupDistance,
                pullAcceleration = authoring.collectorPullAcceleration,
                maxPullSpeed = authoring.collectorMaxPullSpeed
            });

            // Cargo hold: one slot per ResourceType.
            DynamicBuffer<CargoItem> cargo = AddBuffer<CargoItem>(spaceshipEntity);
            for (int i = 0; i < AsteroidFieldRules.ResourceCount; i++)
                cargo.Add(new CargoItem { amount = 0f });

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