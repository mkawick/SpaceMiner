using UnityEngine;

// The project's Layer Matrix: which collision layers hit each other, and which can target which.
// Edit it from Tools > Spacey > Layer Matrix. The AsteroidField bakes it into the subscene.
[CreateAssetMenu(menuName = "Spacey/Layer Matrix", fileName = "LayerMatrix")]
public class LayerMatrixSettings : ScriptableObject
{
    public const string DefaultPath = "Assets/Settings/LayerMatrix.asset";

    [HideInInspector] public int[] collides = new int[LayerMatrix.LayerCount];
    [HideInInspector] public int[] targets = new int[LayerMatrix.LayerCount];

    private void Reset()
    {
        FromMatrix(LayerMatrix.Default());
    }

    public void EnsureSize()
    {
        if (collides == null || collides.Length != LayerMatrix.LayerCount)
            System.Array.Resize(ref collides, LayerMatrix.LayerCount);
        if (targets == null || targets.Length != LayerMatrix.LayerCount)
            System.Array.Resize(ref targets, LayerMatrix.LayerCount);
    }

    public void FromMatrix(LayerMatrix m)
    {
        EnsureSize();
        for (int i = 0; i < LayerMatrix.LayerCount; i++)
        {
            collides[i] = m.collides[i];
            targets[i] = m.targets[i];
        }
    }

    public LayerMatrix ToMatrix()
    {
        EnsureSize();
        LayerMatrix m = LayerMatrix.Empty();
        for (int i = 0; i < LayerMatrix.LayerCount; i++)
        {
            m.collides[i] = (ushort)collides[i];
            m.targets[i] = (ushort)targets[i];
        }
        return m;
    }
}
