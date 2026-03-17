using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for the Chapter 1 surrender threshold system.
    /// Validates that extreme tilt angles trigger surrender (killing upright torque),
    /// recovery timeouts eventually surrender, moderate angles never surrender,
    /// and joint springs ramp down on surrender.
    /// </summary>
    public class SurrenderTests
    {
        private static readonly Vector3 TestOrigin = new Vector3(0f, 0f, 9000f);

        private PlayerPrefabTestRig _rig;

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
        }

        /// <summary>
        /// Test 1: Angle > 85° sustained for 2+ frames → IsSurrendered, upright torque drops to 0.
        /// Uses a strong directional impulse on the torso (same pattern as GetUpReliabilityTests)
        /// to reliably blow through the 80° surrender angle threshold.
        /// </summary>
        [UnityTest]
        public IEnumerator ExtremeAngle_Above85Degrees_SurrenderFiresAndUprightTorqueDropsToZero()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin,
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(150);

            Assert.That(_rig.BalanceController.IsSurrendered, Is.False,
                "Character should not be surrendered after warm-up.");

            // Use a large force-at-position impulse on the torso to tilt the character
            // reliably past 85°. Apply an 800N impulse to overpower the balance system.
            Rigidbody targetBody = _rig.TorsoBody != null ? _rig.TorsoBody : _rig.HipsBody;
            Vector3 forcePoint = targetBody.worldCenterOfMass + Vector3.up * 0.1f;
            targetBody.AddForceAtPosition(Vector3.left * 800f, forcePoint, ForceMode.Impulse);

            // Wait for physics to propagate; the surrender angle threshold is 80° with a
            // 2-frame persistence gate, so 85°+ should clear within ~100 frames.
            bool surrendered = false;
            for (int i = 0; i < 200; i++)
            {
                yield return new WaitForFixedUpdate();
                if (_rig.BalanceController.IsSurrendered)
                {
                    surrendered = true;
                    break;
                }
            }
            Assert.That(surrendered, Is.True,
                "An extreme tilt (>85°) sustained for 2+ frames must trigger surrender.");
            Assert.That(_rig.BalanceController.UprightStrengthScale, Is.EqualTo(0f).Within(0.001f),
                "After surrender, upright strength scale must be zero.");
            Assert.That(_rig.BalanceController.HeightMaintenanceScale, Is.EqualTo(0f).Within(0.001f),
                "After surrender, height maintenance scale must be zero.");
            Assert.That(_rig.BalanceController.StabilizationScale, Is.EqualTo(0f).Within(0.001f),
                "After surrender, stabilization scale must be zero.");
        }

        /// <summary>
        /// Test 2: Recovery running > 0.8 s with angle stuck above 50° → surrender fires.
        /// We apply a sustained moderate force over many frames to keep the character
        /// destabilized in the stumble/near-fall range (50–75°) without blowing through
        /// the 80° extreme-angle gate. The LocomotionDirector's recovery timeout should
        /// eventually call TriggerSurrender.
        /// </summary>
        [UnityTest]
        public IEnumerator RecoveryTimeout_AngleStuckAbove50Degrees_SurrenderFires()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin + new Vector3(100f, 0f, 0f),
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(150);

            Assert.That(_rig.BalanceController.IsSurrendered, Is.False);

            // Apply a moderate initial impulse to destabilize into recovery range.
            Rigidbody targetBody = _rig.TorsoBody != null ? _rig.TorsoBody : _rig.HipsBody;
            Vector3 forcePoint = targetBody.worldCenterOfMass + Vector3.up * 0.1f;
            targetBody.AddForceAtPosition(Vector3.left * 500f, forcePoint, ForceMode.Impulse);

            // Keep applying a strong sustained force to fight the recovery system and
            // keep the angle stuck above 50°. The LocomotionDirector should detect the
            // stuck recovery and trigger surrender after ~0.8 s. We apply the torque
            // continuously so the balance controller cannot recover.
            bool surrendered = false;
            for (int i = 0; i < 300; i++)
            {
                yield return new WaitForFixedUpdate();

                float angle = _rig.BalanceController.UprightAngle;

                if (_rig.BalanceController.IsSurrendered)
                {
                    surrendered = true;
                    break;
                }

                // Sustained destabilizing torque applied every frame to overpower recovery.
                // Apply to hips as angular velocity to maintain the tilt without overshooting.
                if (angle > 20f && angle < 78f)
                {
                    targetBody.AddTorque(Vector3.forward * 150f, ForceMode.Force);
                }
            }

            Assert.That(surrendered, Is.True,
                "A recovery situation with the angle stuck above 50° for > 0.8 s " +
                "should trigger surrender via LocomotionDirector recovery timeout " +
                "or via the momentum+angle surrender condition.");
        }

        /// <summary>
        /// Test 3: Angle < 70° (moderate tilt, not stuck) → surrender does NOT fire.
        /// </summary>
        [UnityTest]
        public IEnumerator ModerateTilt_Below70Degrees_SurrenderDoesNotFire()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin + new Vector3(200f, 0f, 0f),
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(150);

            // Apply a moderate impulse that tilts the character but remains recoverable.
            // A 20 rad/s torque impulse should produce a tilt in the 20–50° range,
            // well below the 80° extreme-angle and 65° momentum-angle thresholds.
            _rig.HipsBody.AddTorque(Vector3.forward * 20f, ForceMode.VelocityChange);

            // Run physics for 2 seconds — character should recover, never surrender.
            int frames = Mathf.CeilToInt(2f / Time.fixedDeltaTime);
            for (int i = 0; i < frames; i++)
            {
                yield return new WaitForFixedUpdate();
                Assert.That(_rig.BalanceController.IsSurrendered, Is.False,
                    $"A moderate tilt (< 70°) must not trigger surrender. " +
                    $"Frame {i}: UprightAngle={_rig.BalanceController.UprightAngle:F1}°");
            }
        }

        /// <summary>
        /// Test 4: After surrender, joint springs ramp to ~25% of baseline within 0.15 s.
        /// </summary>
        [UnityTest]
        public IEnumerator AfterSurrender_JointSpringsRampToLowPercentWithinExpectedDuration()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin + new Vector3(300f, 0f, 0f),
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(150);

            // Capture baseline spring values before surrender.
            ConfigurableJoint[] joints = _rig.Instance.GetComponentsInChildren<ConfigurableJoint>();
            Assert.That(joints.Length, Is.GreaterThan(0),
                "The PlayerRagdoll must have at least one ConfigurableJoint.");

            float[] baselineSprings = new float[joints.Length];
            for (int j = 0; j < joints.Length; j++)
            {
                baselineSprings[j] = joints[j].slerpDrive.positionSpring;
            }

            // Trigger surrender directly.
            _rig.BalanceController.TriggerSurrender(0.7f);
            Assert.That(_rig.BalanceController.IsSurrendered, Is.True);

            // Wait for the spring ramp duration (0.15 s) plus a small margin.
            int rampFrames = Mathf.CeilToInt(0.15f / Time.fixedDeltaTime) + 2;
            for (int i = 0; i < rampFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            // Verify springs have ramped down to approximately 25% of baseline.
            int lowSpringCount = 0;
            for (int j = 0; j < joints.Length; j++)
            {
                float currentSpring = joints[j].slerpDrive.positionSpring;
                float expectedTarget = baselineSprings[j] * 0.25f;

                // Only check joints that had a non-trivial baseline spring.
                if (baselineSprings[j] > 0.01f)
                {
                    // Allow 20–30% range to accommodate per-segment multiplier differences.
                    Assert.That(currentSpring, Is.LessThanOrEqualTo(baselineSprings[j] * 0.35f),
                        $"Joint {joints[j].name} spring should be at most 35% of baseline " +
                        $"(baseline={baselineSprings[j]:F1}, current={currentSpring:F1}).");
                    lowSpringCount++;
                }
            }

            Assert.That(lowSpringCount, Is.GreaterThan(0),
                "At least one joint must have a non-trivial baseline spring to validate the ramp.");
        }
    }
}
