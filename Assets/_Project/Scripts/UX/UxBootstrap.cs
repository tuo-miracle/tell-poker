using Poker.Game;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Poker.UX
{
    /// <summary>執行期自動把 UxController 掛到場景中的 GameController（不改場景、不改 GameController）。</summary>
    public static class UxBootstrap
    {
        public static bool AutoAttach = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Install()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            AttachAll();
        }

        static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachAll();

        public static void AttachAll()
        {
            if (!AutoAttach) return;
            foreach (var gc in Object.FindObjectsByType<GameController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                UxController.Attach(gc);
        }
    }
}
