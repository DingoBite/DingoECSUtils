# DingoECSUtils

Utilities and authoring helpers for Unity DOTS (Entities/ECS).

This repository contains two main parts:
- `DotsPrefabLibAccess/` — authoring and bakers for building “catalogs” of prefabs and scenes into ECS buffers.
- `Utils/` — general ECS helpers/systems (for example `CopyTransformToGOSystem`, `DOTSWorlds`, `ECSUtils`).

## Features

### Built-in Scene Catalog
- `BuiltinSceneCatalogAuthoring` stores a `string -> SceneAsset` dictionary in the Editor.
- `BuiltinSceneCatalogBaker` bakes it into a `DynamicBuffer<BuiltinSceneEntry>` where each entry contains a name and an `EntitySceneReference`.

### DOTS Prefab Catalog
- `DotsPrefabCatalogAuthoring` stores “key -> prefab” entries and provides Editor buttons for auto-fill and validation.
- `DotsPrefabCatalogBaker` bakes it into a `DynamicBuffer<DotsPrefabCatalogEntry>` where:
  - `Key` is `FixedString512Bytes`
  - `PrefabRef` is `EntityPrefabReference`
  - a `DotsPrefabCatalogTag` is added to the entity
- Includes strict key normalization and conflict detection for “similar” keys.

## Dependencies

Required:
- Unity DOTS Entities (a version that supports `Baker<T>` and `TransformUsageFlags`).

Used by the authoring code:
- `AYellowpaper.SerializedCollections` (SerializedDictionary).

Optional (Inspector buttons):
- NaughtyAttributes or VInspector.
  - The code switches via `#if VINSPECTOR_EXISTS` (uses `[VInspector.Button]`), otherwise `[NaughtyAttributes.Button]`.

## Installation

Option A (simple):
1. Copy `DotsPrefabLibAccess/` and `Utils/` into your Unity project under `Assets/` (including `.meta` files).

Option B (git submodule):
1. Add this repository as a submodule inside `Assets/DingoECSUtils/`.

## Quick usage

### 1) Prefabs: build a lookup from `DotsPrefabCatalogEntry`

```csharp
using DingoECSUtils.DotsPrefabLibAccess;
using Unity.Collections;
using Unity.Entities;

public partial struct BuildPrefabCatalogLookupSystem : ISystem
{
    public void OnCreate(ref SystemState state)
    {
        state.RequireForUpdate<DotsPrefabCatalogTag>();
    }

    public void OnUpdate(ref SystemState state)
    {
        foreach (var buffer in SystemAPI.Query<DynamicBuffer<DotsPrefabCatalogEntry>>()
                     .WithAll<DotsPrefabCatalogTag>())
        {
            var map = new NativeHashMap<FixedString512Bytes, EntityPrefabReference>(
                buffer.Length, Allocator.Temp);

            foreach (var e in buffer)
                map.TryAdd(e.Key, e.PrefabRef);

            // TODO: use map (spawn by key, etc.)

            map.Dispose();
        }

        state.Enabled = false;
    }
}
```

### 2) Scenes: find an `EntitySceneReference` by name

```csharp
using DingoECSUtils.DotsPrefabLibAccess;
using Unity.Entities;
using Unity.Collections;

public partial struct FindSceneRefSystem : ISystem
{
    public void OnUpdate(ref SystemState state)
    {
        var target = (FixedString128Bytes)"Main";

        foreach (var buffer in SystemAPI.Query<DynamicBuffer<BuiltinSceneEntry>>())
        {
            foreach (var e in buffer)
            {
                if (e.Name.Equals(target))
                {
                    var sceneRef = e.SceneRef;
                    // TODO: pass sceneRef to your scene-loading code
                }
            }
        }
    }
}
```
