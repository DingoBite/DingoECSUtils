using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Scenes;
using UnityEngine.Scripting;

namespace DingoECSUtils.DotsPrefabLibAccess
{
    public struct BuiltinSceneEntry : IBufferElementData
    {
        public FixedString128Bytes Name;
        public EntitySceneReference SceneRef;
    }

#if UNITY_EDITOR
    [System.Serializable, Preserve]
    public struct SceneNameAsset
    {
        public string Name;
        public SubScene Scene;
    }
#endif

    public class BuiltinSceneCatalogBaker : Baker<BuiltinSceneCatalogAuthoring>
    {
        public override void Bake(BuiltinSceneCatalogAuthoring a)
        {
            var e = GetEntity(TransformUsageFlags.None);
            var buf = AddBuffer<BuiltinSceneEntry>(e);
            foreach (var (key, sceneAsset) in a.Scenes)
            {
                if (string.IsNullOrWhiteSpace(key) || sceneAsset == null)
                    continue;
                buf.Add(new BuiltinSceneEntry
                {
                    Name = new FixedString128Bytes(key),
                    SceneRef = new EntitySceneReference(sceneAsset)
                });
            }
        }
    }
}