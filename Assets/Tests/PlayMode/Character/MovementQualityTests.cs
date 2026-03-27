using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEngine;
using UnityEngine.TestTools;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    public class MovementQualityTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";
        private const string FallPoseLogFileName = "movement-quality-fall-pose.ndjson";

        private const int CollapseEvidenceFrameBudget = 90;
        private const int PlaybackWarmUpFrames = 30;
        private const float FixedDeltaTimeTolerance = 0.0001f;

        private GameObject _ground;
        private GameObject _testCamera;
        private GameObject _player;

        private CharacterState _characterState;

        private float _savedFixedDeltaTime;
        private int _savedSolverIterations;
        private int _savedSolverVelocityIterations;
        private bool[,] _savedLayerCollisionMatrix;

        [SetUp]
        public void SetUp()
        {
            _savedFixedDeltaTime = Time.fixedDeltaTime;
            _savedSolverIterations = Physics.defaultSolverIterations;
            _savedSolverVelocityIterations = Physics.defaultSolverVelocityIterations;
            _savedLayerCollisionMatrix = CaptureLayerCollisionMatrix();

            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;

            Physics.IgnoreLayerCollision(GameSettings.LayerPlayer1Parts, GameSettings.LayerPlayer1Parts, true);
            Physics.IgnoreLayerCollision(GameSettings.LayerPlayer1Parts, GameSettings.LayerEnvironment, false);
            Physics.IgnoreLayerCollision(GameSettings.LayerLowerLegParts, GameSettings.LayerEnvironment, true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_player != null)
            {
                UnityEngine.Object.Destroy(_player);
            }

            if (_ground != null)
            {
                UnityEngine.Object.Destroy(_ground);
            }

            if (_testCamera != null)
            {
                UnityEngine.Object.Destroy(_testCamera);
                _testCamera = null;
            }

            RestoreLayerCollisionMatrix(_savedLayerCollisionMatrix);

            Time.fixedDeltaTime = _savedFixedDeltaTime;
            Physics.defaultSolverIterations = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
        }

        private void CreatePlaybackCamera(float yawDegrees)
        {
            _testCamera = new GameObject("PlaybackCamera");
            _testCamera.transform.rotation = Quaternion.Euler(0f, yawDegrees, 0f);
            _testCamera.transform.position = new Vector3(0f, 5f, -10f);
            Camera cam = _testCamera.AddComponent<Camera>();
            cam.tag = "MainCamera";
        }

        [UnityTest]
        public IEnumerator WalkStraight_RecordedPlayback_NoFalls()
        {
            InputPlayback playback = LoadRecordingOrIgnore("walk-straight");
            yield return SpawnAndConfigurePlayer();
            CreatePlaybackCamera(playback.CameraYaw);

            PlayerMovement playerMovement = _player.GetComponentInChildren<PlayerMovement>();
            Assert.That(playerMovement, Is.Not.Null, "PlayerMovement must exist for recorded playback.");
            AssertPlaybackFixedDeltaTimeMatches(playback, "walk-straight");

            for (int frame = 0; frame < PlaybackWarmUpFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            bool enteredFallen = false;
            int framesApplied = 0;

            // Only count Fallen during active playback, not during the last 60 frames
            // where inputs naturally taper off and the character decelerates.
            int fallenCheckUntil = playback.FrameCount - 60;

            for (int frame = 0; frame < playback.FrameCount; frame++)
            {
                playback.ApplyFrame(frame, playerMovement);
                framesApplied++;
                yield return new WaitForFixedUpdate();
                if (frame < fallenCheckUntil)
                    enteredFallen |= _characterState.CurrentState == CharacterStateType.Fallen;
            }

            playerMovement.SetMoveInputForTest(Vector2.zero);
            playerMovement.SetJumpInputForTest(false);

            // Brief coast: let the character settle after inputs stop.
            for (int frame = 0; frame < 60; frame++)
                yield return new WaitForFixedUpdate();

            LogBaseline(
                nameof(WalkStraight_RecordedPlayback_NoFalls),
                $"framesApplied={framesApplied}/{playback.FrameCount} enteredFallen={enteredFallen}");

            Assert.That(enteredFallen, Is.False,
                $"Recorded walk-straight playback entered Fallen after {framesApplied} frames.");

            Assert.That(framesApplied, Is.EqualTo(playback.FrameCount),
                $"Walk-straight playback did not complete all frames ({framesApplied}/{playback.FrameCount}).");
        }

        [UnityTest]
        public IEnumerator TurnAndWalk_RecordedPlayback_NoFalls()
        {
            InputPlayback playback = LoadRecordingOrIgnore("turn-and-walk");
            yield return SpawnAndConfigurePlayer();
            CreatePlaybackCamera(playback.CameraYaw);

            PlayerMovement playerMovement = _player.GetComponentInChildren<PlayerMovement>();
            Assert.That(playerMovement, Is.Not.Null, "PlayerMovement must exist for recorded playback.");
            AssertPlaybackFixedDeltaTimeMatches(playback, "turn-and-walk");

            for (int frame = 0; frame < PlaybackWarmUpFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            bool enteredFallen = false;
            int framesApplied = 0;

            int fallenCheckUntil = playback.FrameCount - 60;

            for (int frame = 0; frame < playback.FrameCount; frame++)
            {
                playback.ApplyFrame(frame, playerMovement);
                framesApplied++;
                yield return new WaitForFixedUpdate();
                if (frame < fallenCheckUntil)
                    enteredFallen |= _characterState.CurrentState == CharacterStateType.Fallen;
            }

            playerMovement.SetMoveInputForTest(Vector2.zero);
            playerMovement.SetJumpInputForTest(false);

            for (int frame = 0; frame < 60; frame++)
                yield return new WaitForFixedUpdate();

            LogBaseline(
                nameof(TurnAndWalk_RecordedPlayback_NoFalls),
                $"framesApplied={framesApplied}/{playback.FrameCount} enteredFallen={enteredFallen}");

            Assert.That(enteredFallen, Is.False,
                $"Recorded turn-and-walk playback entered Fallen after {framesApplied} frames.");

            Assert.That(framesApplied, Is.EqualTo(playback.FrameCount),
                $"Turn-and-walk playback did not complete all frames ({framesApplied}/{playback.FrameCount}).");
        }

        [UnityTest]
        public IEnumerator LowProgressWithoutRearSupport_DoesNotTransitionIntoFallen()
        {
            yield return SpawnAndConfigurePlayer();

            PlayerMovement playerMovement = _player.GetComponentInChildren<PlayerMovement>();
            BalanceController balanceController = _player.GetComponentInChildren<BalanceController>();
            Rigidbody hipsBody = playerMovement.GetComponent<Rigidbody>();
            Rigidbody footLeftBody = FindRequiredChild("Foot_L").GetComponent<Rigidbody>();
            Rigidbody footRightBody = FindRequiredChild("Foot_R").GetComponent<Rigidbody>();

            Assert.That(playerMovement, Is.Not.Null, "PlayerMovement must exist for collapse false-positive guard.");
            Assert.That(balanceController, Is.Not.Null, "BalanceController must exist for collapse false-positive guard.");
            Assert.That(hipsBody, Is.Not.Null, "PlayerMovement Rigidbody must exist for collapse false-positive guard.");
            Assert.That(footLeftBody, Is.Not.Null, "Foot_L Rigidbody must exist for collapse false-positive guard.");
            Assert.That(footRightBody, Is.Not.Null, "Foot_R Rigidbody must exist for collapse false-positive guard.");

            for (int frame = 0; frame < CollapseEvidenceFrameBudget; frame++)
            {
                ApplySyntheticCollapseEvidence(
                    hipsBody,
                    footLeftBody,
                    footRightBody,
                    playerMovement,
                    balanceController,
                    requestedDirection: Vector3.forward,
                    supportBehindDistance: 0.05f,
                    uprightAngleDeg: 22f,
                    grounded: true);

                yield return new WaitForFixedUpdate();
            }

            LogBaseline(
                nameof(LowProgressWithoutRearSupport_DoesNotTransitionIntoFallen),
                $"finalState={_characterState.CurrentState} frameBudget={CollapseEvidenceFrameBudget}");

            Assert.That(_characterState.CurrentState, Is.Not.EqualTo(CharacterStateType.Fallen),
                "Low progress alone must not transition into Fallen when the feet remain under the hips.");
        }

        private IEnumerator SpawnAndConfigurePlayer()
        {
            // Ground: flat cube 60x60 at y=-0.5 so surface is exactly y=0.
            // Must be LayerEnvironment (12) so GroundSensor and hips collide with it.
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "TestGround";
            _ground.transform.position   = new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(60f, 1f, 60f);
            _ground.layer = GameSettings.LayerEnvironment;

#if UNITY_EDITOR
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
#else
            GameObject prefab = null;
#endif
            Assert.That(prefab, Is.Not.Null,
                $"PlayerRagdoll prefab was not found at '{PlayerRagdollPrefabPath}'.");

            // Spawn high enough to avoid starting interpenetrated with the floor.
            _player = UnityEngine.Object.Instantiate(prefab, new Vector3(0f, 0.5f, 0f), Quaternion.identity);
            Assert.That(_player, Is.Not.Null, "Failed to instantiate PlayerRagdoll prefab.");

            PlayerMovement playerMovement = _player.GetComponentInChildren<PlayerMovement>();
            Assert.That(playerMovement, Is.Not.Null, "PlayerRagdoll prefab is missing PlayerMovement.");
            SetPrivateFloat(playerMovement, "_moveForce", 1500f);
            SetPrivateFloat(playerMovement, "_maxSpeed", 8f);

            FallPoseRecorder recorder = playerMovement.gameObject.AddComponent<FallPoseRecorder>();
            ConfigureFallRecorder(recorder);

            _characterState = _player.GetComponentInChildren<CharacterState>();
            Assert.That(_characterState, Is.Not.Null, "PlayerRagdoll prefab is missing CharacterState.");

            // Warmup: 60 frames with zero input so physics settles before we start driving.
            // Without this the ragdoll is mid-fall when input starts and falls immediately.
            playerMovement.SetMoveInputForTest(Vector2.zero);
            for (int i = 0; i < 60; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private Transform FindRequiredChild(string childName)
        {
            Transform child = null;
            Transform[] children = _player.GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == childName)
                {
                    child = children[i];
                    break;
                }
            }

            Assert.That(child, Is.Not.Null, $"Required child '{childName}' was not found under the spawned ragdoll.");
            return child;
        }

        private static void ApplySyntheticCollapseEvidence(
            Rigidbody hipsBody,
            Rigidbody footLeftBody,
            Rigidbody footRightBody,
            PlayerMovement playerMovement,
            BalanceController balanceController,
            Vector3 requestedDirection,
            float supportBehindDistance,
            float uprightAngleDeg,
            bool grounded)
        {
            Vector3 flatRequestedDirection = Vector3.ProjectOnPlane(requestedDirection, Vector3.up).normalized;
            Vector3 lateralOffset = Vector3.right * 0.18f;
            Vector3 footBasePosition = hipsBody.position - flatRequestedDirection * supportBehindDistance + Vector3.up * 0.02f;

            // STEP 1: Freeze progress so the state machine sees sustained move intent with no forward travel.
            hipsBody.linearVelocity = Vector3.zero;
            hipsBody.angularVelocity = Vector3.zero;
            hipsBody.MoveRotation(Quaternion.AngleAxis(uprightAngleDeg, Vector3.right));

            // STEP 2: Keep the support center behind the hips along the requested direction.
            footLeftBody.position = footBasePosition - lateralOffset;
            footRightBody.position = footBasePosition + lateralOffset;
            footLeftBody.linearVelocity = Vector3.zero;
            footRightBody.linearVelocity = Vector3.zero;
            footLeftBody.angularVelocity = Vector3.zero;
            footRightBody.angularVelocity = Vector3.zero;

            // STEP 3: Hold strong intent and grounded state so collapse confirmation can accumulate.
            playerMovement.SetMoveInputForTest(Vector2.up);
            balanceController.SetGroundStateForTest(grounded, isFallen: false);
        }

        private static InputPlayback LoadRecordingOrIgnore(string recordingName)
        {
            string recordingPath = InputPlayback.GetRecordingPath(recordingName);
            if (!File.Exists(recordingPath))
            {
                Assert.Ignore($"No {recordingName} recording yet.");
            }

            return InputPlayback.Load(recordingName);
        }

        private static void AssertPlaybackFixedDeltaTimeMatches(InputPlayback playback, string recordingName)
        {
            Assert.That(playback.RecordedFixedDeltaTime, Is.EqualTo(Time.fixedDeltaTime).Within(FixedDeltaTimeTolerance),
                $"Recording '{recordingName}' was captured at fixedDeltaTime {playback.RecordedFixedDeltaTime} but the test is running at {Time.fixedDeltaTime}.");
        }

        private static void SetPrivateFloat(object target, string fieldName, float value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(float))
            {
                field.SetValue(target, value);
            }
        }

        private static void ConfigureFallRecorder(FallPoseRecorder recorder)
        {
            Assert.That(recorder, Is.Not.Null, "FallPoseRecorder must be attached before configuration.");

            SetPrivateField(recorder, "_enableDiagnostics", true);
            SetPrivateField(recorder, "_logToConsole", false);
            SetPrivateField(recorder, "_logToFile", true);
            SetPrivateField(recorder, "_clearLogOnStart", true);
            SetPrivateField(recorder, "_logFileName", FallPoseLogFileName);
            SetPrivateField(recorder, "_sampleEveryFixedTicks", 1);
            SetPrivateField(recorder, "_preTriggerSeconds", 1.5f);
            SetPrivateField(recorder, "_postTriggerSeconds", 2.0f);
            SetPrivateField(recorder, "_autoTriggerOnFallen", true);
            SetPrivateField(recorder, "_recordContinuousSamples", false);
            SetPrivateField(recorder, "_allowManualTrigger", false);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                field.SetValue(target, value);
            }
        }

        private static void LogBaseline(string scenario, string summary)
        {
            Debug.Log($"[C1.1 Baseline][MovementQuality] {scenario} {summary}");
        }

        private static bool[,] CaptureLayerCollisionMatrix()
        {
            bool[,] matrix = new bool[32, 32];
            for (int a = 0; a < 32; a++)
            {
                for (int b = 0; b < 32; b++)
                {
                    matrix[a, b] = Physics.GetIgnoreLayerCollision(a, b);
                }
            }

            return matrix;
        }

        private static void RestoreLayerCollisionMatrix(bool[,] matrix)
        {
            if (matrix == null || matrix.GetLength(0) != 32 || matrix.GetLength(1) != 32)
            {
                return;
            }

            for (int a = 0; a < 32; a++)
            {
                for (int b = 0; b < 32; b++)
                {
                    Physics.IgnoreLayerCollision(a, b, matrix[a, b]);
                }
            }
        }
    }
}
