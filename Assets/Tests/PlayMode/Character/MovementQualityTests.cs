using System.Collections;
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
            SpawnAndConfigureCourse(new[]
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
            SpawnAndConfigureCourse(new[]
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

        private void SpawnAndConfigureCourse(Vector3[] waypoints)
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            _ground.transform.localScale = new Vector3(10f, 1f, 10f);
            _ground.layer = GameSettings.LayerEnvironment;

#if UNITY_EDITOR
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
#else
            GameObject prefab = null;
#endif
            Assert.That(prefab, Is.Not.Null,
                $"PlayerRagdoll prefab was not found at '{PlayerRagdollPrefabPath}'.");

            _player = Object.Instantiate(prefab, Vector3.zero, Quaternion.identity);
            Assert.That(_player, Is.Not.Null, "Failed to instantiate PlayerRagdoll prefab.");

            PlayerMovement playerMovement = _player.GetComponent<PlayerMovement>();
            Assert.That(playerMovement, Is.Not.Null, "PlayerRagdoll prefab is missing PlayerMovement.");

            _characterState = _player.GetComponent<CharacterState>();
            Assert.That(_characterState, Is.Not.Null, "PlayerRagdoll prefab is missing CharacterState.");

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
    }
}
