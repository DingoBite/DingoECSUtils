using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;

namespace DingoECSUtils.DotsPrefabLibAccess
{
    public struct DotsPrefabCatalogTag : IComponentData {}

    public struct DotsPrefabCatalogEntry : IBufferElementData
    {
        public FixedString512Bytes Key;
        public EntityPrefabReference PrefabRef;
    }
    
    public sealed class DotsPrefabCatalogBaker : Baker<DotsPrefabCatalogAuthoring>
    {
        public override void Bake(DotsPrefabCatalogAuthoring authoring)
        {
            var e = GetEntity(TransformUsageFlags.None);
            AddComponent<DotsPrefabCatalogTag>(e);

            var buf = AddBuffer<DotsPrefabCatalogEntry>(e);
            foreach (var (key, value) in authoring.Entries)
            {
                var fullKey = authoring.PreparePath(key, value.name);
                if (string.IsNullOrEmpty(fullKey) || value == null)
                    continue;

                var prefabRef = new EntityPrefabReference(value);

                buf.Add(new DotsPrefabCatalogEntry
                {
                    Key = new FixedString512Bytes(fullKey),
                    PrefabRef = prefabRef
                });
            }
        }
    }
}