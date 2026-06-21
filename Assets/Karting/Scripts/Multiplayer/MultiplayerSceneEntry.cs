using UnityEngine;
using UnityEngine.SceneManagement;

namespace KartGame.Multiplayer
{
    public static class MultiplayerSceneEntry
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void RegisterSceneHooks()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            if (scene.name == MainSceneMultiplayerBootstrap.SceneName)
                EnsureBootstrapObject();
        }

        static void EnsureBootstrapObject()
        {
            if (Object.FindObjectOfType<MainSceneMultiplayerBootstrap>() != null)
                return;

            var bootstrapObject = new GameObject("MainSceneMultiplayerBootstrap");
            bootstrapObject.AddComponent<MainSceneMultiplayerBootstrap>();
        }
    }
}
