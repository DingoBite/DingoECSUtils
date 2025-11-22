using Unity.Entities;
using Unity.Scenes;
using UnityEditor;
using UnityEngine;

namespace DingoECSUtils.Utils
{
    public class DOTSWorlds : MonoBehaviour
    {
        [SerializeField] private string _worldName = "DiceGame World";
        [SerializeField] private SubScene _entryPointSubScene;
        [SerializeField] private bool _autoStart;

        public World Main { get; private set; }

        private void Start()
        {
            if (_autoStart)
                Initialize();
        }

        public void Initialize()
        {
#if UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP_RUNTIME_WORLD
            if (World.DefaultGameObjectInjectionWorld != null)
            {
                ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(World.DefaultGameObjectInjectionWorld);
                World.DefaultGameObjectInjectionWorld.Dispose();
            }
            Main = new World(_worldName);
            World.DefaultGameObjectInjectionWorld = Main;
            
            var systems = DefaultWorldInitialization.GetAllSystems(WorldSystemFilterFlags.Default);
            DefaultWorldInitialization.AddSystemsToRootLevelSystemGroups(Main, systems);
            ScriptBehaviourUpdateOrder.AppendWorldToCurrentPlayerLoop(Main);
#else
            Main = World.DefaultGameObjectInjectionWorld;
#endif
            LoadEntryPointSubScene();
        }

        public void LoadEntryPointSubScene()
        {
            if (_entryPointSubScene != null)
                SceneSystem.LoadSceneAsync(Main.Unmanaged, _entryPointSubScene.SceneGUID);
        }
        
        private void OnDestroy() => Teardown();

#if UNITY_EDITOR
        private void OnEnable() => EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        private void OnDisable() => EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;

        private void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode)
                Teardown();
        }
#endif

        private void Teardown()
        {
#if UNITY_DISABLE_AUTOMATIC_SYSTEM_BOOTSTRAP_RUNTIME_WORLD
            if (Main != null && Main.IsCreated)
            {
                ScriptBehaviourUpdateOrder.RemoveWorldFromCurrentPlayerLoop(Main);
                Main.Dispose();
                if (World.DefaultGameObjectInjectionWorld == Main)
                    World.DefaultGameObjectInjectionWorld = null;
                Main = null;
            }
#endif
        }
    }
}