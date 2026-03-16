using System.Reflection;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PhysicsDrivenMovement.Editor
{
    /// <summary>
    /// Editor utility that creates the Arena_01 terrain-validation scene from scratch.
    /// Rebuilds the current live scene contract: wide flat baseline ground, in-scene
    /// PlayerRagdoll prefab instance, disabled LapRunner harness, camera follow setup,
    /// spawn points, and the Chapter 7 controlled terrain gallery.
    /// Access via: Tools → PhysicsDrivenMovement → Build Test Scene.
    /// Collaborators: <see cref="PhysicsDrivenMovement.Core.GameSettings"/>,
    /// <see cref="PlayerMovement"/>, <see cref="LapDemoRunner"/>,
    /// <see cref="TerrainScenarioBuilder"/>.
    /// </summary>
    public static class SceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/Arena_01.unity";
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";
        private const string PhysMatPath = "Assets/PhysicsMaterials/Ragdoll.asset";

        private static readonly Vector3 GroundScale = new Vector3(70.72f, 1f, 63.6f);

        [MenuItem("Tools/PhysicsDrivenMovement/Build All Environment Scenes")]
        public static void BuildAllEnvironmentScenes()
        {
            BuildTestScene();
            ArenaBuilder.BuildMuseumArena();
        }

        [MenuItem("Tools/PhysicsDrivenMovement/Build Test Scene")]
        public static void BuildTestScene()
        {
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            PhysicsMaterial physicsMat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PhysMatPath);
            if (playerPrefab == null)
            {
                Debug.LogError($"[SceneBuilder] PlayerRagdoll prefab not found at '{PlayerRagdollPrefabPath}'.");
                return;
            }

            if (physicsMat == null)
            {
                Debug.LogError($"[SceneBuilder] PhysicsMaterial not found at '{PhysMatPath}'. Run 'Build Player Ragdoll' first.");
                return;
            }

            // STEP 1: Create a new empty scene and rebuild the lighting + runtime scaffolding.
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildDirectionalLight();
            BuildGround();
            BuildSpawnPoints();
            BuildGameSettings();
            BuildCamera();

            // STEP 2: Restore the in-scene player prefab instance and the disabled lap harness.
            BuildScenePlayer(scene, playerPrefab);
            BuildLapRunner(playerPrefab);

            // STEP 3: Author the Chapter 7 terrain gallery away from the flat baseline corridor.
            BuildTerrainGallery(physicsMat);

            // STEP 4: Save the rebuilt scene.
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[SceneBuilder] Test scene saved to '{ScenePath}'.");
        }

        private static void BuildDirectionalLight()
        {
            GameObject lightGO = new GameObject("Directional Light");
            Light light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1f;
            light.color = Color.white;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void BuildGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "Ground";
            ground.layer = GameSettings.LayerEnvironment;
            ground.transform.localScale = GroundScale;
            ground.transform.localPosition = new Vector3(0f, -0.5f, 0f);

            Renderer groundRenderer = ground.GetComponent<Renderer>();
            if (groundRenderer != null)
            {
                Material material = new Material(Shader.Find("Standard"))
                {
                    color = new Color(0.35f, 0.55f, 0.35f),
                };
                groundRenderer.sharedMaterial = material;
            }
        }

        private static void BuildSpawnPoints()
        {
            Vector3[] spawnPositions =
            {
                new Vector3(-8f, 0.5f, 8f),
                new Vector3(8f, 0.5f, 8f),
                new Vector3(-8f, 0.5f, -8f),
                new Vector3(8f, 0.5f, -8f),
            };

            for (int spawnIndex = 0; spawnIndex < spawnPositions.Length; spawnIndex++)
            {
                GameObject spawn = new GameObject($"SpawnPoint_{spawnIndex + 1}");
                spawn.transform.localPosition = spawnPositions[spawnIndex];
            }
        }

        private static void BuildGameSettings()
        {
            GameObject settingsGO = new GameObject("GameSettings");
            settingsGO.AddComponent<GameSettings>();
        }

        private static void BuildCamera()
        {
            GameObject cameraGO = new GameObject("Main Camera");
            Camera camera = cameraGO.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.fieldOfView = 60f;
            cameraGO.AddComponent<AudioListener>();
            cameraGO.AddComponent<CameraFollow>();
            cameraGO.transform.SetPositionAndRotation(
                new Vector3(-0.73f, 1.72f, -11.27f),
                Quaternion.Euler(20f, 0f, 0f));
            cameraGO.tag = "MainCamera";
        }

        private static void BuildScenePlayer(Scene scene, GameObject playerPrefab)
        {
            GameObject playerInstance = PrefabUtility.InstantiatePrefab(playerPrefab, scene) as GameObject;
            if (playerInstance == null)
            {
                Debug.LogError("[SceneBuilder] Failed to instantiate PlayerRagdoll prefab into Arena_01.");
                return;
            }

            playerInstance.name = playerPrefab.name;

            FallPoseRecorder recorder = playerInstance.GetComponentInChildren<FallPoseRecorder>(includeInactive: true);
            if (recorder != null)
            {
                FieldInfo recordContinuousField = typeof(FallPoseRecorder).GetField(
                    "_recordContinuousSamples",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                recordContinuousField?.SetValue(recorder, false);
            }
        }

        private static void BuildLapRunner(GameObject playerPrefab)
        {
            GameObject lapRunnerGO = new GameObject("LapRunner");
            LapDemoRunner lapRunner = lapRunnerGO.AddComponent<LapDemoRunner>();

            FieldInfo prefabField = typeof(LapDemoRunner).GetField(
                "_playerRagdollPrefab",
                BindingFlags.Instance | BindingFlags.NonPublic);
            prefabField?.SetValue(lapRunner, playerPrefab);

            lapRunnerGO.SetActive(false);
        }

        private static void BuildTerrainGallery(PhysicsMaterial physicsMat)
        {
            GameObject terrainRoot = TerrainScenarioBuilder.CreateScenarioContainer(parent: null);

            Material slopeMaterial = TerrainScenarioBuilder.CreateSurfaceMaterial("Arena01_SlopeLane", new Color(0.68f, 0.47f, 0.27f));
            Material stepUpMaterial = TerrainScenarioBuilder.CreateSurfaceMaterial("Arena01_StepUpLane", new Color(0.65f, 0.52f, 0.32f));
            Material stepDownMaterial = TerrainScenarioBuilder.CreateSurfaceMaterial("Arena01_StepDownLane", new Color(0.54f, 0.43f, 0.28f));
            Material unevenMaterial = TerrainScenarioBuilder.CreateSurfaceMaterial("Arena01_UnevenPatch", new Color(0.42f, 0.52f, 0.31f));
            Material obstacleMaterial = TerrainScenarioBuilder.CreateSurfaceMaterial("Arena01_LowObstacleLane", new Color(0.44f, 0.34f, 0.20f));

            TerrainScenarioBuilder.BuildSlopeLane(
                terrainRoot.transform,
                "Arena01_SlopeLane",
                new Vector3(-22f, 0f, 18f),
                TerrainScenarioBuilder.TerrainLaneAxis.X,
                3.4f,
                9f,
                1.1f,
                slopeMaterial,
                physicsMat);

            TerrainScenarioBuilder.BuildStepUpLane(
                terrainRoot.transform,
                "Arena01_StepUpLane",
                new Vector3(0f, 0f, 18f),
                TerrainScenarioBuilder.TerrainLaneAxis.X,
                3.2f,
                8.2f,
                0.75f,
                stepUpMaterial,
                physicsMat);

            TerrainScenarioBuilder.BuildStepDownLane(
                terrainRoot.transform,
                "Arena01_StepDownLane",
                new Vector3(22f, 0f, 18f),
                TerrainScenarioBuilder.TerrainLaneAxis.X,
                3.2f,
                8.2f,
                0.75f,
                stepDownMaterial,
                physicsMat);

            TerrainScenarioBuilder.BuildUnevenPatch(
                terrainRoot.transform,
                "Arena01_UnevenPatch",
                new Vector3(-16f, 0f, -18f),
                new Vector2(8f, 5.5f),
                unevenMaterial,
                physicsMat);

            TerrainScenarioBuilder.BuildLowObstacleLane(
                terrainRoot.transform,
                "Arena01_LowObstacleLane",
                new Vector3(16f, 0f, -18f),
                TerrainScenarioBuilder.TerrainLaneAxis.X,
                3.2f,
                9f,
                obstacleMaterial,
                physicsMat);
        }
    }
}
