# DingoECSUtils

Русская версия. English version: [README.md](./README.md)

Набор утилит и authoring-инструментов для Unity DOTS (`Entities` / ECS).

Репозиторий решает четыре практические задачи, которые почти всегда возникают в гибридных Unity-проектах:

- явная инициализация и корректное завершение DOTS `World`;
- запекание человекочитаемых каталогов префабов и сцен в ECS-буферы;
- асинхронная загрузка и кэширование `EntityPrefabReference` / `EntitySceneReference`;
- мост между ECS-данными и классическими `GameObject`-представлениями, когда проект ещё не полностью переведён на DOTS.

Этот README задуман одновременно как обзор и как техническая документация по текущему коду репозитория.

## Зачем нужен этот репозиторий

Unity Entities даёт мощные низкоуровневые примитивы, но команде всё равно приходится самостоятельно решать много инфраструктурных задач:

- где создаётся runtime `World` и кто отвечает за его жизненный цикл;
- как адресовать контент, не размазывая `EntityPrefabReference` по gameplay-коду;
- как дедуплицировать и кэшировать загрузку сцен и префабов;
- как передавать результат ECS-симуляции в обычные Unity-объекты на переходном этапе миграции с `MonoBehaviour` на ECS.

`DingoECSUtils` собирает эти обязанности в переиспользуемые authoring-компоненты, bakers, runtime-сервисы и небольшие helper-методы.

## Что находится внутри

| Папка | Зона ответственности | Основные типы |
| --- | --- | --- |
| `DotsPrefabLibAccess/` | Authoring, baking, runtime-индексация, загрузка префабов и сцен | `DotsPrefabLib`, `DotsPrefabCatalogAuthoring`, `DotsPrefabCatalogBaker`, `BuiltinSceneCatalogAuthoring`, `BuiltinSceneCatalogBaker`, `ScenesCatalogManager` |
| `Utils/` | Жизненный цикл DOTS `World`, ECS helper-расширения, мост ECS -> `GameObject` | `DOTSWorlds`, `ECSUtils`, `CopyTransformToGOSystem` |

## Архитектура на верхнем уровне

1. Authoring-компоненты хранят словари и инспекторные данные в Editor.
2. Bakers переводят эти данные в ECS-буферы и теги.
3. Runtime-код сканирует мир и находит запечённые каталог-сущности.
4. `DotsPrefabLib` разрешает строковые ключи в `EntityPrefabReference` или `EntitySceneReference`.
5. Gameplay-код загружает префабы и сцены асинхронно через единый слой доступа.
6. При необходимости bridge-системы прокидывают результат ECS-трансформов обратно в managed `Transform`.

В результате authoring-слой остаётся удобным для контентной работы, а runtime-доступ остаётся DOTS-native.

## Ключевые runtime-концепции

### `Utils/DOTSWorlds.cs`

`DOTSWorlds` — это `MonoBehaviour`-обёртка над активным DOTS `World`.

Обязанности:

- хранит ссылку на runtime-мир в `Main`;
- может автоматически запускать инициализацию в `Start()`, если включён `_autoStart`;
- может загружать входную `SubScene` сразу после инициализации;
- корректно завершает мир в `OnDestroy()`, `Shutdown()` и при выходе из play mode в редакторе;
- поддерживает два режима:
  - использование стандартного мира Unity, если автоматический bootstrap не отключён;
  - создание и владение собственным миром, если определён `UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP_RUNTIME_WORLD`.

Техническое поведение:

- В ручном bootstrap-режиме класс удаляет стандартный мир, создаёт новый `World`, подключает все системные группы по умолчанию и добавляет мир в player loop.
- В автоматическом bootstrap-режиме `Main` просто указывает на `World.DefaultGameObjectInjectionWorld`.
- `LoadEntryPointSubScene()` вызывает `SceneSystem.LoadSceneAsync(Main.Unmanaged, _entryPointSubScene.SceneGUID)`.

Это основной ответ на вопрос: кто именно владеет жизненным циклом ECS-мира на уровне scene composition.

### `Utils/ECSUtils.cs`

`ECSUtils` содержит небольшие, но полезные расширения для типового `EntityManager`-кода:

- `PopulateDynamicBuffer<T>`: дописывает `IEnumerable<T>` в существующий dynamic buffer;
- `AttachDynamicBuffer<T>`: добавляет или перезаписывает dynamic buffer на сущности;
- `AttachComponentData<T>`: добавляет или перезаписывает `IComponentData`;
- `TryAddComponentData<T>`: добавляет `IComponentData` только если его ещё нет на сущности.

Эти helper-методы уменьшают шаблонный код в baker-ах и runtime-композиции и делают намерение явнее.

### `Utils/CopyTransformToGOSystem.cs`

Этот файл реализует однонаправленный мост ECS -> `GameObject` для трансформа.

Публичные элементы:

- `CopyTransformSetup.CopyEntityToGameObjectTransform(EntityManager, Entity, GameObject)`
- `CopyTransformToGO : IComponentData`
- `CopyTransformToGOSystem : ISystem`

Как это работает:

1. Setup-расширение добавляет целевой `Transform` как managed component object на сущность.
2. Затем на сущность добавляется тег `CopyTransformToGO`.
3. `CopyTransformToGOSystem` работает в `SimulationSystemGroup` после `TransformSystemGroup`.
4. Для каждой сущности с `LocalToWorld` и `CopyTransformToGO` система копирует world transform в managed `Transform`.

Важно: мост синхронизирует только ECS -> `GameObject`. Обратная запись трансформа сцены в ECS не выполняется.

## Подсистема каталогов префабов и сцен

Название папки `DotsPrefabLibAccess` шире, чем просто "префабы". Фактически внутри находятся:

- authoring и baking каталога префабов;
- authoring и baking каталога встроенных сцен;
- runtime-библиотека для загрузки префабов и сцен;
- вспомогательные типы для сбора ссылок на сцены.

### Контракты данных

| Тип | Вид | Поля | Назначение |
| --- | --- | --- | --- |
| `DotsPrefabCatalogTag` | `IComponentData` | нет | Помечает сущности, которые содержат буфер каталога префабов |
| `DotsPrefabCatalogEntry` | `IBufferElementData` | `FixedString512Bytes Key`, `EntityPrefabReference PrefabRef` | Запечённая запись для поиска префаба |
| `BuiltinSceneEntry` | `IBufferElementData` | `FixedString128Bytes Name`, `EntitySceneReference SceneRef` | Запечённая запись для поиска built-in сцены |
| `PingTag` | `IComponentData` | нет | Минимальный marker/tag, который bake-ится через `PingAuthoring` |
| `ExternalSceneDesc` | обычная структура | `Name`, `Guid`, `SectionIndex` | Runtime-описание сцены из внешнего манифеста |

### `DotsPrefabCatalogAuthoring.cs`

`DotsPrefabCatalogAuthoring` — основной authoring-компонент для таблиц поиска префабов.

Сериализуемые поля:

- `_catalogPath`: namespace каталога верхнего уровня;
- `_keysPrefix`: локальный префикс внутри каталога;
- `_addPrefabNamesBeforeTag`: управляет финальной формой ключа;
- `_entries`: `SerializedDictionary<string, GameObject>` с authored-записями;
- `_includeInactive`: учитывать ли неактивных детей при автосборке;
- `_afterBakeState` и `_afterBakeStateKeys`: readonly-поля предпросмотра и отладки, заполняемые при валидации.

Генерация ключа:

- `FullPrefix` имеет вид `"{_catalogPath}/{_keysPrefix}"`;
- `PreparePath(key, prefabName)`:
  - убирает ведущий `#` у authored-ключа;
  - при необходимости добавляет имя префаба перед тегом;
  - затем префиксует результат через `FullPrefix`.

Примеры результата:

- `_catalogPath = "gameplay/units"`
- `_keysPrefix = "enemies"`
- authored key = `melee`
- prefab name = `Goblin`

Если `_addPrefabNamesBeforeTag == true`, финальный ключ будет:

```text
gameplay/units/enemies/Goblin#melee
```

Если `_addPrefabNamesBeforeTag == false`, финальный ключ будет:

```text
gameplay/units/enemies/#melee
```

Editor-tooling:

- `PopulateUniqueChildren()` сканирует дочерние трансформы, находит source prefab assets и добавляет каждый уникальный префаб один раз.
- `PopulatePrefabStructure()` сканирует дочерние трансформы и генерирует индексированные ключи вроде `goblin_0000`, `goblin_0001` и т.д.
- `OnValidate()` строит preview финальной post-bake карты и отсортированного списка ключей.

Проверка конфликтов:

- компонент использует `NormalizeKeyStrict()` для валидации;
- нормализация переводит строку в lowercase и оставляет только буквы и цифры;
- поэтому ключи вроде `Goblin-Melee`, `goblin_melee` и `goblin melee` считаются эквивалентными при similarity-check.

Это важное архитектурное решение: похожие ключи ловятся заранее, а не начинают тихо конфликтовать уже в runtime.

### `DotsPrefabCatalogBaker.cs`

`DotsPrefabCatalogBaker` переводит authoring-данные в runtime ECS-данные:

- создаёт сущность с `TransformUsageFlags.None`;
- добавляет `DotsPrefabCatalogTag`;
- добавляет `DynamicBuffer<DotsPrefabCatalogEntry>`;
- конвертирует каждый authored prefab в `EntityPrefabReference`;
- сохраняет итоговый строковый ключ как `FixedString512Bytes`.

В runtime gameplay-системам уже не нужно ходить в инспекторные словари и authoring-объекты. Им нужен только запечённый буфер.

### `BuiltinSceneCatalogAuthoring.cs`

`BuiltinSceneCatalogAuthoring` — лёгкий authoring-компонент для отображения "имя сцены -> scene asset".

Особенности реализации:

- сериализуемый словарь существует только под `#if UNITY_EDITOR`;
- тип словаря — `SerializedDictionary<string, SceneAsset>`;
- runtime-код больше не работает напрямую с `SceneAsset`, а использует только запечённые ссылки.

### `BuiltinSceneCatalogBaker.cs`

`BuiltinSceneCatalogBaker` запекает `BuiltinSceneCatalogAuthoring` в `DynamicBuffer<BuiltinSceneEntry>`.

Каждая запись содержит:

- строковый ключ в `FixedString128Bytes`;
- `EntitySceneReference`, созданный из scene asset.

Это даёт стабильный человекочитаемый слой адресации сцен для ECS scene loading.

### `ScenesCatalogManager.cs`

`ScenesCatalogManager` — статический helper для построения runtime-словарей со ссылками на сцены.

Доступные методы:

- `CollectBuildInScenes(EntityManager em)`
  - сканирует мир на все буферы `BuiltinSceneEntry`;
  - возвращает `Dictionary<string, EntitySceneReference>`.
- `CollectFromManifest(IEnumerable<ExternalSceneDesc> manifest)`
  - строит такой же словарь, но из внешнего манифеста вместо baked-данных.

Это даёт проекту два источника сцен:

- baked-сцены из authoring-данных внутри мира;
- внешние манифесты сцен.

### `DotsPrefabLib.cs`

`DotsPrefabLib` — центральный runtime-сервис репозитория.

Он наследуется от `HardLinkAppModelBase`, то есть рассчитан на работу внутри model-driven application setup, а не как свободный singleton.

#### Зависимости, которые ожидает `PostInitialize`

`PostInitialize(ExternalDependencies externalDependencies)` ожидает:

- зарегистрированный в external dependencies экземпляр `DOTSWorlds`;
- доступный в config root `DotsPrefabLibConfig`.

Во время инициализации он:

- получает `externalDependencies.Get<DOTSWorlds>().Main`;
- сохраняет `EntityManager`;
- читает `DotsPrefabLibConfig`;
- переиндексирует все доступные на текущий момент prefab catalog-буферы мира;
- выполняет preload настроенных ассетов через `PreloadAsync()`.

#### API загрузки префабов

| Метод | Назначение | Технические детали |
| --- | --- | --- |
| `LoadPrefabAsync(string key)` | Загружает по логическому строковому ключу | Нормализует ключ, дедуплицирует in-flight загрузки, проверяет наличие `PrefabRoot` |
| `LoadPrefabAsync(EntityPrefabReference epr)` | Загружает напрямую по prefab reference | Использует GUID-кэш |
| `WarmupAssetAsync(string key)` | Алиас для preload или warmup префаба | Внутри вызывает `LoadPrefabAsync(key)` |
| `Release(string key)` | Уничтожает одну загруженную prefab-сущность и удаляет её из кэша | Управление lifetime вручную |
| `ReleaseAll()` | Уничтожает все закэшированные префабы | Очищает prefab cache и таблицу in-flight загрузок |

Что важно знать про внутреннее поведение:

- `_keyToRef` строится сканированием всех сущностей, у которых есть и `DotsPrefabCatalogTag`, и `DotsPrefabCatalogEntry`;
- ключи нормализуются через `NormalizePath()` до lookup;
- библиотека использует `_inflightPrefabs` и `_inflightByGuid`, чтобы не дублировать асинхронную загрузку;
- после успешной загрузки root-сущность кешируется и получает имя через `_em.SetName(prefabEntity, fullKey)`.

Важное ограничение:

- lifetime префабов управляется вручную;
- по префабам нет reference counting на уровне вызывающих систем;
- если один и тот же префаб используют несколько подсистем, координация `Release` остаётся на стороне caller-а или application layer.

#### API загрузки сцен

| Метод | Назначение | Технические детали |
| --- | --- | --- |
| `LoadSceneAsync(EntitySceneReference sceneRef, string debugName = null, bool autoLoad = true, bool blockOnStreamIn = false, bool keepMetaEntitiesAlive = false)` | Асинхронно загружает ECS-сцену | Возвращает meta entity сцены |
| `UnloadScene(EntitySceneReference sceneRef, bool destroyMetaEntities = true)` | Уменьшает ref-count и выгружает сцену при достижении нуля | Поддерживает deferred unload, если загрузка ещё не завершилась |
| `UnloadAllScenes(bool destroyMetaEntities = true)` | Выгружает все отслеживаемые сцены | Очищает внутренние таблицы bookkeeping |

Детали scene-management:

- сцены отслеживаются по GUID в `_sceneByGuid`;
- каждая запись хранит meta entity, ref count, debug name и флаг `KeepMetaAlive`;
- `_inflightSceneLoads` дедуплицирует параллельные запросы на загрузку;
- `_pendingReleaseWhileLoading` обрабатывает гонку, когда unload пришёл раньше окончания async load;
- `PruneSceneBookkeeping()` очищает устаревшие записи с нулевым ref count, если сцена уже не загружена.

Динамическое расширение каталогов:

- после загрузки или выгрузки сцены вызывается `ReindexAllCatalogs()`;
- это означает, что только что загруженные сцены могут принести дополнительные `DotsPrefabCatalogTag` буферы;
- на практике набор доступных prefab-ключей может расширяться вместе со stream loading контентных сцен.

Это одно из самых сильных свойств библиотеки: discovery контента может расти вместе со streaming-сценами, а не фиксироваться только на старте приложения.

Текущее замечание по реализации:

- `keepMetaEntitiesAlive` сохраняется во внутреннем bookkeeping;
- но фактическое поведение unload сейчас определяется аргументом `destroyMetaEntities` в `UnloadScene` / `UnloadAllScenes`;
- то есть `KeepMetaAlive` пока является сохранённым состоянием, а не полностью реализованной отдельной политикой выгрузки.

### `DotsPrefabLibConfig.cs` и `DotsPrefabLibScriptableConfig.cs`

Конфигурация намеренно сделана маленькой:

- `DotsPrefabLibConfig` содержит `List<string> PreloadAssets`;
- `DotsPrefabLibScriptableConfig` открывает эту конфигурацию как `ScriptableObject`.

Поток preload выглядит так:

1. Ключи добавляются в `PreloadAssets`.
2. `DotsPrefabLib.PostInitialize()` вызывает `PreloadAsync()`.
3. Каждый непустой ключ нормализуется и прогревается через `WarmupAssetAsync()`.

Такой подход делает startup warmup явным и управляемым через данные.

### `PingAuthoring.cs`

`PingAuthoring` — минимальный authoring-компонент, который bake-ит `PingTag`.

Его удобно использовать как:

- smoke-test baker;
- простой marker entity;
- минимальный пример authoring-компонента и tag baker-а.

## Зависимости

### Зависимости на уровне AppSDK-репозиториев

Значения веток ниже взяты из `.gitmodules` хост-проекта. Если ветка там не указана, репозиторий помечен как не зафиксированный по ветке.

| Модуль | Зачем нужен в этом репозитории | Репозиторий | Ветка в хост-проекте |
| --- | --- | --- | --- |
| `DingoProjectAppStructure` | Даёт `HardLinkAppModelBase`, config-абстракции и `ExternalDependencies`, которые использует `DotsPrefabLib` | [github.com/DingoBite/DingoProjectAppStructure](https://github.com/DingoBite/DingoProjectAppStructure.git) | не зафиксирована в `.gitmodules` |
| `DingoUnityExtensions` | Даёт helper-методы `NormalizePath()` и extension-методы `Transform.SetTRS(...)`, используемые каталогами и bridge-кодом | [github.com/DingoBite/DingoUnityExtensions](https://github.com/DingoBite/DingoUnityExtensions) | `dev` |
| `AppStructure` | Сопутствующий модуль прикладной архитектуры, который обычно используется вместе с `DingoProjectAppStructure` в том же AppSDK-стеке | [github.com/DingoBite/AppStructure](https://github.com/DingoBite/AppStructure) | `string-as-key-refactor` |

### Дополнительные package-зависимости

- Unity Entities или Scenes API с поддержкой `Baker<T>`, `TransformUsageFlags`, `EntityPrefabReference` и `EntitySceneReference`;
- `Cysharp.Threading.Tasks` (`UniTask`);
- `AYellowpaper.SerializedCollections`;
- `NaughtyAttributes`.

Про инспекторные кнопки:

- в коде есть условная ветка `VINSPECTOR_EXISTS`;
- но `DotsPrefabCatalogAuthoring.cs` напрямую импортирует `NaughtyAttributes`;
- поэтому в текущем состоянии репозитория `NaughtyAttributes` фактически остаётся compile-time зависимостью, если файл специально не изменить.

## Установка

### Вариант 1. Git submodule

```bash
git submodule add https://github.com/DingoBite/DingoECSUtils.git Assets/AppSDK/DingoECSUtils
git submodule update --init --recursive
```

### Вариант 2. Копирование в Unity-проект

Скопируйте папки репозитория в `Assets/` вашего Unity-проекта и сохраните `.meta`-файлы.

## Гид по интеграции

### Минимальный bootstrap setup

1. Добавьте `DOTSWorlds` в bootstrap-сцену.
2. Назначьте входную `SubScene`, если мир должен автоматически загружать её при старте.
3. Инициализируйте `DOTSWorlds` до того, как остальной runtime-слой начнёт запрашивать ECS-доступ.
4. Зарегистрируйте экземпляр `DOTSWorlds` в контейнере внешних зависимостей.
5. Зарегистрируйте `DotsPrefabLibConfig` в config root, если нужен preload.
6. Зарегистрируйте `DotsPrefabLib` в app model-слое, если ваша архитектура использует модели на базе `HardLinkAppModelBase`.

Пример bootstrap-паттерна:

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

### Authoring prefab-каталога

Типовой поток:

1. Создайте `GameObject`, который будет содержать `DotsPrefabCatalogAuthoring`.
2. Заполните `_catalogPath` и `_keysPrefix`.
3. Решите, нужно ли включать имя префаба перед тегом.
4. Либо заполните записи вручную, либо используйте кнопки автонаполнения.
5. Разместите authoring-объект в bake-сцене или subscene, чтобы baker создал ECS-данные.

Практические советы:

- используйте `_addPrefabNamesBeforeTag = true`, если ключи должны оставаться читаемыми в больших каталогах;
- используйте строгие namespace-ы в `_catalogPath` и `_keysPrefix`, чтобы избежать коллизий между gameplay-доменами;
- относитесь к финальному ключу как к публичному API, на который опираются runtime-код и конфигурация.

### Загрузка префаба в runtime

Пример:

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

### Загрузка сцен по имени каталога

Пример:

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

### Мост ECS transform -> `GameObject`

Пример:

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

Этот паттерн особенно полезен, когда:

- симуляция уже живёт в ECS;
- представление всё ещё остаётся на классических Unity-объектах;
- команда мигрирует на DOTS постепенно, а не переписывает весь проект за один шаг.

## Преимущества решения

### 1. Явное владение миром

`DOTSWorlds` даёт понятный и централизованный ответ на вопросы старта мира, загрузки входной subscene и корректного teardown.

### 2. Адресация контента через логические ключи

Gameplay-код может зависеть от стабильных человекочитаемых ключей, а не от разбросанных по проекту `EntityPrefabReference`.

### 3. DOTS-native runtime-данные

Authoring-данные запекаются в ECS-буферы. В runtime больше не нужно ходить в editor-only ассеты и инспекторные словари.

### 4. Дедупликация асинхронной загрузки

Параллельные запросы на один и тот же префаб или сцену используют общий in-flight task вместо повторной загрузки.

### 5. Динамическое расширение каталогов через загруженные сцены

Так как библиотека переиндексирует `DotsPrefabCatalogTag` после изменений сцен, stream loading сцен может автоматически расширять набор доступных prefab-ключей.

### 6. Удобство для гибридных проектов

Transform bridge и authoring-first workflow помогают переходить на ECS постепенно, не отказываясь сразу от `GameObject`-view слоя и привычного editor tooling.

### 7. Небольшой helper API при заметном снижении boilerplate

`ECSUtils` маленький по объёму, но снимает типовой шаблонный код add-or-overwrite, который иначе быстро расползается по baker-ам и spawn-пайплайнам.

### 8. Warmup через конфигурацию

`PreloadAssets` делает стартовый прогрев ассетов явным и управляемым через данные, а не через скрытый bootstrap-код.

## Ограничения и замечания по реализации

- Это `Assets/`-ориентированный Unity-репозиторий, а не UPM-пакет.
- Authoring-компоненты завязаны на editor-only сущности вроде `SceneAsset` и инспекторных словарей, тогда как runtime потребляет baked ECS-буферы.
- Освобождение префабов ручное и без reference counting.
- Освобождение сцен идёт с reference counting по GUID сцены.
- Стабильность итоговых ключей критична: изменение правил каталогизации меняет строковый API для runtime-кода и preload-конфигов.
- `CopyTransformToGOSystem` зеркалит трансформ только из ECS в managed-объект.
- `KeepMetaAlive` пока хранится, но не применяется как отдельная полностью реализованная политика выгрузки.

## Когда этот репозиторий подходит лучше всего

Используйте `DingoECSUtils`, если проекту нужно сразу несколько из следующих свойств:

- единая точка владения DOTS `World`;
- загрузка ECS-префабов по человекочитаемым ключам;
- загрузка ECS-сцен по логическим именам;
- streaming-сцены, которые могут приносить новые runtime-каталоги контента;
- мост между ECS-симуляцией и классическим Unity presentation-слоем;
- совместимость с model- или config-driven application layer.

Если проект уже является полностью чистым ECS-кодом со своим bootstrap и команда предпочитает везде работать напрямую с `EntityPrefabReference` и `EntitySceneReference`, этот репозиторий может показаться намеренно более высокоуровневым, чем требуется.
