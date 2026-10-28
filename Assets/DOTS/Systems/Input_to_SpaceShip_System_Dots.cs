using Unity.Collections;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

// Reads the keyboard and writes it into the player ship's InputData_Dots.
// W/S thrust, A/D strafe, Q/E rotate. 1/2/3 pick a weapon slot; Space fires the active weapon.
// The mouse is free for selecting (left) and giving orders (right).
// moveDir = (strafe, thrust), lookDir.x = rotation (-1 Q, +1 E).
public partial class Input_to_SpaceShip_System_Dots : SystemBase
{
    private InputControls input;

    protected override void OnCreate()
    {
        input = new InputControls();
        input.Enable();
        RequireForUpdate<InputData_Dots>();
    }

    protected override void OnDestroy()
    {
        if (input != null)
        {
            input.Disable();
            input.Dispose();
        }
    }

    protected override void OnUpdate()
    {
        float2 move = input.Player.Move.ReadValue<Vector2>();
        Keyboard keyboard = Keyboard.current;
        float rotate = 0f;
        bool firing = false;
        int pickSlot = -1;
        if (keyboard != null)
        {
            if (keyboard.qKey.isPressed) rotate -= 1f;
            if (keyboard.eKey.isPressed) rotate += 1f;
            firing = keyboard.spaceKey.isPressed;
            if (keyboard.digit1Key.wasPressedThisFrame) pickSlot = 0;
            if (keyboard.digit2Key.wasPressedThisFrame) pickSlot = 1;
            if (keyboard.digit3Key.wasPressedThisFrame) pickSlot = 2;
        }
        float now = (float)SystemAPI.Time.ElapsedTime;

        bool hasCatalog = SystemAPI.HasSingleton<WeaponCatalog>();
        DynamicBuffer<WeaponDef> catalog = hasCatalog
            ? SystemAPI.GetBuffer<WeaponDef>(SystemAPI.GetSingletonEntity<WeaponCatalog>())
            : default;

        var ecb = new EntityCommandBuffer(Allocator.Temp);

        foreach (var (config, status, inputData, transform, active, targetable, entity) in
                 SystemAPI.Query<RefRO<SpaceShipControllerData_Dots>,
                                 RefRO<SpaceShipStatus_Dots>,
                                 RefRW<InputData_Dots>,
                                 RefRO<LocalTransform>,
                                 RefRW<ActiveWeapon>,
                                 RefRO<Targetable>>()
                          .WithEntityAccess())
        {
            inputData.ValueRW.moveDir = move;
            inputData.ValueRW.lookDir = new float2(rotate, 0f);

            DynamicBuffer<WeaponSlot> slots = SystemAPI.GetBuffer<WeaponSlot>(entity);
            if (pickSlot >= 0 && pickSlot < slots.Length)
                active.ValueRW.slot = pickSlot;
            int slotIndex = active.ValueRO.slot;
            if (!firing || slotIndex < 0 || slotIndex >= slots.Length)
                continue;

            WeaponSlot slot = slots[slotIndex];
            if (now < slot.readyTime)
                continue;

            WeaponDef def = WeaponData.Find(slot.id, hasCatalog, catalog);
            LocalTransform t = transform.ValueRO;
            Entity request = ecb.CreateEntity();
            ecb.AddComponent(request, new ProjectileRequest
            {
                weapon = slot.id,
                owner = entity,
                faction = targetable.ValueRO.faction,
                position = t.TransformPoint(config.ValueRO.firingPoint),
                dir = t.Rotation,
                speedBonus = status.ValueRO.speed
            });
            slot.interval = math.max(0.01f, def.fireInterval);
            slot.readyTime = now + slot.interval;
            slots[slotIndex] = slot;
        }

        ecb.Playback(EntityManager);
        ecb.Dispose();
    }
}
