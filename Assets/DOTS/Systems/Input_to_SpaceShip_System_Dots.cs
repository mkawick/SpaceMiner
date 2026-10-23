using System;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEditor.PackageManager;
using UnityEditor.Rendering.LookDev;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using static UnityEngine.EventSystems.EventTrigger;
using static UnityEngine.InputSystem.HID.HID;

[RequireMatchingQueriesForUpdate]
public partial class Input_to_SpaceShip_System_Dots : SystemBase
{
    private InputControls input;

    protected override void OnCreate()
    {
        base.OnCreate();

        input = new InputControls();
        input.Enable();
    }

    protected override void OnUpdate()
    {
        EntityCommandBuffer ecb = new EntityCommandBuffer(Allocator.TempJob);

        {//-------------------------------
            float3 firingPoint;
            float currectElapsedTime = (float) SystemAPI.Time.ElapsedTime;
            foreach (var (spaceshipConfig, spaceshipStats, transform) in 
                SystemAPI.Query<RefRO<SpaceShipControllerData_Dots>, 
                RefRW<SpaceShipStatus_Dots>,
                RefRW<LocalTransform>>())
            {
                firingPoint = spaceshipConfig.ValueRO.firingPoint + transform.ValueRO.Position;

                // TODO: check the last time sent
                foreach (RefRW<InputData_Dots> data in SystemAPI.Query<RefRW<InputData_Dots>>())
                {
                    ecb = PlayerMove(ecb, data);
                    ecb = PlayerFire(ecb, data, firingPoint, spaceshipStats, currectElapsedTime);
                }
            }
        }//-------------------------------

        Dependency.Complete();
        ecb.Playback(EntityManager);
        ecb.Dispose();
    }

    private EntityCommandBuffer PlayerFire(
        EntityCommandBuffer ecb, 
        RefRW<InputData_Dots> data, 
        float3 firingPoint, 
        RefRW<SpaceShipStatus_Dots> spaceshipStats, 
        float currectElapsedTime)
    {
        if (input.Player.Attack.ReadValue<float>() > 0 && currectElapsedTime > spaceshipStats.ValueRO.nextTimeToFire)
        {
            //Debug.Log("<b> <size=13> <color=#9D55AA>input attack</color> </size> </b>");
            //Debug.Log("input attack");
            var e = ecb.CreateEntity();
            Unity.Mathematics.float3 position = new Unity.Mathematics.float3(0, 0, 0);
            //firingPoint.
            //if (firingPoint.x != 0 && firingPoint.y != 0 && firingPoint.z != 0)
            {
                position = firingPoint;
            }

            // TODO: check the last time sent
            Unity.Mathematics.quaternion direction = Unity.Mathematics.quaternion.EulerXYZ(0, 0, Mathf.Deg2Rad * 90);
            ecb.AddComponent(e, new ProjectileRequest
            {
                projectileEnum = 0,
                damageIfHits = 1,
                ownerId = 42,
                position = position,
                dir = direction,
                speed = 4
            });

            spaceshipStats.ValueRW.nextTimeToFire = currectElapsedTime + spaceshipStats.ValueRO.fireRate;
            //Debug.Log("input ProjectileRequest created");
            //Debug.Log("<b> <size=13> <color=#9D55AA>input ProjectileRequest created</color> </size> </b>");
        }
        return ecb;
    }

    private EntityCommandBuffer PlayerMove(EntityCommandBuffer ecb, RefRW<InputData_Dots> data)
    {
        // we also have C=Crouch, E=Interact, Space=Jump, Attack=left mouse button
        // and Look(RightMouse)

        data.ValueRW.moveDir = input.Player.Move.ReadValue<Vector2>();
        if (input.Player.RightClick.ReadValue<float>() > 0)
        {
            data.ValueRW.lookDir = input.Player.Look.ReadValue<Vector2>();
        }
        

        return ecb;
    }
}
