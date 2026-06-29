using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace DingoECSUtils.DotsPrefabLibAccess
{
    [Serializable, Preserve]
    public class DotsPrefabLibConfig : ScriptableObject
    {
        public List<string> PreloadAssets;
    }
}