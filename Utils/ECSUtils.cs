using System.Collections.Generic;
using Unity.Entities;

namespace DingoECSUtils.Utils
{
    public static class ECSUtils
    {
        public static void PopulateDynamicBuffer<T>(this ref DynamicBuffer<T> dynamicBuffer, IEnumerable<T> enumerable) where T : unmanaged, IBufferElementData
        {
            foreach (var element in enumerable)
            {
                dynamicBuffer.Add(element);
            }
        }

        public static void AttachDynamicBuffer<T>(this EntityManager em, Entity target, IEnumerable<T> enumerable, bool overwrite = false) where T : unmanaged, IBufferElementData
        {
            DynamicBuffer<T> buf;
            if (em.HasBuffer<T>(target))
            {
                if (!overwrite)
                    return;
                buf = em.GetBuffer<T>(target);
                buf.Clear();
            }
            else
            {
                buf = em.AddBuffer<T>(target);
            }

            buf.PopulateDynamicBuffer(enumerable);
        }

        public static void AttachComponentData<T>(this EntityManager em, Entity target, T componentData, bool overwrite = false) where T : unmanaged, IComponentData
        {
            if (!em.TryAddComponentData(target, componentData) && !overwrite)
                return;
            em.SetComponentData(target, componentData);
        }
        
        public static bool TryAddComponentData<T>(this EntityManager em, Entity target, T componentData) where T : unmanaged, IComponentData
        {
            if (!em.HasComponent<T>(target))
            {
                em.AddComponentData(target, componentData);
                return true;
            }
            return false;
        }
    }
}