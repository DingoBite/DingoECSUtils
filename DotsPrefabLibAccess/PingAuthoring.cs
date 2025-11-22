using Unity.Entities;
using UnityEngine;

namespace DingoECSUtils.DotsPrefabLibAccess
{
    public struct PingTag : IComponentData {}
    
    public sealed class PingAuthoring : MonoBehaviour
    {
        class Baker : Baker<PingAuthoring>
        {
            public override void Bake(PingAuthoring a)
            {
                var e = GetEntity(TransformUsageFlags.None);
                AddComponent<PingTag>(e);
            }
        }
    }
}