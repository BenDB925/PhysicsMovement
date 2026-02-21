using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PhysicsDrivenMovement.Core;

namespace PhysicsDrivenMovement.Editor
{
    /// <summary>
    /// Editor utility that creates the Arena_01 test scene from scratch.
    /// Builds a flat ground plane (20×1×20), adds a directional light and ambient,
    /// places four spawn points at arena corners, and ensures the scene includes a
    /// GameSettings initialiser GameObject.
    /// Access via: Tools → PhysicsDrivenMovement → Build Test Scene.
    /// Collaborators: <see cref="PhysicsDrivenMovement.Core.GameSettings"/>.
    /// </summary>
    public static class SceneBuilder
    {
        private const string ScenePath     = "Assets/Scenes/Arena_01.unity";
        private const float  ArenaHalfSize = 10f;

        [MenuItem("Tools/PhysicsDrivenMovement/Build Test Scene")]
        public static void BuildTestScene()
        {
            // STEP 1: Create a new empty scene and save it.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // STEP 2: Directional light.
            GameObject lightGO = new GameObject("Directional Light");
            Light light = lightGO.AddComponent<Light>();
            light.type      = LightType.Directional;
            light.intensity = 1f;
            light.color     = Color.white;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // STEP 3: Ground plane — a scaled cube acting as the floor.
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name                     = "Ground";
            ground.transform.localScale     = new Vector3(20f, 1f, 20f);
            ground.transform.localPosition  = new Vector3(0f, -0.5f, 0f);

            // Apply a default material tint to distinguish it visually.
            Renderer groundRenderer = ground.GetComponent<Renderer>();
            if (groundRenderer != null)
            {
                Material mat = new Material(Shader.Find("Standard"))
                {
                    color = new Color(0.35f, 0.55f, 0.35f)
                };
                groundRenderer.sharedMaterial = mat;
            }

            // STEP 4: Spawn points at the four corners of the usable area.
            Vector3[] spawnPositions =
            {
                new Vector3(-ArenaHalfSize + 2f, 0.5f,  ArenaHalfSize - 2f),
                new Vector3( ArenaHalfSize - 2f, 0.5f,  ArenaHalfSize - 2f),
                new Vector3(-ArenaHalfSize + 2f, 0.5f, -ArenaHalfSize + 2f),
                new Vector3( ArenaHalfSize - 2f, 0.5f, -ArenaHalfSize + 2f),
            };

            for (int i = 0; i < spawnPositions.Length; i++)
            {
                GameObject spawn = new GameObject($"SpawnPoint_{i + 1}");
                spawn.transform.localPosition = spawnPositions[i];
            }

            // STEP 5: GameSettings initialiser — ensures physics is configured even
            //         if the project settings YAML was not applied at project open.
            GameObject settingsGO = new GameObject("GameSettings");
            settingsGO.AddComponent<GameSettings>();

            // STEP 6: Camera placeholder.
            GameObject camGO = new GameObject("Main Camera");
            Camera cam = camGO.AddComponent<Camera>();
            cam.clearFlags      = CameraClearFlags.Skybox;
            cam.fieldOfView     = 60f;
            camGO.transform.SetPositionAndRotation(
                new Vector3(0f, 6f, -12f),
                Quaternion.Euler(20f, 0f, 0f));
            camGO.tag = "MainCamera";

            // STEP 7: Save the scene.
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[SceneBuilder] Test scene saved to '{ScenePath}'.");
        }
    }
}
