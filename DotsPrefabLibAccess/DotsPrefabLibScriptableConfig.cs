using DingoProjectAppStructure.Core.Config;
using UnityEngine;

namespace DingoECSUtils.DotsPrefabLibAccess
{
    [CreateAssetMenu(fileName = nameof(DotsPrefabLibConfig), menuName = CREATE_MENU_PREFIX + nameof(DotsPrefabLibConfig))]
    public class DotsPrefabLibScriptableConfig : ScriptableConfig<DotsPrefabLibConfig> {}
}