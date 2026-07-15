using AYellowpaper.SerializedCollections;
using Unity.Entities.Serialization;
using UnityEngine;

namespace DingoECSUtils.DotsPrefabLibAccess
{
    public sealed class BuiltinSceneCatalogAuthoring : MonoBehaviour
    {
        [SerializeField] private SerializedDictionary<string, EntitySceneReference> _scenes = new();

        public SerializedDictionary<string, EntitySceneReference> Scenes => _scenes;
    }
}
