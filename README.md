# DingoECSUtils

English version. Russian version: [README_ru.md](./README_ru.md)

Utilities and authoring helpers for Unity DOTS (Entities/ECS).

The repository is focused on four practical problems that appear in hybrid Unity projects:

- explicit DOTS world bootstrap and teardown;
- baking human-readable prefab and scene catalogs into ECS-friendly buffers;
- async loading and caching of `EntityPrefabReference` / `EntitySceneReference`;
- bridging ECS transform data back into classic `GameObject` views when a project is not 100% pure ECS yet.

This README is both an overview and a technical reference for the code that currently exists in the repository.

## Why this repo exists

Unity Entities provides powerful low-level primitives, but teams still have to solve a lot of glue code themselves:

- where the runtime `World` is created and who owns it;
- how content is addressed without hardcoding `EntityPrefabReference` values in gameplay code;
- how scene and prefab loading is deduplicated and cached;
- how DOTS runtime data is exposed to classic scene objects during a staged migration from `MonoBehaviour` to ECS.

`DingoECSUtils` packages those responsibilities into reusable authoring components, bakers, runtime services, and helper methods.

## What is inside

| Folder | Responsibility | Main types |
| --- | --- | --- |
| `DotsPrefabLibAccess/` | Authoring, baking, runtime indexing, prefab loading, scene loading | `DotsPrefabLib`, `DotsPrefabCatalogAuthoring`, `DotsPrefabCatalogBaker`, `BuiltinSceneCatalogAuthoring`, `BuiltinSceneCatalogBaker`, `ScenesCatalogManager` |
| `Utils/` | DOTS world lifecycle, ECS helper extensions, ECS-to-GameObject bridge | `DOTSWorlds`, `ECSUtils`, `CopyTransformToGOSystem` |

## Architecture at a glance

1. Authoring components keep dictionaries and inspector data in the Editor.
2. Bakers convert that data into ECS buffers and tags.
3. Runtime code scans the world for baked catalog entities.
4. `DotsPrefabLib` resolves string keys into `EntityPrefabReference` or `EntitySceneReference`.
5. Gameplay code loads prefabs and scenes asynchronously through a single access point.
6. Optional bridge systems can push ECS transform results back into managed `Transform` objects.

That keeps authoring data friendly for content creation while runtime access stays DOTS-native.

## Core runtime concepts

### `Utils/DOTSWorlds.cs`

`DOTSWorlds` is a `MonoBehaviour` wrapper around the active DOTS `World`.

Responsibilities:

- stores the runtime world reference in `Main`;
- optionally bootstraps the world on `Start()` when `_autoStart` is enabled;
- optionally loads an entry `SubScene` after initialization;
- tears the world down on `OnDestroy()`, `Shutdown()`, and editor play-mode exit;
- supports two modes:
  - use Unity's default world when automatic bootstrap is enabled;
  - create and own a custom world when `UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP_RUNTIME_WORLD` is defined.

Technical behavior:

- In manual bootstrap mode it disposes the default world, creates a new `World`, adds all default systems, and appends that world to the player loop.
- In automatic bootstrap mode it simply points `Main` to `World.DefaultGameObjectInjectionWorld`.
- `LoadEntryPointSubScene()` calls `SceneSystem.LoadSceneAsync(Main.Unmanaged, _entryPointSubScene.SceneGUID)`.

This class is the main answer to "who owns the ECS world lifecycle in the scene composition layer?"

### `Utils/ECSUtils.cs`

`ECSUtils` provides small but useful extension methods for repetitive `EntityManager` code:

- `PopulateDynamicBuffer<T>`: appends an `IEnumerable<T>` into an existing dynamic buffer.
- `AttachDynamicBuffer<T>`: adds or overwrites a dynamic buffer on an entity.
- `AttachComponentData<T>`: adds or overwrites `IComponentData`.
- `TryAddComponentData<T>`: adds `IComponentData` only when the entity does not have it yet.

These helpers reduce boilerplate in baking and runtime composition code and make intent clearer when attaching ECS data.

### `Utils/CopyTransformToGOSystem.cs`

This file implements a one-way ECS-to-GameObject transform bridge.

Public pieces:

- `CopyTransformSetup.CopyEntityToGameObjectTransform(EntityManager, Entity, GameObject)`
- `CopyTransformToGO : IComponentData`
- `CopyTransformToGOSystem : ISystem`

How it works:

1. The setup extension adds the target `Transform` as a managed component object on the entity.
2. It also adds the `CopyTransformToGO` tag.
3. `CopyTransformToGOSystem` runs in `SimulationSystemGroup` after `TransformSystemGroup`.
4. For each entity with `LocalToWorld` and `CopyTransformToGO`, the system copies world transform data into the managed `Transform`.

Important note: this bridge only syncs ECS -> `GameObject`. It does not write scene transform changes back into ECS.

## Prefab and scene catalog subsystem

The folder name `DotsPrefabLibAccess` is broader than prefabs alone. In practice it contains:

- prefab catalog authoring and baking;
- built-in scene catalog authoring and baking;
- runtime library for loading prefabs and scenes;
- small helper types for collecting scene references.

### Data contracts

| Type | Kind | Fields | Purpose |
| --- | --- | --- | --- |
| `DotsPrefabCatalogTag` | `IComponentData` | none | Marks entities that hold a prefab catalog buffer |
| `DotsPrefabCatalogEntry` | `IBufferElementData` | `FixedString512Bytes Key`, `EntityPrefabReference PrefabRef` | Baked prefab lookup entry |
| `BuiltinSceneEntry` | `IBufferElementData` | `FixedString128Bytes Name`, `EntitySceneReference SceneRef` | Baked built-in scene lookup entry |
| `PingTag` | `IComponentData` | none | Minimal marker/tag baked by `PingAuthoring` |
| `ExternalSceneDesc` | plain struct | `Name`, `Guid`, `SectionIndex` | Runtime descriptor for external scene manifests |

### `DotsPrefabCatalogAuthoring.cs`

`DotsPrefabCatalogAuthoring` is the main content authoring component for prefab lookup tables.

Serialized fields:

- `_catalogPath`: higher-level catalog namespace;
- `_keysPrefix`: local prefix inside the catalog;
- `_addPrefabNamesBeforeTag`: controls final key shape;
- `_entries`: `SerializedDictionary<string, GameObject>` of authored entries;
- `_includeInactive`: whether auto-population scans inactive children too;
- `_afterBakeState` and `_afterBakeStateKeys`: readonly preview/debug fields built during validation.

Key generation:

- `FullPrefix` is `"{_catalogPath}/{_keysPrefix}"`.
- `PreparePath(key, prefabName)`:
  - trims leading `#` from the authored key;
  - optionally prepends the prefab name before the tag;
  - prefixes the result with `FullPrefix`.

Resulting examples:

- `_catalogPath = "gameplay/units"`
- `_keysPrefix = "enemies"`
- authored key = `melee`
- prefab name = `Goblin`

If `_addPrefabNamesBeforeTag` is `true`, the final key becomes:

```text
gameplay/units/enemies/Goblin#melee
```

If `_addPrefabNamesBeforeTag` is `false`, the final key becomes:

```text
gameplay/units/enemies/#melee
```

Editor tooling:

- `PopulateUniqueChildren()` scans child transforms, resolves source prefab assets, and adds each unique prefab once.
- `PopulatePrefabStructure()` scans child transforms and generates indexed keys such as `goblin_0000`, `goblin_0001`, and so on.
- `OnValidate()` builds a preview of the post-bake map and sorted keys for inspector verification.

Conflict detection:

- The component uses `NormalizeKeyStrict()` for validation.
- This normalization lowercases the key and keeps only letters and digits.
- Because of that, keys like `Goblin-Melee`, `goblin_melee`, and `goblin melee` are considered equivalent during similarity checks.

This is an important design choice: it catches content conflicts early instead of letting similar-looking keys silently collide at runtime.

### `DotsPrefabCatalogBaker.cs`

`DotsPrefabCatalogBaker` converts authoring data into runtime ECS data:

- creates an entity with `TransformUsageFlags.None`;
- adds `DotsPrefabCatalogTag`;
- adds a `DynamicBuffer<DotsPrefabCatalogEntry>`;
- converts each authored prefab into `EntityPrefabReference`;
- stores the final string key as `FixedString512Bytes`.

At runtime, gameplay systems do not need access to inspector dictionaries or authoring objects. They only need the baked buffer.

### `BuiltinSceneCatalogAuthoring.cs`

`BuiltinSceneCatalogAuthoring` is a lightweight authoring component for scene name -> entity-scene-reference mappings.

Implementation notes:

- the dictionary type is `SerializedDictionary<string, EntitySceneReference>`;
- Unity's Entities property drawer still exposes each value as a `SceneAsset` picker in the Editor;
- the serialized layout is identical in the Editor, Standalone Player, and Dedicated Server;
- runtime code consumes the same build-safe reference type that is copied into the baked buffer.

### `BuiltinSceneCatalogBaker.cs`

`BuiltinSceneCatalogBaker` bakes `BuiltinSceneCatalogAuthoring` into a `DynamicBuffer<BuiltinSceneEntry>`.

Each entry stores:

- a string key in `FixedString128Bytes`;
- the authored `EntitySceneReference` copied without an editor-only conversion step.

This provides a stable human-readable scene addressing layer for ECS scene loading.

### `ScenesCatalogManager.cs`

`ScenesCatalogManager` is a static helper for building runtime dictionaries of scene references.

Available methods:

- `CollectBuildInScenes(EntityManager em)`
  - scans the world for all `BuiltinSceneEntry` buffers;
  - returns `Dictionary<string, EntitySceneReference>`.
- `CollectFromManifest(IEnumerable<ExternalSceneDesc> manifest)`
  - creates the same dictionary from an external manifest instead of baked entities.

This gives the project two scene-source options:

- baked scenes coming from authoring data inside the world;
- externally supplied scene manifests.

### `DotsPrefabLib.cs`

`DotsPrefabLib` is the central runtime service of the repository.

It inherits from `HardLinkAppModelBase`, so it is designed to live inside a model-driven application setup, not as a loose singleton.

#### Dependencies expected by `PostInitialize`

`PostInitialize(ExternalDependencies externalDependencies)` expects:

- `DOTSWorlds` to be registered in external dependencies;
- `DotsPrefabLibConfig` to be available in the config root.

During initialization it:

- grabs `externalDependencies.Get<DOTSWorlds>().Main`;
- stores the `EntityManager`;
- reads `DotsPrefabLibConfig`;
- reindexes all currently available prefab catalogs in the world;
- preloads configured assets through `PreloadAsync()`.

#### Prefab loading API

| Method | Purpose | Technical notes |
| --- | --- | --- |
| `LoadPrefabAsync(string key)` | Load by logical string key | Normalizes the key, deduplicates in-flight loads, checks `PrefabRoot` availability |
| `LoadPrefabAsync(EntityPrefabReference epr)` | Load directly by prefab reference | Uses GUID-based cache |
| `WarmupAssetAsync(string key)` | Alias for preloading or warming a prefab | Calls `LoadPrefabAsync(key)` |
| `Release(string key)` | Destroy one loaded prefab entity and remove it from cache | Manual lifetime management |
| `ReleaseAll()` | Destroy all cached prefabs | Clears both prefab cache and in-flight table |

Internal behavior worth knowing:

- `_keyToRef` is built by scanning every entity that has both `DotsPrefabCatalogTag` and `DotsPrefabCatalogEntry`.
- keys are normalized through `NormalizePath()` before lookup.
- the library uses `_inflightPrefabs` and `_inflightByGuid` to prevent duplicate async load work.
- once a prefab is fully loaded, the resulting root entity is cached and named with `_em.SetName(prefabEntity, fullKey)`.

Important limitation:

- prefab lifetime is manual;
- there is no per-caller reference counting for prefabs;
- if multiple systems use the same loaded prefab, release coordination is the responsibility of the caller or application layer.

#### Scene loading API

| Method | Purpose | Technical notes |
| --- | --- | --- |
| `LoadSceneAsync(EntitySceneReference sceneRef, string debugName = null, bool autoLoad = true, bool blockOnStreamIn = false, bool keepMetaEntitiesAlive = false)` | Load an ECS scene asynchronously | Returns the scene meta entity |
| `UnloadScene(EntitySceneReference sceneRef, bool destroyMetaEntities = true)` | Decrement ref-count and unload when it reaches zero | Supports deferred unload if loading is still in progress |
| `UnloadAllScenes(bool destroyMetaEntities = true)` | Unload every tracked scene | Clears scene bookkeeping tables |

Scene-management details:

- scenes are tracked by GUID in `_sceneByGuid`;
- every record stores meta entity, ref count, debug name, and a `KeepMetaAlive` flag;
- `_inflightSceneLoads` deduplicates concurrent scene load requests;
- `_pendingReleaseWhileLoading` handles the race where unload is requested before async load finishes;
- `PruneSceneBookkeeping()` removes stale zero-ref records whose scenes are no longer loaded.

Dynamic catalog behavior:

- after scene load or unload, `ReindexAllCatalogs()` is called again;
- this means newly loaded scenes can contribute additional `DotsPrefabCatalogTag` buffers;
- in practice, prefab catalogs can be extended dynamically by content scenes.

This is one of the most valuable properties of the library because content discovery can grow with scene streaming instead of being fixed only at app startup.

Current implementation note:

- `keepMetaEntitiesAlive` is stored in scene bookkeeping, but current unload behavior is controlled by the `destroyMetaEntities` argument passed to `UnloadScene` or `UnloadAllScenes`;
- in other words, `KeepMetaAlive` is currently bookkeeping state, not a fully enforced unload policy.

### `DotsPrefabLibConfig.cs` and `DotsPrefabLibScriptableConfig.cs`

Configuration is intentionally small:

- `DotsPrefabLibConfig` contains `List<string> PreloadAssets`;
- `DotsPrefabLibScriptableConfig` exposes that config as a `ScriptableObject`.

The preload flow is straightforward:

1. Add keys to `PreloadAssets`.
2. `DotsPrefabLib.PostInitialize()` calls `PreloadAsync()`.
3. Every non-empty key is normalized and warmed through `WarmupAssetAsync()`.

This makes startup warmup explicit and data-driven.

### `PingAuthoring.cs`

`PingAuthoring` is a tiny authoring component that bakes a `PingTag`.

It is useful as:

- a smoke-test baker;
- a simple marker entity;
- a minimal example for adding an authoring component and tag baker.

## Dependencies

### AppSDK repository dependencies

Branch values below are taken from the host project's `.gitmodules`. When no branch is specified there, the repository is marked as not pinned.

| Module | Why it matters here | Repository | Branch in host project |
| --- | --- | --- | --- |
| `DingoProjectAppStructure` | Provides `HardLinkAppModelBase`, config abstractions, and `ExternalDependencies` used by `DotsPrefabLib` | [github.com/DingoBite/DingoProjectAppStructure](https://github.com/DingoBite/DingoProjectAppStructure.git) | not pinned in `.gitmodules` |
| `DingoUnityExtensions` | Provides `NormalizePath()` helpers and `Transform.SetTRS(...)` extension methods used by catalog and bridge code | [github.com/DingoBite/DingoUnityExtensions](https://github.com/DingoBite/DingoUnityExtensions) | `dev` |
| `AppStructure` | Companion app-architecture module typically used together with `DingoProjectAppStructure` in the same AppSDK stack | [github.com/DingoBite/AppStructure](https://github.com/DingoBite/AppStructure) | `string-as-key-refactor` |

### Additional package dependencies

- Unity Entities or Scenes APIs with support for `Baker<T>`, `TransformUsageFlags`, `EntityPrefabReference`, and `EntitySceneReference`;
- `Cysharp.Threading.Tasks` (`UniTask`);
- `AYellowpaper.SerializedCollections`;
- `NaughtyAttributes`.

About inspector buttons:

- the code contains a conditional `VINSPECTOR_EXISTS` branch;
- however, `DotsPrefabCatalogAuthoring.cs` imports `NaughtyAttributes` directly;
- because of that, `NaughtyAttributes` is effectively a compile-time dependency in the repository's current state unless the file is adjusted.

## Installation

### Option 1. Git submodule

```bash
git submodule add https://github.com/DingoBite/DingoECSUtils.git Assets/AppSDK/DingoECSUtils
git submodule update --init --recursive
```

### Option 2. Copy into a Unity project

Copy the repository folders into your Unity project's `Assets/` tree and keep the `.meta` files intact.

## Integration guide

### Minimal bootstrap setup

1. Add `DOTSWorlds` to your bootstrap scene.
2. Assign an entry `SubScene` if the world should load one automatically.
3. Initialize it before the rest of the runtime model layer needs ECS access.
4. Register the `DOTSWorlds` instance in your external dependency container.
5. Register `DotsPrefabLibConfig` in your config root if you want preloads.
6. Register `DotsPrefabLib` in your app model layer if your architecture uses `HardLinkAppModelBase` models.

Example bootstrap pattern:

```csharp
using Cysharp.Threading.Tasks;
using DingoECSUtils.Utils;
using DingoProjectAppStructure.Core.Model;
using UnityEngine;

public class ExternalDependenciesRegisterer : ExternalDependenciesRegistererBase
{
    [SerializeField] private DOTSWorlds _dotsWorlds;

    protected override void AwakePreInitialize()
    {
        _dotsWorlds.Initialize();
        base.AwakePreInitialize();
    }

    protected override async UniTask AddictiveRegisterExternalDependenciesAsync(ExternalDependencies externalDependencies)
    {
        externalDependencies.Register(_dotsWorlds);
        await base.AddictiveRegisterExternalDependenciesAsync(externalDependencies);
    }

    public override void Dispose()
    {
        _dotsWorlds?.Shutdown();
        base.Dispose();
    }
}
```

### Authoring a prefab catalog

Typical flow:

1. Create a `GameObject` that will hold `DotsPrefabCatalogAuthoring`.
2. Fill `_catalogPath` and `_keysPrefix`.
3. Decide whether the prefab name should be included before the tag.
4. Either populate entries manually or use one of the auto-fill buttons.
5. Place the authoring object in a baked scene or subscene so the baker produces ECS data.

Practical advice:

- use `_addPrefabNamesBeforeTag = true` when keys should remain readable across large catalogs;
- use strict namespaces in `_catalogPath` and `_keysPrefix` to avoid collisions between gameplay domains;
- treat the final prepared key as public API consumed by runtime code and config.

### Loading a prefab at runtime

Example:

```csharp
using Cysharp.Threading.Tasks;
using DingoECSUtils.DotsPrefabLibAccess;
using Unity.Entities;

public static class SpawnExample
{
    public static async UniTask<Entity> SpawnAsync(DotsPrefabLib lib, EntityManager em, string key)
    {
        var prefab = await lib.LoadPrefabAsync(key);
        if (prefab == Entity.Null)
            return Entity.Null;

        return em.Instantiate(prefab);
    }
}
```

### Loading scenes by catalog name

Example:

```csharp
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using DingoECSUtils.DotsPrefabLibAccess;
using Unity.Entities;
using Unity.Entities.Serialization;

public static class SceneExample
{
    public static async UniTask<Entity> LoadNamedSceneAsync(EntityManager em, DotsPrefabLib lib)
    {
        Dictionary<string, EntitySceneReference> scenes = ScenesCatalogManager.CollectBuildInScenes(em);
        var sceneRef = scenes["arena"];
        return await lib.LoadSceneAsync(sceneRef, debugName: "arena");
    }
}
```

### Bridging ECS transform data to a `GameObject`

Example:

```csharp
using DingoECSUtils.Utils;
using Unity.Entities;
using UnityEngine;

public static class ViewBridgeExample
{
    public static void Bind(EntityManager em, Entity entity, GameObject view)
    {
        em.CopyEntityToGameObjectTransform(entity, view);
    }
}
```

This pattern is especially useful when:

- simulation lives in ECS;
- presentation still lives in classic Unity objects;
- the team is migrating to DOTS incrementally instead of rewriting the whole project at once.

## Advantages of the solution

### 1. Explicit world ownership

`DOTSWorlds` gives the scene composition layer a clear answer to world startup, entry subscene loading, and teardown. This is easier to reason about than scattered static initialization.

### 2. Content-addressable runtime loading

Gameplay code can depend on stable logical keys instead of embedding `EntityPrefabReference` handles everywhere. This keeps content lookup readable and easier to refactor.

### 3. DOTS-native runtime data

Authoring data is baked into ECS buffers. At runtime there is no need to traverse authoring dictionaries or editor-only assets to find prefabs and scenes.

### 4. Async load deduplication

Concurrent calls for the same prefab or scene reuse in-flight tasks instead of triggering duplicate load work. This reduces race conditions and unnecessary asset churn.

### 5. Dynamic catalog expansion through loaded scenes

Because the library reindexes all `DotsPrefabCatalogTag` buffers after scene changes, streaming a scene can expand the set of available prefab keys automatically.

### 6. Hybrid-project friendliness

The transform bridge and authoring-first workflows help teams move toward ECS without abandoning `GameObject`-based views and editor tooling on day one.

### 7. Small helper API with a large reduction in boilerplate

`ECSUtils` is intentionally small, but it removes repetitive add-or-overwrite patterns that otherwise spread across bakers and spawn pipelines.

### 8. Configuration-driven warmup

`PreloadAssets` turns startup warmup into data, which is easier to review and change than hidden bootstrap code.

## Constraints and implementation notes

- This repository is an `Assets/`-style Unity code drop, not a UPM package.
- Authoring components rely on Editor-only concepts such as `SceneAsset` and inspector dictionaries, while runtime consumes baked ECS buffers.
- Prefab release is manual and not reference-counted.
- Scene release is reference-counted by scene GUID.
- Final key stability matters: renaming catalog path rules changes the string API expected by runtime code and preload config.
- `CopyTransformToGOSystem` only mirrors transforms from ECS to managed objects.
- `KeepMetaAlive` is currently stored but not actively enforced as a separate unload policy.

## When this repo is a good fit

Use `DingoECSUtils` when your project needs most of the following:

- one explicit place that owns the DOTS world;
- ECS prefab loading by human-readable keys;
- ECS scene loading by logical names;
- streaming scenes that can introduce new runtime content catalogs;
- a hybrid bridge between ECS simulation and classic Unity presentation;
- compatibility with a model or config-driven application layer.

If your project is already a pure ECS codebase with a custom bootstrap and your team prefers direct use of `EntityPrefabReference` and `EntitySceneReference` everywhere, this repository may feel intentionally higher-level than necessary.
