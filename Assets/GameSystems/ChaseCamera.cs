using Unity.Entities;
using Unity.Transforms;
using UnityEngine;
using UnityEngine.InputSystem;

// Spacey chase camera. Follows the player's ship (the entity with InputData_Dots) from a
// three-quarter view. Hold the right mouse button to orbit and tilt; use the mouse wheel to zoom
// from a bird's-eye view down to close behind the ship.
// It attaches itself to the main camera when play starts; add it by hand to tune the values.
public class ChaseCamera : MonoBehaviour
{
    [Header("Zoom (mouse wheel)")]
    public float distance = 30f;
    public float minDistance = 6f;
    public float maxDistance = 150f;
    [Range(0.02f, 0.5f)] public float zoomStep = 0.12f;

    [Header("Orbit (hold right mouse)")]
    public float pitch = 35f;
    public float minPitch = 8f;
    public float maxPitch = 85f;
    public float orbitSensitivity = 0.2f;
    [Tooltip("How fast the camera swings back behind the ship after orbiting. 0 = stay where you left it.")]
    public float recenterSpeed = 1.5f;

    [Header("Follow")]
    public float followSharpness = 10f;
    public float lookHeight = 1.5f;

    private float orbitYaw;
    private World queryWorld;
    private EntityQuery playerQuery;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void AttachToMainCamera()
    {
        Camera cam = Camera.main;
        if (cam != null && cam.GetComponent<ChaseCamera>() == null)
            cam.gameObject.AddComponent<ChaseCamera>();
    }

    private bool TryGetPlayer(out LocalToWorld playerTransform)
    {
        playerTransform = default;
        World world = World.DefaultGameObjectInjectionWorld;
        if (world == null || !world.IsCreated)
            return false;

        if (queryWorld != world)
        {
            playerQuery = world.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<InputData_Dots>(),
                ComponentType.ReadOnly<LocalToWorld>());
            queryWorld = world;
        }

        if (playerQuery.CalculateEntityCount() != 1)
            return false;

        playerTransform = playerQuery.GetSingleton<LocalToWorld>();
        return true;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        Mouse mouse = Mouse.current;
        if (mouse != null)
        {
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll != 0f)
                distance = Mathf.Clamp(distance * (1f - zoomStep * Mathf.Sign(scroll)), minDistance, maxDistance);

            if (mouse.rightButton.isPressed)
            {
                Vector2 delta = mouse.delta.ReadValue();
                orbitYaw += delta.x * orbitSensitivity;
                pitch = Mathf.Clamp(pitch - delta.y * orbitSensitivity, minPitch, maxPitch);
            }
            else if (recenterSpeed > 0f)
            {
                orbitYaw = Mathf.LerpAngle(orbitYaw, 0f, 1f - Mathf.Exp(-recenterSpeed * dt));
            }
        }

        if (!TryGetPlayer(out LocalToWorld ship))
            return;

        Vector3 shipPosition = ship.Position;
        Vector3 forward = ship.Forward;
        forward.y = 0f;
        float shipYaw = forward.sqrMagnitude > 1e-4f ? Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg : 0f;

        Quaternion view = Quaternion.Euler(pitch, shipYaw + orbitYaw, 0f);
        Vector3 target = shipPosition + Vector3.up * lookHeight;
        Vector3 desired = target - view * Vector3.forward * distance;

        float blend = 1f - Mathf.Exp(-followSharpness * dt);
        transform.position = Vector3.Lerp(transform.position, desired, blend);
        transform.rotation = Quaternion.LookRotation(target - transform.position, Vector3.up);
    }

    private void OnDestroy()
    {
        if (queryWorld != null && queryWorld.IsCreated)
            playerQuery.Dispose();
    }
}
