using AYellowpaper.SerializedCollections;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

namespace DingoECSUtils.DotsPrefabLibAccess
{
    public sealed class BuiltinSceneCatalogAuthoring : MonoBehaviour
    {
#if UNITY_EDITOR
        [SerializeField] private SerializedDictionary<string, SceneAsset> _scenes = new();

        public SerializedDictionary<string, SceneAsset> Scenes => _scenes;
#endif
    }
}