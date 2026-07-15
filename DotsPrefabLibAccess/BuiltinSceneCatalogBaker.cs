using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;

namespace DingoECSUtils.DotsPrefabLibAccess
{
    public struct BuiltinSceneEntry : IBufferElementData
    {
        public FixedString128Bytes Name;
        public EntitySceneReference SceneRef;
    }

#if UNITY_EDITOR
    public class BuiltinSceneCatalogBaker : Baker<BuiltinSceneCatalogAuthoring>
    {
        public override void Bake(BuiltinSceneCatalogAuthoring a)
        {
            var e = GetEntity(TransformUsageFlags.None);
            var buf = AddBuffer<BuiltinSceneEntry>(e);
            foreach (var (key, sceneReference) in a.Scenes)
            {
                if (string.IsNullOrWhiteSpace(key) || !sceneReference.IsReferenceValid)
                {
                    continue;
                }

                buf.Add(new BuiltinSceneEntry
                {
                    Name = new FixedString128Bytes(key),
                    SceneRef = sceneReference
                });
            }
        }
    }
#endif
}
