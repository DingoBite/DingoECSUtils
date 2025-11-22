using AYellowpaper.SerializedCollections;
using UnityEditor;
using UnityEngine;

namespace DingoECSUtils.DotsPrefabLibAccess
{
    public sealed class BuiltinSceneCatalogAuthoring : MonoBehaviour
    {
        [SerializeField] private SerializedDictionary<string, SceneAsset> _scenes;

        public SerializedDictionary<string, SceneAsset> Scenes => _scenes;
    }
}