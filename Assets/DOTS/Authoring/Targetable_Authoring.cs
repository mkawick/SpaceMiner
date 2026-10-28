using Unity.Entities;
using UnityEngine;

// Makes an object shootable and targetable. Add it to a prefab (ship, bot, building, asteroid)
// or a scene object and pick its collision layer. Objects spawned by the game without one get
// sensible defaults in code; with one, these values win (the game still fills in the faction).
[DisallowMultipleComponent]
public class Targetable_Authoring : MonoBehaviour
{
    [Tooltip("Which layer this object is on. The Layer Matrix decides what hits it and what can target it.")]
    public CollisionLayer layer = CollisionLayer.Ship;
    [Tooltip("Hit radius on the play plane. 0 = work it out from the renderers.")]
    public float radius = 0f;
    [Tooltip("0 = thin hull (combat weapons hurt it), 1 = solid rock (only mining weapons do real damage)")]
    [Range(0f, 1f)] public float density = 0f;
    public float maxHealth = 100f;
    [Tooltip("None = filled in by the game when spawned (the player's or the builder's faction)")]
    public CombatFaction faction = CombatFaction.None;

    public float EffectiveRadius()
    {
        if (radius > 0f)
            return radius;
        float r = 0f;
        Vector3 centre = transform.position;
        foreach (Renderer renderer in GetComponentsInChildren<Renderer>())
        {
            Bounds b = renderer.bounds;
            float dx = Mathf.Max(Mathf.Abs(b.min.x - centre.x), Mathf.Abs(b.max.x - centre.x));
            float dz = Mathf.Max(Mathf.Abs(b.min.z - centre.z), Mathf.Abs(b.max.z - centre.z));
            r = Mathf.Max(r, Mathf.Max(dx, dz));
        }
        return r > 0f ? r : 1f;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.55f, 0.15f, 0.9f);
        Gizmos.DrawWireSphere(transform.position, EffectiveRadius());
    }

    private class Baker : Baker<Targetable_Authoring>
    {
        public override void Bake(Targetable_Authoring a)
        {
            Entity entity = GetEntity(TransformUsageFlags.Dynamic);
            AddComponent(entity, new Targetable
            {
                layer = a.layer,
                faction = a.faction,
                radius = a.EffectiveRadius(),
                density = Mathf.Clamp01(a.density)
            });
            float health = Mathf.Max(1f, a.maxHealth);
            AddComponent(entity, new Health { current = health, max = health });
        }
    }
}
