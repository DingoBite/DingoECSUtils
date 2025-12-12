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
