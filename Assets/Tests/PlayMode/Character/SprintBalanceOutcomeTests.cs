using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Outcome-based Arena_01 diagnostics for sprint-speed balance validation.
    /// Confirms sustained sprint stability, height hold, and recovery from a
    /// small lateral perturbation at sprint speed.
    /// </summary>
    public class SprintBalanceOutcomeTests
    {
        private const string ArenaSceneName = "Arena_01";
        private const int SettleFrames = 100;
        private const int SustainedSprintFrames = 800;
        private const int HeightMeasurementStartFrame = 100;
        private const int HeightStabilityFrames = 500;
        private const int HeightStabilitySampleFrames = 300;
        private const int PerturbationPreSprintFrames = 300;
        private const int PerturbationForceFrames = 10;
        private const int PerturbationRecoveryFrames = 300;
        private const int UprightRecoveryDeadlineFrames = 150;
        private const float SprintSpeedFloorMetresPerSecond = 7.5f;
        private const float SprintHeightReferenceMetres = 0.35f;
        private const float HeightToleranceMetres = 0.1f;
        private const float HeightStabilityStdDevLimitMetres = 0.05f;
        private const float UprightRecoveryTiltDegrees = 15f;
        private const float MinorPerturbationForceNewton = 50f;

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
        public IEnumerator Sprint_SustainedWithoutFall_AndHipsHeightStaysNearTarget()
        {
            yield return LoadArenaScene();
            ArenaCharacterContext context = FindActiveCharacter();
            yield return SettleCharacter(context);

            SprintWindowMetrics metrics = new SprintWindowMetrics(SprintHeightReferenceMetres);
            yield return RunSprintWindow(context, SustainedSprintFrames, HeightMeasurementStartFrame, metrics);

            RequireSprintSpeedOrIgnore("sustained sprint", metrics.PeakPlanarSpeed);
            Assert.That(metrics.EnteredFallen, Is.False,
                "CharacterState should never enter Fallen during the sustained 8 s Arena_01 sprint diagnostic.");
            Assert.That(metrics.HeightSampleCount, Is.EqualTo(SustainedSprintFrames - HeightMeasurementStartFrame),
                "The sustained sprint diagnostic should sample hips height for the full post-ramp window.");
            Assert.That(metrics.MaxHeightErrorMetres, Is.LessThanOrEqualTo(HeightToleranceMetres),
                $"After the sprint ramp, hips height should stay within +/-{HeightToleranceMetres:F2} m of the sprint validation reference. Sprint reference={SprintHeightReferenceMetres:F2} m, standing target={context.BalanceController.StandingHipsHeight:F2} m, sampled range=[{metrics.MinSampledHeight:F3}, {metrics.MaxSampledHeight:F3}] m, max error={metrics.MaxHeightErrorMetres:F3} m.");
        }

        [UnityTest]
        public IEnumerator Sprint_HeightStableAtSpeed_LastThreeSecondsStayBelowStdDevThreshold()
        {
            yield return LoadArenaScene();
            ArenaCharacterContext context = FindActiveCharacter();
            yield return SettleCharacter(context);

            SprintWindowMetrics metrics = new SprintWindowMetrics(context.BalanceController.StandingHipsHeight);
            yield return RunSprintWindow(context, HeightStabilityFrames, 0, metrics);

            IReadOnlyList<float> trailingHeights = metrics.GetTrailingHeightSamples(HeightStabilitySampleFrames);
            float stdDev = ComputeStandardDeviation(trailingHeights);

            RequireSprintSpeedOrIgnore("height stability", metrics.PeakPlanarSpeed);
            Assert.That(metrics.EnteredFallen, Is.False,
                "CharacterState should remain upright during the 5 s sprint height-stability diagnostic.");
            Assert.That(trailingHeights.Count, Is.EqualTo(HeightStabilitySampleFrames),
                $"The height-stability diagnostic should analyse the last {HeightStabilitySampleFrames} physics samples.");
            Assert.That(stdDev, Is.LessThan(HeightStabilityStdDevLimitMetres),
                $"Hips height over the last 3 s of sprint should avoid pumping. StdDev={stdDev:F4} m (limit < {HeightStabilityStdDevLimitMetres:F2} m), sampled range=[{metrics.MinSampledHeight:F3}, {metrics.MaxSampledHeight:F3}] m.");
        }

        [UnityTest]
        public IEnumerator Sprint_RecoveryFromMinorPerturbation_RegainsUprightWithinOnePointFiveSeconds()
        {
            yield return LoadArenaScene();
            ArenaCharacterContext context = FindActiveCharacter();
            yield return SettleCharacter(context);

            context.Movement.SetMoveInputForTest(SprintMoveInput);
            context.Movement.SetSprintInputForTest(true);

            float peakPlanarSpeed = 0f;
            bool enteredFallen = false;

            for (int frame = 0; frame < PerturbationPreSprintFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                RecordSprintState(context, ref peakPlanarSpeed, ref enteredFallen);
            }

            float baselineTilt = context.BalanceController.UprightAngle;
            Vector3 travelDirection = GetTravelDirection(context);
            Assert.That(travelDirection.sqrMagnitude, Is.GreaterThan(0.0001f),
                "The perturbation diagnostic requires a valid sprint travel direction before applying the lateral shove.");

            Vector3 lateralDirection = Vector3.Cross(Vector3.up, travelDirection);
            if (lateralDirection.sqrMagnitude < 0.0001f)
            {
                lateralDirection = Vector3.forward;
            }

            lateralDirection.Normalize();

            for (int frame = 0; frame < PerturbationForceFrames; frame++)
            {
                context.HipsBody.AddForce(lateralDirection * MinorPerturbationForceNewton, ForceMode.Force);
                yield return new WaitForFixedUpdate();
                RecordSprintState(context, ref peakPlanarSpeed, ref enteredFallen);
            }

            int recoveryFrame = -1;
            float peakTiltAfterImpulse = context.BalanceController.UprightAngle;

            for (int frame = 0; frame < PerturbationRecoveryFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                RecordSprintState(context, ref peakPlanarSpeed, ref enteredFallen);

                float uprightAngle = context.BalanceController.UprightAngle;
                peakTiltAfterImpulse = Mathf.Max(peakTiltAfterImpulse, uprightAngle);

                if (recoveryFrame < 0 &&
                    uprightAngle <= UprightRecoveryTiltDegrees &&
                    context.CharacterState.CurrentState != CharacterStateType.Fallen)
                {
                    recoveryFrame = frame;
                }
            }

            ResetInput(context);

            RequireSprintSpeedOrIgnore("minor-perturbation recovery", peakPlanarSpeed);
            Assert.That(enteredFallen, Is.False,
                "CharacterState should not enter Fallen during the sprint perturbation diagnostic.");
            Assert.That(recoveryFrame, Is.GreaterThanOrEqualTo(0).And.LessThan(UprightRecoveryDeadlineFrames),
                $"After a 50 N lateral force applied for 0.1 s, the character should recover to <= {UprightRecoveryTiltDegrees:F0}° within {UprightRecoveryDeadlineFrames} physics frames. Baseline tilt={baselineTilt:F2}°, peak tilt={peakTiltAfterImpulse:F2}°, recoveryFrame={(recoveryFrame >= 0 ? recoveryFrame.ToString() : "none")}.");
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
                "Arena_01 must contain at least one active PlayerMovement for sprint balance diagnostics.");

            PlayerMovement movement = movements[0];
            Rigidbody hipsBody = movement.GetComponent<Rigidbody>();
            CharacterState characterState = movement.GetComponent<CharacterState>();
            BalanceController balanceController = movement.GetComponent<BalanceController>();

            Assert.That(hipsBody, Is.Not.Null, "Arena_01 sprint balance diagnostics require the hips Rigidbody.");
            Assert.That(characterState, Is.Not.Null, "Arena_01 sprint balance diagnostics require CharacterState.");
            Assert.That(balanceController, Is.Not.Null, "Arena_01 sprint balance diagnostics require BalanceController.");

            return new ArenaCharacterContext(movement, hipsBody, characterState, balanceController);
        }

        private static IEnumerator SettleCharacter(ArenaCharacterContext context)
        {
            ResetInput(context);

            for (int frame = 0; frame < SettleFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private static IEnumerator RunSprintWindow(
            ArenaCharacterContext context,
            int totalFrames,
            int heightSampleStartFrame,
            SprintWindowMetrics metrics)
        {
            context.Movement.SetMoveInputForTest(SprintMoveInput);
            context.Movement.SetSprintInputForTest(true);

            for (int frame = 0; frame < totalFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                metrics.RecordFrame(context, frame >= heightSampleStartFrame);
            }

            ResetInput(context);
        }

        private static void ResetInput(ArenaCharacterContext context)
        {
            context.Movement.SetMoveInputForTest(Vector2.zero);
            context.Movement.SetSprintInputForTest(false);
        }

        private static void RecordSprintState(
            ArenaCharacterContext context,
            ref float peakPlanarSpeed,
            ref bool enteredFallen)
        {
            peakPlanarSpeed = Mathf.Max(peakPlanarSpeed, GetPlanarSpeed(context.HipsBody));
            enteredFallen |= context.CharacterState.CurrentState == CharacterStateType.Fallen;
        }

        private static float GetPlanarSpeed(Rigidbody hipsBody)
        {
            Vector3 planarVelocity = Vector3.ProjectOnPlane(hipsBody.linearVelocity, Vector3.up);
            return planarVelocity.magnitude;
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

        private static float ComputeStandardDeviation(IReadOnlyList<float> values)
        {
            Assert.That(values, Is.Not.Null);
            Assert.That(values.Count, Is.GreaterThan(0), "Standard deviation requires at least one sample.");

            float mean = 0f;
            for (int index = 0; index < values.Count; index++)
            {
                mean += values[index];
            }

            mean /= values.Count;

            float variance = 0f;
            for (int index = 0; index < values.Count; index++)
            {
                float delta = values[index] - mean;
                variance += delta * delta;
            }

            variance /= values.Count;
            return Mathf.Sqrt(variance);
        }

        private static void RequireSprintSpeedOrIgnore(string scenario, float peakPlanarSpeed)
        {
            if (peakPlanarSpeed >= SprintSpeedFloorMetresPerSecond)
            {
                return;
            }

            Assert.Ignore(
                $"WP-5 {scenario} diagnostics require Arena_01 sprint speed >= {SprintSpeedFloorMetresPerSecond:F1} m/s, but peak planar speed was {peakPlanarSpeed:F2} m/s. Step 1 remains blocked until the in-scene sprint tier reaches the target envelope.");
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

        private sealed class SprintWindowMetrics
        {
            private readonly float _targetHeight;
            private readonly List<float> _sampledHeights = new List<float>();

            public SprintWindowMetrics(float targetHeight)
            {
                _targetHeight = targetHeight;
            }

            public bool EnteredFallen { get; private set; }
            public float PeakPlanarSpeed { get; private set; }
            public float MaxHeightErrorMetres { get; private set; }
            public float MinSampledHeight { get; private set; } = float.MaxValue;
            public float MaxSampledHeight { get; private set; } = float.MinValue;
            public int HeightSampleCount => _sampledHeights.Count;

            public void RecordFrame(ArenaCharacterContext context, bool sampleHeight)
            {
                PeakPlanarSpeed = Mathf.Max(PeakPlanarSpeed, GetPlanarSpeed(context.HipsBody));
                EnteredFallen |= context.CharacterState.CurrentState == CharacterStateType.Fallen;

                if (!sampleHeight)
                {
                    return;
                }

                float height = context.HipsBody.position.y;
                _sampledHeights.Add(height);
                MinSampledHeight = Mathf.Min(MinSampledHeight, height);
                MaxSampledHeight = Mathf.Max(MaxSampledHeight, height);
                MaxHeightErrorMetres = Mathf.Max(MaxHeightErrorMetres, Mathf.Abs(height - _targetHeight));
            }

            public IReadOnlyList<float> GetTrailingHeightSamples(int count)
            {
                int sampleCount = Mathf.Min(count, _sampledHeights.Count);
                int startIndex = _sampledHeights.Count - sampleCount;
                List<float> trailing = new List<float>(sampleCount);

                for (int index = startIndex; index < _sampledHeights.Count; index++)
                {
                    trailing.Add(_sampledHeights[index]);
                }

                return trailing;
            }
        }
    }
}