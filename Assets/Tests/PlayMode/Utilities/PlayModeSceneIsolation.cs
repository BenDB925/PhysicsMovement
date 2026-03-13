using UnityEngine.SceneManagement;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Moves prefab-backed PlayMode tests onto a fresh active runtime scene so prior
    /// scene-loading fixtures do not keep new test objects inside authored scenes.
    /// </summary>
    internal static class PlayModeSceneIsolation
    {
        private static int s_sceneCounter;

        public static void ResetToEmptyScene()
        {
            Scene scene = SceneManager.CreateScene($"PlayModeIsolationScene_{++s_sceneCounter}");
            SceneManager.SetActiveScene(scene);
        }
    }
}