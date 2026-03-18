using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Outcome tests that measure planted-foot drift — how far a foot slides in the
    /// XZ plane while it should be stationary on the ground. Failing tests indicate
    /// that the body is moving faster than the leg cycle can anchor.
    /// </summary>
    public class FootSlidingTests
    {
        private const int SettleFrames = 100;
        private const int WalkFrames = 500;

        /// <summary>
        /// Maximum per-step planted foot drift at walk speed before the test fails.
        /// The plan's initial guess of 0.04m assumed IK-quality foot anchoring;
        /// the physics-driven character (no foot pinning) measures ~0.31m peak
        /// drift at walk speed. This threshold is a regression gate — not a quality
        /// target. WP3b will tighten it after the speed envelope is analyzed.
        /// </summary>
        private const float MaxPlantedFootDriftMetres = 0.35f;

        /// <summary>
        /// Frames to skip after entering Stance before starting drift measurement.
        /// This filters out the initial foot-settling motion as the plant finalizes.
        /// </summary>
        private const int StanceSettleFrames = 3;

        private static readonly FieldInfo LeftLegStateMachineField = typeof(LegAnimator)
            .GetField("_leftLegStateMachine", BindingFlags.NonPublic | BindingFlags.Instance);

        private static readonly FieldInfo RightLegStateMachineField = typeof(LegAnimator)
            .GetField("_rightLegStateMachine", BindingFlags.NonPublic | BindingFlags.Instance);

        private static PropertyInfo _legStateCurrentStateProperty;

        private PlayerPrefabTestRig _rig;
        private int _leftStanceAge;
        private int _rightStanceAge;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rig = PlayerPrefabTestRig.Create();
            _leftStanceAge = 0;
            _rightStanceAge = 0;
            yield return _rig.WarmUp(SettleFrames);
        }

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
        }

        [UnityTest]
        public IEnumerator WalkForward_PlantedFeetDoNotSlide()
        {
            GroundSensor leftSensor = FindGroundSensor(_rig.Instance, "Foot_L");
            GroundSensor rightSensor = FindGroundSensor(_rig.Instance, "Foot_R");
            Assert.That(leftSensor, Is.Not.Null, "Left GroundSensor not found.");
            Assert.That(rightSensor, Is.Not.Null, "Right GroundSensor not found.");

            object leftStateMachine = LeftLegStateMachineField?.GetValue(_rig.LegAnimator);
            object rightStateMachine = RightLegStateMachineField?.GetValue(_rig.LegAnimator);
            Assert.That(leftStateMachine, Is.Not.Null, "Left LegStateMachine not resolved.");
            Assert.That(rightStateMachine, Is.Not.Null, "Right LegStateMachine not resolved.");

            EnsureLegStatePropertyCached(leftStateMachine);

            PlantedFootDriftTracker tracker = new PlantedFootDriftTracker();

            // Walk forward (no sprint) for 5 seconds.
            _rig.PlayerMovement.SetMoveInputForTest(new Vector2(0f, 1f));

            for (int i = 0; i < WalkFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                bool leftInStance = IsFootInStance(leftStateMachine, leftSensor);
                bool rightInStance = IsFootInStance(rightStateMachine, rightSensor);

                // Track how long each foot has been continuously in stance.
                _leftStanceAge = leftInStance ? _leftStanceAge + 1 : 0;
                _rightStanceAge = rightInStance ? _rightStanceAge + 1 : 0;

                // Only count as planted after the settle window so we don't
                // measure the initial foot-settling motion.
                bool leftPlanted = _leftStanceAge > StanceSettleFrames;
                bool rightPlanted = _rightStanceAge > StanceSettleFrames;

                tracker.Sample(
                    _rig.FootL.position,
                    leftPlanted,
                    _rig.FootR.position,
                    rightPlanted);
            }

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);
            tracker.Flush();

            float planarSpeed = new Vector3(_rig.HipsBody.linearVelocity.x, 0f, _rig.HipsBody.linearVelocity.z).magnitude;

            Debug.Log(
                $"[METRIC] PlantedFootDrift_Walk " +
                $"MaxDrift={tracker.MaxDrift:F4} " +
                $"AverageDrift={tracker.AverageDrift:F4} " +
                $"StepCount={tracker.CompletedStepCount} " +
                $"MaxDriftL={tracker.MaxDriftLeft:F4} " +
                $"MaxDriftR={tracker.MaxDriftRight:F4} " +
                $"FinalSpeed={planarSpeed:F2}");

            Assert.That(tracker.CompletedStepCount, Is.GreaterThan(0),
                "No completed step cycles detected — planted-state signal may be broken.");

            Assert.That(tracker.MaxDrift, Is.LessThan(MaxPlantedFootDriftMetres),
                $"Planted foot drift {tracker.MaxDrift:F4}m exceeds threshold {MaxPlantedFootDriftMetres}m. " +
                $"The body is moving faster than the leg cycle can anchor. " +
                $"Steps={tracker.CompletedStepCount} AvgDrift={tracker.AverageDrift:F4}m");
        }

        private static bool IsFootInStance(object legStateMachine, GroundSensor sensor)
        {
            if (!sensor.IsGrounded) return false;

            // LegStateType: Stance=0 — the established support phase.
            // We exclude Plant (2) because the foot is still settling into position.
            object stateObj = _legStateCurrentStateProperty.GetValue(legStateMachine);
            int state = (int)stateObj;
            return state == 0; // Stance only
        }

        private static void EnsureLegStatePropertyCached(object legStateMachine)
        {
            if (_legStateCurrentStateProperty != null) return;
            _legStateCurrentStateProperty = legStateMachine.GetType()
                .GetProperty("CurrentState", BindingFlags.Public | BindingFlags.Instance);
            Assert.That(_legStateCurrentStateProperty, Is.Not.Null,
                "LegStateMachine.CurrentState property not found via reflection.");
        }

        private static GroundSensor FindGroundSensor(GameObject root, string footName)
        {
            GroundSensor[] sensors = root.GetComponentsInChildren<GroundSensor>(includeInactive: false);
            for (int i = 0; i < sensors.Length; i++)
            {
                if (sensors[i].transform.name == footName)
                    return sensors[i];
            }
            return null;
        }
    }
}
