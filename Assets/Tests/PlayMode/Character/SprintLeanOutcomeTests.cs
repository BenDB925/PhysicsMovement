using System.Collections;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Outcome-based sprint-lean coverage against the production Arena_01 scene.
    /// Verifies the visible sprint lean increase, release back to walk posture, and
    /// the Fallen-state safety margin for the sprint posture layer.
    /// </summary>
    public class SprintLeanOutcomeTests
    {
        private const string ArenaSceneName = "Arena_01";
        private const int SettleFrames = 100;
        private const int WalkFrames = 300;
        private const int SprintFrames = 300;
        private const int PostSprintWalkFrames = 200;
        private const int FinalWalkSampleFrames = 100;
        private const int SafetySprintFrames = 500;

        private static readonly Vector2 SprintMoveInput = Vector2.right;

        private float _originalFixedDeltaTime;
        private int _originalSolverIterations;
        private int _originalSolverVelocityIterations;
        private bool[,] _originalLayerCollisionMatrix;

        [SetUp]
        public void SetUp()
        {
            _originalFixedDeltaTime = Time.fixedDeltaTime;
            _originalSolverIterations = Physics.defaultSolverIterations;
            _originalSolverVelocityIterations = Physics.defaultSolverVelocityIterations;
            _originalLayerCollisionMatrix = CaptureLayerCollisionMatrix();

            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
        }

        [TearDown]
        public void TearDown()
        {
            Time.fixedDeltaTime = _originalFixedDeltaTime;
            Physics.defaultSolverIterations = _originalSolverIterations;
            Physics.defaultSolverVelocityIterations = _originalSolverVelocityIterations;
            RestoreLayerCollisionMatrix(_originalLayerCollisionMatrix);
        }

        [UnityTest]
        public IEnumerator Sprint_ForwardLeanIncrease()
        {
            // Arrange
            yield return LoadArenaScene();
            ArenaCharacterContext walkContext = FindActiveCharacter();
            yield return SettleCharacter(walkContext);

            MovementWindowMetrics walkMetrics = new MovementWindowMetrics();

            // Act
            yield return RunMovementWindow(
                walkContext,
                SprintMoveInput,
                sprintHeld: false,
                totalFrames: WalkFrames,
                sampleStartFrame: 0,
                walkMetrics);

            yield return LoadArenaScene();
            ArenaCharacterContext sprintContext = FindActiveCharacter();
            yield return SettleCharacter(sprintContext);

            MovementWindowMetrics sprintMetrics = new MovementWindowMetrics();
            yield return RunMovementWindow(
                sprintContext,
                SprintMoveInput,
                sprintHeld: true,
                totalFrames: SprintFrames,
                sampleStartFrame: 0,
                sprintMetrics);

            // Assert
            Assert.That(walkMetrics.SampleCount, Is.GreaterThan(0),
                "The walk regression guard should record forward-tilt samples over the Arena_01 walk window.");
            Assert.That(sprintMetrics.SampleCount, Is.GreaterThan(0),
                "The sprint outcome slice should record forward-tilt samples over the Arena_01 sprint window.");
            Assert.That(walkMetrics.EnteredFallen, Is.False,
                "The walk regression guard should remain in locomotion rather than measuring a fallen posture.");
            Assert.That(sprintMetrics.EnteredFallen, Is.False,
                "The sprint lean outcome should measure an active sprint posture, not a fallen posture.");
            Assert.That(sprintMetrics.AverageTiltDegrees, Is.GreaterThanOrEqualTo(5f),
                $"Average forward tilt during the 3 s sprint window should show a visible sprint lean. Observed={sprintMetrics.AverageTiltDegrees:F2}°.");
            Assert.That(walkMetrics.AverageTiltDegrees, Is.LessThan(5f),
                $"Average forward tilt during the matching 3 s walk window should stay below the sprint-lean threshold. Observed={walkMetrics.AverageTiltDegrees:F2}°.");
        }

        [UnityTest]
        public IEnumerator SprintEnd_LeanRecovers()
        {
            // Arrange
            yield return LoadArenaScene();
            ArenaCharacterContext context = FindActiveCharacter();
            yield return SettleCharacter(context);

            MovementWindowMetrics sprintMetrics = new MovementWindowMetrics();
            MovementWindowMetrics recoveryWalkMetrics = new MovementWindowMetrics();

            // Act
            yield return RunMovementWindow(
                context,
                SprintMoveInput,
                sprintHeld: true,
                totalFrames: SprintFrames,
                sampleStartFrame: 0,
                sprintMetrics);

            yield return RunMovementWindow(
                context,
                SprintMoveInput,
                sprintHeld: false,
                totalFrames: PostSprintWalkFrames,
                sampleStartFrame: PostSprintWalkFrames - FinalWalkSampleFrames,
                recoveryWalkMetrics);

            // Assert
            Assert.That(sprintMetrics.EnteredFallen, Is.False,
                "The precondition sprint window should stay upright before measuring the post-sprint walk recovery.");
            Assert.That(recoveryWalkMetrics.SampleCount, Is.EqualTo(FinalWalkSampleFrames),
                "The lean recovery assertion should sample the final 1 s walk window exactly.");
            Assert.That(recoveryWalkMetrics.EnteredFallen, Is.False,
                "The walk recovery window should not enter Fallen while sprint lean decays back to the walk posture.");
            Assert.That(recoveryWalkMetrics.AverageTiltDegrees, Is.LessThan(4f),
                $"Average forward tilt during the final 1 s of post-sprint walking should return near the walk posture. Observed={recoveryWalkMetrics.AverageTiltDegrees:F2}°.");
        }

        [UnityTest]
        public IEnumerator Sprint_DoesNotTriggerFallen()
        {
            // Arrange
            yield return LoadArenaScene();
            ArenaCharacterContext context = FindActiveCharacter();
            yield return SettleCharacter(context);

            MovementWindowMetrics sprintMetrics = new MovementWindowMetrics();

            // Act
            yield return RunMovementWindow(
                context,
                SprintMoveInput,
                sprintHeld: true,
                totalFrames: SafetySprintFrames,
                sampleStartFrame: 0,
                sprintMetrics);

            // Assert
            Assert.That(sprintMetrics.EnteredFallen, Is.False,
                "CharacterState should never enter Fallen during the 5 s flat-ground sprint safety window.");
        }

        private static IEnumerator LoadArenaScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(ArenaSceneName, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, $"Failed to start async load for scene '{ArenaSceneName}'.");

            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;
            yield return new WaitForFixedUpdate();
        }

        private static ArenaCharacterContext FindActiveCharacter()
        {
            PlayerMovement[] movements = Object.FindObjectsByType<PlayerMovement>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            Assert.That(movements.Length, Is.GreaterThan(0),
                "Arena_01 must contain at least one active PlayerMovement for the sprint lean outcome tests.");

            PlayerMovement movement = movements[0];
            Rigidbody hipsBody = movement.GetComponent<Rigidbody>();
            CharacterState characterState = movement.GetComponent<CharacterState>();
            BalanceController balanceController = movement.GetComponent<BalanceController>();

            Assert.That(hipsBody, Is.Not.Null, "Arena_01 sprint lean coverage requires the hips Rigidbody.");
            Assert.That(characterState, Is.Not.Null, "Arena_01 sprint lean coverage requires CharacterState.");
            Assert.That(balanceController, Is.Not.Null, "Arena_01 sprint lean coverage requires BalanceController.");

            return new ArenaCharacterContext(movement, hipsBody, characterState, balanceController);
        }

        private static IEnumerator SettleCharacter(ArenaCharacterContext context)
        {
            context.Movement.SetMoveInputForTest(Vector2.zero);
            context.Movement.SetSprintInputForTest(false);

            for (int frame = 0; frame < SettleFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private static IEnumerator RunMovementWindow(
            ArenaCharacterContext context,
            Vector2 moveInput,
            bool sprintHeld,
            int totalFrames,
            int sampleStartFrame,
            MovementWindowMetrics metrics)
        {
            // STEP 1: Hold the requested move and sprint test input for the full window.
            context.Movement.SetMoveInputForTest(moveInput);
            context.Movement.SetSprintInputForTest(sprintHeld);

            // STEP 2: Sample forward tilt against the current travel plane each physics tick.
            for (int frame = 0; frame < totalFrames; frame++)
            {
                yield return new WaitForFixedUpdate();

                metrics.RecordState(context.CharacterState.CurrentState);

                if (frame < sampleStartFrame)
                {
                    continue;
                }

                Vector3 travelDirection = GetTravelDirection(context);
                if (travelDirection.sqrMagnitude < 0.0001f)
                {
                    continue;
                }

                metrics.AddTiltSample(GetForwardTiltAngle(context.HipsBody, travelDirection));
            }

            // STEP 3: Reset test input so the next movement window starts from a clean intent state.
            context.Movement.SetMoveInputForTest(Vector2.zero);
            context.Movement.SetSprintInputForTest(false);
        }

        private static Vector3 GetTravelDirection(ArenaCharacterContext context)
        {
            Vector3 facingDirection = Vector3.ProjectOnPlane(context.Movement.CurrentFacingDirection, Vector3.up);
            if (facingDirection.sqrMagnitude > 0.0001f)
            {
                return facingDirection.normalized;
            }

            Vector3 moveWorldDirection = Vector3.ProjectOnPlane(context.Movement.CurrentMoveWorldDirection, Vector3.up);
            if (moveWorldDirection.sqrMagnitude > 0.0001f)
            {
                return moveWorldDirection.normalized;
            }

            Vector3 velocityDirection = Vector3.ProjectOnPlane(context.HipsBody.linearVelocity, Vector3.up);
            if (velocityDirection.sqrMagnitude > 0.0001f)
            {
                return velocityDirection.normalized;
            }

            return Vector3.zero;
        }

        private static float GetForwardTiltAngle(Rigidbody hipsBody, Vector3 travelDirection)
        {
            Vector3 travelXZ = Vector3.ProjectOnPlane(travelDirection, Vector3.up);
            if (travelXZ.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            Vector3 planeNormal = Vector3.Cross(travelXZ.normalized, Vector3.up);
            Vector3 projectedWorldUp = Vector3.ProjectOnPlane(Vector3.up, planeNormal);
            Vector3 projectedHipsUp = Vector3.ProjectOnPlane(hipsBody.transform.up, planeNormal);
            if (projectedWorldUp.sqrMagnitude < 0.0001f || projectedHipsUp.sqrMagnitude < 0.0001f)
            {
                return 0f;
            }

            return Vector3.Angle(projectedWorldUp.normalized, projectedHipsUp.normalized);
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

        private readonly struct ArenaCharacterContext
        {
            public ArenaCharacterContext(
                PlayerMovement movement,
                Rigidbody hipsBody,
                CharacterState characterState,
                BalanceController balanceController)
            {
                Movement = movement;
                HipsBody = hipsBody;
                CharacterState = characterState;
                BalanceController = balanceController;
            }

            public PlayerMovement Movement { get; }
            public Rigidbody HipsBody { get; }
            public CharacterState CharacterState { get; }
            public BalanceController BalanceController { get; }
        }

        private sealed class MovementWindowMetrics
        {
            private float _tiltSumDegrees;

            public int SampleCount { get; private set; }
            public bool EnteredFallen { get; private set; }
            public float AverageTiltDegrees => SampleCount > 0 ? _tiltSumDegrees / SampleCount : 0f;

            public void AddTiltSample(float tiltDegrees)
            {
                _tiltSumDegrees += tiltDegrees;
                SampleCount++;
            }

            public void RecordState(CharacterStateType state)
            {
                EnteredFallen |= state == CharacterStateType.Fallen;
            }
        }
    }
}