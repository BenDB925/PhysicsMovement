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
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";

        private const int WalkStraightFrameBudget = 600;
        private const int CornerCourseFrameBudget = 1200;

        private const int MaxConsecutiveFallenStraight = 30;
        private const int MaxConsecutiveFallenCorner = 120;

        private GameObject _ground;
        private GameObject _player;
        private GameObject _courseRunnerGO;

        private CharacterState _characterState;
        private WaypointCourseRunner _courseRunner;

        private float _savedFixedDeltaTime;
        private int _savedSolverIterations;
        private int _savedSolverVelocityIterations;

        [SetUp]
        public void SetUp()
        {
            _savedFixedDeltaTime = Time.fixedDeltaTime;
            _savedSolverIterations = Physics.defaultSolverIterations;
            _savedSolverVelocityIterations = Physics.defaultSolverVelocityIterations;

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
                Object.Destroy(_courseRunnerGO);
            }

            if (_player != null)
            {
                Object.Destroy(_player);
            }

            if (_ground != null)
            {
                Object.Destroy(_ground);
            }

            Time.fixedDeltaTime = _savedFixedDeltaTime;
            Physics.defaultSolverIterations = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
        }

        [UnityTest]
        public IEnumerator WalkStraight_NoFalls()
        {
            yield return SpawnAndConfigureCourse(new[]
            {
                new Vector3(0f, 0f, 20f)
            });

            int maxConsecutiveFallen = 0;
            int consecutiveFallen = 0;

            for (int frame = 0; frame < WalkStraightFrameBudget && !_courseRunner.IsComplete; frame++)
            {
                yield return new WaitForFixedUpdate();
                TrackFallenFrames(ref consecutiveFallen, ref maxConsecutiveFallen);
            }

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
            yield return SpawnAndConfigureCourse(new[]
            {
                new Vector3(10f, 0f, 0f),
                new Vector3(10f, 0f, 10f),
                new Vector3(0f, 0f, 10f)
            });

            int maxConsecutiveFallen = 0;
            int consecutiveFallen = 0;

            for (int frame = 0; frame < CornerCourseFrameBudget && !_courseRunner.IsComplete; frame++)
            {
                yield return new WaitForFixedUpdate();
                TrackFallenFrames(ref consecutiveFallen, ref maxConsecutiveFallen);
            }

            Assert.That(_courseRunner.IsComplete, Is.True,
                $"Corner course did not finish within {CornerCourseFrameBudget} frames. " +
                $"Current waypoint index: {_courseRunner.CurrentWaypointIndex}.");

            Assert.That(_courseRunner.FramesElapsed, Is.LessThanOrEqualTo(CornerCourseFrameBudget),
                $"Corner course exceeded frame budget: {_courseRunner.FramesElapsed}/{CornerCourseFrameBudget}.");

            Assert.That(maxConsecutiveFallen, Is.LessThanOrEqualTo(MaxConsecutiveFallenCorner),
                $"Character stayed in Fallen for {maxConsecutiveFallen} consecutive frames " +
                $"(limit {MaxConsecutiveFallenCorner}).");
        }

        private IEnumerator SpawnAndConfigureCourse(Vector3[] waypoints)
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
            _player = Object.Instantiate(prefab, new Vector3(0f, 1f, 0f), Quaternion.identity);
            Assert.That(_player, Is.Not.Null, "Failed to instantiate PlayerRagdoll prefab.");

            PlayerMovement playerMovement = _player.GetComponentInChildren<PlayerMovement>();
            Assert.That(playerMovement, Is.Not.Null, "PlayerRagdoll prefab is missing PlayerMovement.");
            SetPrivateFloat(playerMovement, "_moveForce", 1500f);
            SetPrivateFloat(playerMovement, "_maxSpeed", 8f);

            _characterState = _player.GetComponentInChildren<CharacterState>();
            Assert.That(_characterState, Is.Not.Null, "PlayerRagdoll prefab is missing CharacterState.");

            // Warmup: 60 frames with zero input so physics settles before we start driving.
            // Without this the ragdoll is mid-fall when input starts and falls immediately.
            playerMovement.SetMoveInputForTest(Vector2.zero);
            for (int i = 0; i < 60; i++)
                yield return new WaitForFixedUpdate();

            _courseRunnerGO = new GameObject("WaypointCourseRunner");
            _courseRunner = _courseRunnerGO.AddComponent<WaypointCourseRunner>();
            _courseRunner.Initialize(_player, waypoints);
            Assert.That(_courseRunner.IsComplete, Is.False, "Course runner must start active with valid inputs.");
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

        private static void SetPrivateFloat(object target, string fieldName, float value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null && field.FieldType == typeof(float))
            {
                field.SetValue(target, value);
            }
        }
    }
}
