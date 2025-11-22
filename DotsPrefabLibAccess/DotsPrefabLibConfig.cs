using System;
using System.Collections.Generic;
using DingoProjectAppStructure.Core.Config;
using UnityEngine.Scripting;

namespace DingoECSUtils.DotsPrefabLibAccess
{
    [Serializable, Preserve]
    public class DotsPrefabLibConfig : ConfigBase
    {
        public List<string> PreloadAssets;
    }
}