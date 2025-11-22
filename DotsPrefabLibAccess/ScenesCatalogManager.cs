using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using UnityEngine;
using Hash128 = Unity.Entities.Hash128;

namespace DingoECSUtils.DotsPrefabLibAccess
{
    public struct ExternalSceneDesc
    {
        public string Name;
        public Hash128 Guid;
        public int SectionIndex;
    }
    
    public class ScenesCatalogManager
    {
        public static Dictionary<string, EntitySceneReference> CollectBuildInScenes(EntityManager em)
        {
            var map = new Dictionary<string, EntitySceneReference>();
            using var q = em.CreateEntityQuery(ComponentType.ReadOnly<BuiltinSceneEntry>());
            using var ents = q.ToEntityArray(Allocator.Temp);
            foreach (var e in ents)
            {
                var buf = em.GetBuffer<BuiltinSceneEntry>(e);
                foreach (var it in buf)
                {
                    Debug.Log($"Found buildIn scene: {it.Name}");
                    map[it.Name.ToString()] = it.SceneRef;
                }
            }
            return map;
        }
        
        public static Dictionary<string, EntitySceneReference> CollectFromManifest(IEnumerable<ExternalSceneDesc> manifest)
        {
            var map = new Dictionary<string, EntitySceneReference>();
            foreach (var m in manifest)
            {
                var esr = new EntitySceneReference(m.Guid, m.SectionIndex);
                map[m.Name] = esr;
            }
            return map;
        }
    }
}