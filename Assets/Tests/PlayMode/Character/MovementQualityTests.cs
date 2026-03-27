using System;
using System.Collections;
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

        private const int WalkStraightFrameBudget = 600;
        private const int CornerCourseFrameBudget = 1200;
        private const int CollapseEvidenceFrameBudget = 90;

        private const int MaxConsecutiveFallenStraight = 30;
        private const int MaxConsecutiveFallenCorner = 160;
        private const float StraightCourseDistance = 20f;

        private static readonly Vector3[] StraightCourseWaypoints = BuildStraightCourseWaypoints();
        private static readonly Vector3[] CornerCourseWaypoints = BuildCornerCourseWaypoints();

        private GameObject _ground;
        private GameObject _player;
        private GameObject _courseRunnerGO;

        private CharacterState _characterState;
        private WaypointCourseRunner _courseRunner;

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
            if (_courseRunnerGO != null)
            {
                UnityEngine.Object.Destroy(_courseRunnerGO);
            }

            if (_player != null)
            {
                UnityEngine.Object.Destroy(_player);
            }

            if (_ground != null)
            {
                UnityEngine.Object.Destroy(_ground);
            }

            RestoreLayerCollisionMatrix(_savedLayerCollisionMatrix);

            Time.fixedDeltaTime = _savedFixedDeltaTime;
            Physics.defaultSolverIterations = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
        }

        [UnityTest]
        public IEnumerator WalkStraight_NoFalls()
        {
            yield return SpawnAndConfigureCourse(StraightCourseWaypoints);

            int maxConsecutiveFallen = 0;
            int consecutiveFallen = 0;

            for (int frame = 0; frame < WalkStraightFrameBudget && !_courseRunner.IsComplete; frame++)
            {
                yield return new WaitForFixedUpdate();
                TrackFallenFrames(ref consecutiveFallen, ref maxConsecutiveFallen);
            }

            LogBaseline(
                nameof(WalkStraight_NoFalls),
                $"completed={_courseRunner.IsComplete} framesElapsed={_courseRunner.FramesElapsed} maxConsecutiveFallen={maxConsecutiveFallen}");

            Assert.That(_courseRunner.IsComplete, Is.True,
                $"Straight course did not finish within {WalkStraightFrameBudget} frames. " +
                $"Reached waypoint index {_courseRunner.CurrentWaypointIndex}.");

            Assert.That(_courseRunner.FramesElapsed, Is.LessThanOrEqualTo(WalkStraightFrameBudget),
                $"Straight course exceeded frame budget: {_courseRunner.FramesElapsed}/{WalkStraightFrameBudget}.");

            Assert.That(maxConsecutiveFallen, Is.LessThanOrEqualTo(MaxConsecutiveFallenStraight),
                $"Character stayed in Fallen for {maxConsecutiveFallen} consecutive frames " +
                $"(limit {MaxConsecutiveFallenStraight}).");
        }

        [UnityTest]
        public IEnumerator TurnAndWalk_CornerRecovery()
        {
            yield return SpawnAndConfigureCourse(CornerCourseWaypoints);

            int maxConsecutiveFallen = 0;
            int consecutiveFallen = 0;

            for (int frame = 0; frame < CornerCourseFrameBudget && !_courseRunner.IsComplete; frame++)
            {
                yield return new WaitForFixedUpdate();
                TrackFallenFrames(ref consecutiveFallen, ref maxConsecutiveFallen);
            }

            LogBaseline(
                nameof(TurnAndWalk_CornerRecovery),
                $"completed={_courseRunner.IsComplete} framesElapsed={_courseRunner.FramesElapsed} maxConsecutiveFallen={maxConsecutiveFallen}");

            Assert.That(_courseRunner.IsComplete, Is.True,
                $"Corner course did not finish within {CornerCourseFrameBudget} frames. " +
                $"Current waypoint index: {_courseRunner.CurrentWaypointIndex}.");

            Assert.That(_courseRunner.FramesElapsed, Is.LessThanOrEqualTo(CornerCourseFrameBudget),
                $"Corner course exceeded frame budget: {_courseRunner.FramesElapsed}/{CornerCourseFrameBudget}.");

            Assert.That(maxConsecutiveFallen, Is.LessThanOrEqualTo(MaxConsecutiveFallenCorner),
                $"Character stayed in Fallen for {maxConsecutiveFallen} consecutive frames " +
                $"(limit {MaxConsecutiveFallenCorner}).");
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

        private IEnumerator SpawnAndConfigureCourse(Vector3[] waypoints)
        {
            yield return SpawnAndConfigurePlayer();

            _courseRunnerGO = new GameObject("WaypointCourseRunner");
            _courseRunner = _courseRunnerGO.AddComponent<WaypointCourseRunner>();
            _courseRunner.Initialize(_player, waypoints);
            Assert.That(_courseRunner.IsComplete, Is.False, "Course runner must start active with valid inputs.");
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

        private static Vector3[] BuildStraightCourseWaypoints()
        {
            Vector3[] startStopDirections = ScenarioPathUtility.GetTravelDirections(ScenarioDefinitions.StartStop);
            return new[]
            {
                startStopDirections[0] * StraightCourseDistance,
            };
        }

        private static Vector3[] BuildCornerCourseWaypoints()
        {
            Vector3[] hardTurnWaypoints = ScenarioDefinitions.HardTurn90.Waypoints;
            if (hardTurnWaypoints == null || hardTurnWaypoints.Length < 2)
            {
                throw new InvalidOperationException("ScenarioDefinitions.HardTurn90 must expose at least two waypoints.");
            }

            Vector3 firstWaypoint = SwapXZ(hardTurnWaypoints[0]);
            Vector3 secondWaypoint = SwapXZ(hardTurnWaypoints[1]);
            return new[]
            {
                firstWaypoint,
                secondWaypoint,
                secondWaypoint - firstWaypoint,
            };
        }

        private void TrackFallenFrames(ref int consecutiveFallen, ref int maxConsecutiveFallen)
        {
            if (_characterState.CurrentState == CharacterStateType.Fallen)
            {
                consecutiveFallen++;
                maxConsecutiveFallen = Mathf.Max(maxConsecutiveFallen, consecutiveFallen);
            }
            else
            {
                consecutiveFallen = 0;
            }
        }

        private static Vector3 SwapXZ(Vector3 waypoint)
        {
            return new Vector3(waypoint.z, waypoint.y, waypoint.x);
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
