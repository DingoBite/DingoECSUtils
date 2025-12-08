using System.Collections.Generic;
using System.Linq;
using System.Text;
using AYellowpaper.SerializedCollections;
using DingoUnityExtensions.Utils;
using NaughtyAttributes;
using UnityEditor;
using UnityEngine;

namespace DingoECSUtils.DotsPrefabLibAccess
{
    [DisallowMultipleComponent]
    public sealed class DotsPrefabCatalogAuthoring : MonoBehaviour
    {
        [SerializeField] private string _catalogPath;
        [SerializeField] private string _keysPrefix;
        [SerializeField] private bool _addPrefabNamesBeforeTag;
        
        [SerializeField, SerializedDictionary("Tag", "Prefab")] private SerializedDictionary<string, GameObject> _entries = new();
        [SerializeField, ReadOnly] private SerializedDictionary<string, GameObject> _afterBakeState;
        [SerializeField, ReadOnly] private List<string> _afterBakeStateKeys;
        
        [SerializeField] private bool _includeInactive;
        
        public SerializedDictionary<string, GameObject> Entries => _entries;
        public string FullPrefix => $"{_catalogPath}/{_keysPrefix}";
        public bool AddPrefabBeforeTag => _addPrefabNamesBeforeTag;
        
#if VINSPECTOR_EXISTS
        [VInspector.Button]
#else
        [NaughtyAttributes.Button]
#endif
        private void PopulateUniqueChildren()
        {
#if UNITY_EDITOR
            var map = new Dictionary<string, GameObject>();
            var seen = new HashSet<string>();
            foreach (var t in GetComponentsInChildren<Transform>(_includeInactive))
            {
                if (t == transform)
                    continue;
                var asset = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                if (asset == null)
                    continue;

                var key = asset.name.NormalizePath();
                var norm = NormalizeKeyStrict(key);
                if (!seen.Add(norm))
                {
                    Debug.LogError($"[DotsPrefabCatalog] Similar key conflict: '{key}'");
                    continue;
                }

                map.TryAdd(key, asset);
            }

            _entries.Clear();
            foreach (var kv in map)
            {
                _entries.Add(kv.Key, kv.Value);
            }

            EditorUtility.SetDirty(this);
#endif
        }

#if VINSPECTOR_EXISTS
        [VInspector.Button]
#else
        [NaughtyAttributes.Button]
#endif
        private void PopulatePrefabStructure()
        {
#if UNITY_EDITOR
            var map = new Dictionary<string, GameObject>();
            var seen = new HashSet<string>();
            var idx = 0;
            foreach (var t in GetComponentsInChildren<Transform>(_includeInactive))
            {
                if (t == transform)
                    continue;
                var asset = PrefabUtility.GetCorrespondingObjectFromSource(t.gameObject);
                if (asset == null)
                    continue;

                var baseKey = asset.name.NormalizePath();
                string key;
                do
                {
                    key = $"{baseKey}_{idx++:0000}";
                } while (!seen.Add(NormalizeKeyStrict(key)));

                map[key] = asset;
            }

            _entries.Clear();
            foreach (var kv in map)
            {
                _entries.Add(kv.Key, kv.Value);
            }

            EditorUtility.SetDirty(this);
#endif
        }

        public string PreparePath(string key, string prefabName)
        {
            var fullKey = key.TrimStart('#');
            if (AddPrefabBeforeTag)
                fullKey = $"{prefabName}#{fullKey}";
            else 
                fullKey = $"#{fullKey}";
                
            fullKey = FullPrefix + fullKey;
            return fullKey;
        }
        
        private static string NormalizeKeyStrict(string s)
        {
            s = s.ToLowerInvariant();
            var sb = new StringBuilder(s.Length);
            foreach (var c in s.Where(char.IsLetterOrDigit))
            {
                sb.Append(c);
            }

            return sb.ToString();
        }

        private void OnValidate()
        {
            _afterBakeState.Clear();
            foreach (var (key, value) in _entries)
            {
                if (string.IsNullOrEmpty(key) || value == null || string.IsNullOrWhiteSpace(value.name))
                    return;
                _afterBakeState.AddConflictAllowed(PreparePath(key, value.name), value);
            }

            _afterBakeStateKeys.Clear();
            if (_afterBakeState.Count > 0)
            {
                _afterBakeStateKeys.AddRange(_afterBakeState.Keys);
                _afterBakeStateKeys.Sort();
            }
        }
    }
}