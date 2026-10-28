using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

// Optional: put this on any object in the subscene to tune weapons in the Inspector.
// Without it the built-in values in WeaponData.Default are used. Reset fills in those defaults.
public class WeaponCatalog_Authoring : MonoBehaviour
{
    public List<WeaponDef> weapons = new List<WeaponDef>();

    private void Reset()
    {
        weapons.Clear();
        for (int i = 0; i < WeaponData.Count; i++)
            weapons.Add(WeaponData.Default((WeaponId)i));
    }

    private class Baker : Baker<WeaponCatalog_Authoring>
    {
        public override void Bake(WeaponCatalog_Authoring authoring)
        {
            Entity entity = GetEntity(TransformUsageFlags.None);
            AddComponent<WeaponCatalog>(entity);
            DynamicBuffer<WeaponDef> buffer = AddBuffer<WeaponDef>(entity);
            foreach (WeaponDef def in authoring.weapons)
                buffer.Add(def);
        }
    }
}
