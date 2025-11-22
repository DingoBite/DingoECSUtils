using DingoUnityExtensions.Extensions;
using Unity.Entities;
using Unity.Transforms;
using UnityEngine;

namespace DingoECSUtils.Utils
{
    public static class CopyTransformSetup
    {
        public static void CopyEntityToGameObjectTransform(this EntityManager em, Entity entity, GameObject gameObject)
        {
            em.AddComponentObject(entity, gameObject.transform);
            em.AddComponent<CopyTransformToGO>(entity);
        }
    }
    
    public struct CopyTransformToGO : IComponentData { }

    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(TransformSystemGroup))]
    public partial struct CopyTransformToGOSystem : ISystem
    {
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<CopyTransformToGO>();
        }

        public void OnUpdate(ref SystemState state)
        {
            var em = state.EntityManager;

            foreach (var (ltw, e) in SystemAPI.Query<RefRO<LocalToWorld>>().WithAll<CopyTransformToGO>().WithEntityAccess())
            {
                if (em.HasComponent<Transform>(e))
                {
                    var bridge = em.GetComponentObject<Transform>(e);
                    if (bridge != null)
                        bridge.transform.SetTRS(SpaceType.World, ltw.ValueRO.Value);
                }
            }
        }
    }
}