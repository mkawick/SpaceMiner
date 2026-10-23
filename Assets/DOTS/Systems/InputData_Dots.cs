using Unity.Entities;
using Unity.Mathematics;

public struct InputData_Dots : IComponentData
{
    public float2 lookDir;
    public float2 moveDir;
}
