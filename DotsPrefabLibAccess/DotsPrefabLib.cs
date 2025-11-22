using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Cysharp.Threading.Tasks;
using DingoECSUtils.Utils;
using DingoProjectAppStructure.Core.GeneralUtils;
using DingoProjectAppStructure.Core.Model;
using DingoUnityExtensions.Utils;
using Unity.Collections;
using Unity.Entities;
using Unity.Entities.Serialization;
using Unity.Mathematics;
using Unity.Scenes;
using UnityEngine;
using Hash128 = UnityEngine.Hash128;

namespace DingoECSUtils.DotsPrefabLibAccess
{
    public class DotsPrefabLib : HardLinkAppModelBase
    {
        private DotsPrefabLibConfig _cfg;

        private readonly Dictionary<string, EntityPrefabReference> _keyToRef = new();
        private readonly Dictionary<string, Entity> _prefabCache = new();
        private readonly Dictionary<string, Task<Entity>> _inflightPrefabs = new();
        private readonly Dictionary<Hash128, Entity> _prefabByGuid = new();
        private readonly Dictionary<Hash128, Task<Entity>> _inflightByGuid = new();

        private struct SceneRecord
        {
            public Entity Meta;
            public int RefCount;
            public bool KeepMetaAlive;
            public string DebugName;
        }

        private readonly Dictionary<Hash128, SceneRecord> _sceneByGuid = new();
        private readonly Dictionary<Hash128, Task<Entity>> _inflightSceneLoads = new();
        private readonly Dictionary<Hash128, int> _pendingReleaseWhileLoading = new();

        private World _world;
        private EntityManager _em;

        public override async Task PostInitialize(ExternalDependencies externalDependencies)
        {
            _world = externalDependencies.Get<DOTSWorlds>().Main;
            _em = _world.EntityManager;

            _cfg = externalDependencies.Configs().Get<DotsPrefabLibConfig>();

            ReindexAllCatalogs();
            await PreloadAsync();

            await base.PostInitialize(externalDependencies);
        }
        
        public async Task<Entity> LoadPrefabAsync(string key)
        {
            key = key.NormalizePath();

            if (_prefabCache.TryGetValue(key, out var ready) && ready != Entity.Null && _em.Exists(ready) && _em.HasComponent<PrefabRoot>(ready))
                return ready;

            if (_inflightPrefabs.TryGetValue(key, out var inflight))
                return await inflight;

            var task = LoadPrefabCoreAsync(key);
            _inflightPrefabs[key] = task;
            return await task;
        }

        public async Task<Entity> LoadPrefabAsync(EntityPrefabReference epr)
        {
            var guid = epr.AssetGUID;
            if (_prefabByGuid.TryGetValue(guid, out var ready) && ready != Entity.Null && _em.Exists(ready) && _em.HasComponent<PrefabRoot>(ready))
                return ready;

            if (_inflightByGuid.TryGetValue(guid, out var inflight))
                return await inflight;

            var t = LoadByGuidAsync(epr);
            _inflightByGuid[guid] = t;
            return await t;
        }

        public Task WarmupAssetAsync(string key) => LoadPrefabAsync(key);

        public void Release(string key)
        {
            key = key.NormalizePath();
            if (_prefabCache.TryGetValue(key, out var prefab) && prefab != Entity.Null && _em.Exists(prefab))
                _em.DestroyEntity(prefab);

            _prefabCache.Remove(key);
            _inflightPrefabs.Remove(key);
        }

        public void ReleaseAll()
        {
            foreach (var kv in _prefabCache)
            {
                if (kv.Value != Entity.Null && _em.Exists(kv.Value))
                    _em.DestroyEntity(kv.Value);
            }

            _prefabCache.Clear();
            _inflightPrefabs.Clear();
        }
        
        public async Task<Entity> LoadSceneAsync(EntitySceneReference sceneRef, string debugName = null, bool autoLoad = true, bool blockOnStreamIn = false, bool keepMetaEntitiesAlive = false)
        {
            var sceneGuid = new SceneReference(sceneRef).SceneGUID;
            PruneSceneBookkeeping();

            if (_sceneByGuid.TryGetValue(sceneGuid, out var rec) && rec.Meta != Entity.Null && _em.Exists(rec.Meta))
            {
                rec.RefCount++;
                _sceneByGuid[sceneGuid] = rec;
                return rec.Meta;
            }

            if (_inflightSceneLoads.TryGetValue(sceneGuid, out var inflight))
            {
                var meta0 = await inflight;
                if (_sceneByGuid.TryGetValue(sceneGuid, out var r2))
                {
                    r2.RefCount++;
                    _sceneByGuid[sceneGuid] = r2;
                }

                return meta0;
            }

            var t = LoadSceneCoreAsync(sceneGuid, sceneRef, debugName, autoLoad, blockOnStreamIn, keepMetaEntitiesAlive);
            _inflightSceneLoads[sceneGuid] = t;
            return await t;
        }

        public void UnloadScene(EntitySceneReference sceneRef, bool destroyMetaEntities = true)
        {
            var sceneGuid = new SceneReference(sceneRef).SceneGUID;

            if (_inflightSceneLoads.ContainsKey(sceneGuid))
            {
                _pendingReleaseWhileLoading.TryGetValue(sceneGuid, out var cnt);
                _pendingReleaseWhileLoading[sceneGuid] = cnt + 1;
                return;
            }

            if (!_sceneByGuid.TryGetValue(sceneGuid, out var rec))
                return;

            rec.RefCount = math.max(0, rec.RefCount - 1);
            _sceneByGuid[sceneGuid] = rec;

            if (rec.RefCount > 0)
                return;

            var unloadParams = destroyMetaEntities ? SceneSystem.UnloadParameters.DestroyMetaEntities : SceneSystem.UnloadParameters.Default;
            if (_em.Exists(rec.Meta))
                SceneSystem.UnloadScene(_world.Unmanaged, rec.Meta, unloadParams);

            _sceneByGuid.Remove(sceneGuid);
            ReindexAllCatalogs();
        }

        public void UnloadAllScenes(bool destroyMetaEntities = true)
        {
            var unloadParams = destroyMetaEntities ? SceneSystem.UnloadParameters.DestroyMetaEntities : SceneSystem.UnloadParameters.Default;

            foreach (var kv in _sceneByGuid.ToArray())
            {
                var rec = kv.Value;
                if (_em.Exists(rec.Meta))
                    SceneSystem.UnloadScene(_world.Unmanaged, rec.Meta, unloadParams);
                _sceneByGuid.Remove(kv.Key);
            }

            _inflightSceneLoads.Clear();
            _pendingReleaseWhileLoading.Clear();

            ReindexAllCatalogs();
        }

        private async Task<Entity> LoadSceneCoreAsync(Hash128 sceneGuid, EntitySceneReference sceneRef, string debugName, bool autoLoad, bool blockOnStreamIn, bool keepMetaEntitiesAlive)
        {
            try
            {
                SceneLoadFlags flags = 0;
                if (!autoLoad)
                    flags |= SceneLoadFlags.DisableAutoLoad;
                if (blockOnStreamIn)
                    flags |= SceneLoadFlags.BlockOnStreamIn;
                var p = new SceneSystem.LoadParameters { AutoLoad = autoLoad, Flags = flags };

                var meta = SceneSystem.LoadSceneAsync(_world.Unmanaged, sceneRef, p);

                while (!_em.Exists(meta))
                {
                    await UniTask.Yield();
                }

                if (!blockOnStreamIn)
                {
                    SceneSystem.SceneStreamingState state;
                    if (autoLoad)
                    {
                        do
                        {
                            await UniTask.Yield();
                            state = SceneSystem.GetSceneStreamingState(_world.Unmanaged, meta);
                        } while (state is SceneSystem.SceneStreamingState.Loading or SceneSystem.SceneStreamingState.Unloaded or SceneSystem.SceneStreamingState.LoadedSectionEntities);
                    }
                    else
                    {
                        do
                        {
                            await UniTask.Yield();
                            state = SceneSystem.GetSceneStreamingState(_world.Unmanaged, meta);
                        } while (state is SceneSystem.SceneStreamingState.Loading or SceneSystem.SceneStreamingState.Unloaded);
                    }
                }

                _pendingReleaseWhileLoading.TryGetValue(sceneGuid, out var pendingReleases);
                if (pendingReleases > 0)
                    _pendingReleaseWhileLoading.Remove(sceneGuid);

                var rec = new SceneRecord
                {
                    Meta = meta,
                    RefCount = math.max(1 - pendingReleases, 0),
                    KeepMetaAlive = keepMetaEntitiesAlive,
                    DebugName = debugName ?? sceneGuid.ToString()
                };

                _sceneByGuid[sceneGuid] = rec;

                ReindexAllCatalogs();

                return meta;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[DotsPrefabLib] LoadSceneAsync failed: {debugName ?? sceneGuid.ToString()} — {ex.Message}");
                return Entity.Null;
            }
            finally
            {
                _inflightSceneLoads.Remove(sceneGuid);
            }
        }

        private void PruneSceneBookkeeping()
        {
            var stale = new List<Hash128>();
            foreach (var kv in _sceneByGuid)
            {
                var rec = kv.Value;
                var alive = rec.Meta != Entity.Null && _em.Exists(rec.Meta) && SceneSystem.IsSceneLoaded(_world.Unmanaged, rec.Meta);
                if (!alive && rec.RefCount == 0)
                    stale.Add(kv.Key);
            }

            foreach (var guid in stale)
            {
                _sceneByGuid.Remove(guid);
            }
        }

        private void ReindexAllCatalogs()
        {
            _keyToRef.Clear();

            using var q = _em.CreateEntityQuery(new EntityQueryDesc
            {
                All = new[]
                {
                    ComponentType.ReadOnly<DotsPrefabCatalogTag>(),
                    ComponentType.ReadOnly<DotsPrefabCatalogEntry>()
                }
            });

            using var catalogs = q.ToEntityArray(Allocator.Temp);
            foreach (var catalog in catalogs)
            {
                var buf = _em.GetBuffer<DotsPrefabCatalogEntry>(catalog);
                foreach (var entry in buf)
                {
                    var key = entry.Key.ToString().NormalizePath();
                    _keyToRef[key] = entry.PrefabRef;
                }
            }
        }

        private async Task<Entity> LoadByGuidAsync(EntityPrefabReference epr)
        {
            try
            {
                var prefab = SceneSystem.LoadPrefabAsync(_world.Unmanaged, epr);
                while (!_em.Exists(prefab) || !_em.HasComponent<PrefabRoot>(prefab))
                {
                    await UniTask.Yield();
                }

                var prefabEntity = _em.GetComponentData<PrefabRoot>(prefab).Root;
                _prefabByGuid[epr.AssetGUID] = prefabEntity;
                return prefabEntity;
            }
            finally
            {
                _inflightByGuid.Remove(epr.AssetGUID);
            }
        }

        private async Task<Entity> LoadPrefabCoreAsync(string fullKey)
        {
            try
            {
                if (!_keyToRef.TryGetValue(fullKey, out var prefabRef))
                {
                    ReindexAllCatalogs();

                    if (!_keyToRef.TryGetValue(fullKey, out prefabRef))
                        throw new Exception($"Key '{fullKey}' not found in prefab catalogs.");
                }

                var prefabEntity = await LoadByGuidAsync(prefabRef);
                _em.SetName(prefabEntity, fullKey);
                _prefabCache[fullKey] = prefabEntity;
                return prefabEntity;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                Debug.LogError($"[DotsPrefabLib] LoadPrefabAsync('{fullKey}') failed: {ex.Message}");
                return Entity.Null;
            }
            finally
            {
                _inflightPrefabs.Remove(fullKey);
            }
        }

        private async UniTask PreloadAsync()
        {
            if (_cfg?.PreloadAssets == null)
                return;

            foreach (var key in _cfg.PreloadAssets.Where(key => !string.IsNullOrWhiteSpace(key)))
            {
                await WarmupAssetAsync(key.NormalizePath());
            }
        }
    }
}