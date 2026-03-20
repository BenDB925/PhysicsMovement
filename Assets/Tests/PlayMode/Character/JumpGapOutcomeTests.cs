using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Outcome-based PlayMode tests that lock the standing and sprint gap targets
    /// before runtime tuning. These are intentionally red until the jump reach
    /// tuning slices land.
    /// </summary>
    public class JumpGapOutcomeTests
    {
        private const int SettleFrames = 80;
        private const float WindUpDurationSeconds = 0.2f;

        // Locked gap widths (meters) — do not change without revisiting the plan.
        private const float StandingShortGapMeters = 2.4f;
        private const float SprintGapMeters = 3.6f;

        private const float PlatformHeight = 1f;
        private const float LaunchPlatformLength = 12f;
        private const float LaunchPlatformWidth = 6f;
        private const float FarPlatformLength = 10f;
        private const float FarPlatformWidth = 6f;
        private const float SpawnHipsHeight = 0.5f;
        private const float StandingSpawnInset = 2f;
        private const float SprintSpawnInset = 4f;

        private const int StandingTouchdownBudgetFrames = 240;
        private const int SprintTouchdownBudgetFrames = 220;
        private const int FallenWindowSampleFrames = 90;
        private const int FallenWindowMaxFrames = 20;
        private const int SprintRampFrames = 520;

        private static readonly Vector3 TestOrigin = new Vector3(240f, 0f, 240f);

        private PlayerPrefabTestRig _rig;
        private GameObject _launchPlatform;
        private GameObject _farPlatform;
        private Vector3 _forward;

        [SetUp]
        public void SetUp()
        {
            // STEP 1: Build a prefab-backed rig with ground parked far below the gap platforms.
            _forward = Vector3.forward;
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin,
                GroundOffset = new Vector3(0f, -8f, 0f),
                GroundScale = new Vector3(10f, 1f, 10f),
            });
        }

        [TearDown]
        public void TearDown()
        {
            if (_launchPlatform != null)
            {
                UnityEngine.Object.Destroy(_launchPlatform);
            }

            if (_farPlatform != null)
            {
                UnityEngine.Object.Destroy(_farPlatform);
            }

            _rig?.Dispose();
            _rig = null;
        }

        [UnityTest]
        public IEnumerator StandingShortGap_FromRest_ClearsGapAndLandsOnFarPlatform()
        {
            // Arrange
            CreateGapPlatforms(StandingShortGapMeters);
            yield return _rig.WarmUp(SettleFrames);
            ClearGroundStateOverride();

            Vector3 spawnPosition = _launchPlatform.transform.position
                + _forward * (-LaunchPlatformLength * 0.5f + StandingSpawnInset)
                + Vector3.up * SpawnHipsHeight;
            RepositionRagdoll(_rig.RagdollSetup, _rig.HipsBody, spawnPosition);
            yield return new WaitForFixedUpdate();

            // Act
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            _rig.PlayerMovement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();
            _rig.PlayerMovement.SetJumpInputForTest(false);

            JumpGapOutcome outcome = new JumpGapOutcome();
            yield return CaptureJumpGapOutcome(outcome, StandingTouchdownBudgetFrames);

            // Assert
            Assert.That(outcome.MaxFarEdgeProgress, Is.GreaterThanOrEqualTo(StandingShortGapMeters),
                $"Standing jump should clear the {StandingShortGapMeters:F2}m gap " +
                $"(max progress {outcome.MaxFarEdgeProgress:F2}m)." );
            Assert.That(outcome.LandedOnFarPlatform, Is.True,
                "Standing jump should land on the far platform.");
            Assert.That(outcome.LandingFrame, Is.GreaterThanOrEqualTo(0),
                "Standing jump should land within the test window.");
            Assert.That(outcome.LandingFrame, Is.LessThanOrEqualTo(StandingTouchdownBudgetFrames),
                $"Standing jump should land within {StandingTouchdownBudgetFrames} frames " +
                $"(landed at {outcome.LandingFrame}).");
            AssertFallenWindowWithinLimit("Standing", outcome.MaxConsecutiveFallenFramesAfterLanding, FallenWindowMaxFrames);
        }

        [UnityTest]
        public IEnumerator SprintGap_WithRunUp_ClearsGapAndLandsOnFarPlatform()
        {
            // Arrange
            CreateGapPlatforms(SprintGapMeters);
            yield return _rig.WarmUp(SettleFrames);
            ClearGroundStateOverride();

            Vector3 spawnPosition = _launchPlatform.transform.position
                + _forward * (-LaunchPlatformLength * 0.5f + SprintSpawnInset)
                + Vector3.up * SpawnHipsHeight;
            RepositionRagdoll(_rig.RagdollSetup, _rig.HipsBody, spawnPosition);
            yield return new WaitForFixedUpdate();

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            _rig.PlayerMovement.SetSprintInputForTest(true);
            for (int frame = 0; frame < SprintRampFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            // Act
            _rig.PlayerMovement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();
            _rig.PlayerMovement.SetJumpInputForTest(false);

            JumpGapOutcome outcome = new JumpGapOutcome();
            yield return CaptureJumpGapOutcome(outcome, SprintTouchdownBudgetFrames);

            // Assert
            Assert.That(outcome.MaxFarEdgeProgress, Is.GreaterThanOrEqualTo(SprintGapMeters),
                $"Sprint jump should clear the {SprintGapMeters:F2}m gap " +
                $"(max progress {outcome.MaxFarEdgeProgress:F2}m)." );
            Assert.That(outcome.LandedOnFarPlatform, Is.True,
                "Sprint jump should land on the far platform.");
            Assert.That(outcome.LandingFrame, Is.GreaterThanOrEqualTo(0),
                "Sprint jump should land within the test window.");
            Assert.That(outcome.LandingFrame, Is.LessThanOrEqualTo(SprintTouchdownBudgetFrames),
                $"Sprint jump should land within {SprintTouchdownBudgetFrames} frames " +
                $"(landed at {outcome.LandingFrame}).");
            AssertFallenWindowWithinLimit("Sprint", outcome.MaxConsecutiveFallenFramesAfterLanding, FallenWindowMaxFrames);
        }

        private void CreateGapPlatforms(float gapWidth)
        {
            // STEP 2: Build the launch and far platforms separated by the target gap.
            Vector3 launchCenter = new Vector3(TestOrigin.x, TestOrigin.y - PlatformHeight * 0.5f, TestOrigin.z);
            _launchPlatform = CreatePlatform("JumpGap_LaunchPlatform", launchCenter, LaunchPlatformLength, LaunchPlatformWidth);

            float centerSeparation = LaunchPlatformLength * 0.5f + gapWidth + FarPlatformLength * 0.5f;
            Vector3 farCenter = launchCenter + _forward * centerSeparation;
            _farPlatform = CreatePlatform("JumpGap_FarPlatform", farCenter, FarPlatformLength, FarPlatformWidth);
        }

        private static GameObject CreatePlatform(string name, Vector3 center, float length, float width)
        {
            GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
            platform.name = name;
            platform.transform.position = center;
            platform.transform.localScale = new Vector3(width, PlatformHeight, length);
            platform.layer = GameSettings.LayerEnvironment;
            return platform;
        }

        private IEnumerator CaptureJumpGapOutcome(JumpGapOutcome outcome, int touchdownBudgetFrames)
        {
            // STEP 3: Track airborne progress, landing, and Fallen windows after touchdown.
            int windUpFrames = Mathf.CeilToInt(WindUpDurationSeconds / Time.fixedDeltaTime) + 2;
            for (int frame = 0; frame < windUpFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            bool wasAirborne = _rig.CharacterState.CurrentState == CharacterStateType.Airborne || !_rig.BalanceController.IsGrounded;
            int landingFrame = -1;
            int consecutiveFallenFrames = 0;
            int maxConsecutiveFallenFrames = 0;
            bool landedOnFarPlatform = false;

            int totalFrames = touchdownBudgetFrames + FallenWindowSampleFrames;
            for (int frame = 0; frame < totalFrames; frame++)
            {
                yield return new WaitForFixedUpdate();

                float progress = MeasureFarEdgeProgress(_rig.Hips.position);
                outcome.MaxFarEdgeProgress = Mathf.Max(outcome.MaxFarEdgeProgress, progress);

                bool isAirborne = _rig.CharacterState.CurrentState == CharacterStateType.Airborne || !_rig.BalanceController.IsGrounded;
                if (isAirborne)
                {
                    wasAirborne = true;
                }

                if (landingFrame < 0 && wasAirborne && _rig.BalanceController.IsGrounded)
                {
                    landingFrame = frame;
                    landedOnFarPlatform = IsTouchdownOnPlatform(_farPlatform, _rig.Hips.position);
                }

                if (landingFrame >= 0 && frame - landingFrame < FallenWindowSampleFrames)
                {
                    if (_rig.CharacterState.CurrentState == CharacterStateType.Fallen)
                    {
                        consecutiveFallenFrames++;
                        maxConsecutiveFallenFrames = Mathf.Max(maxConsecutiveFallenFrames, consecutiveFallenFrames);
                    }
                    else
                    {
                        consecutiveFallenFrames = 0;
                    }
                }
            }

            outcome.LandingFrame = landingFrame;
            outcome.LandedOnFarPlatform = landedOnFarPlatform;
            outcome.MaxConsecutiveFallenFramesAfterLanding = maxConsecutiveFallenFrames;
        }

        private float MeasureFarEdgeProgress(Vector3 hipsPosition)
        {
            Vector3 launchCenter = _launchPlatform.transform.position;
            Vector3 launchFarEdge = launchCenter + _forward * (LaunchPlatformLength * 0.5f);
            return Vector3.Dot(hipsPosition - launchFarEdge, _forward);
        }

        private static bool IsTouchdownOnPlatform(GameObject platform, Vector3 hipsPosition)
        {
            if (platform == null)
            {
                return false;
            }

            Collider collider = platform.GetComponent<Collider>();
            if (collider == null)
            {
                return false;
            }

            Ray ray = new Ray(hipsPosition + Vector3.up * 0.5f, Vector3.down);
            int mask = 1 << GameSettings.LayerEnvironment;
            if (Physics.Raycast(ray, out RaycastHit hit, 2f, mask, QueryTriggerInteraction.Ignore))
            {
                return hit.collider != null && hit.collider.gameObject == platform;
            }

            return false;
        }

        private static void AssertFallenWindowWithinLimit(string label, int maxConsecutiveFallenFrames, int allowedFrames)
        {
            Assert.That(maxConsecutiveFallenFrames, Is.LessThanOrEqualTo(allowedFrames),
                $"{label} jump should not remain Fallen for more than {allowedFrames} frames " +
                $"after touchdown (observed {maxConsecutiveFallenFrames}).");
        }

        private void ClearGroundStateOverride()
        {
            SetPrivateField(_rig.BalanceController, "_overrideGroundState", false);
        }

        private static void RepositionRagdoll(RagdollSetup ragdollSetup, Rigidbody hipsBody, Vector3 desiredHipsPosition)
        {
            Vector3 translation = desiredHipsPosition - hipsBody.position;

            if (ragdollSetup != null && ragdollSetup.AllBodies != null && ragdollSetup.AllBodies.Count > 0)
            {
                for (int i = 0; i < ragdollSetup.AllBodies.Count; i++)
                {
                    Rigidbody body = ragdollSetup.AllBodies[i];
                    if (body == null)
                    {
                        continue;
                    }

                    body.position += translation;
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                Physics.SyncTransforms();
                return;
            }

            Rigidbody[] bodies = hipsBody.GetComponentsInChildren<Rigidbody>(includeInactive: false);
            for (int i = 0; i < bodies.Length; i++)
            {
                Rigidbody body = bodies[i];
                if (body == null)
                {
                    continue;
                }

                body.position += translation;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            Physics.SyncTransforms();
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Could not find private field '{fieldName}' on {instance.GetType().Name}.");
            field.SetValue(instance, value);
        }

        private sealed class JumpGapOutcome
        {
            public float MaxFarEdgeProgress;
            public int LandingFrame = -1;
            public bool LandedOnFarPlatform;
            public int MaxConsecutiveFallenFramesAfterLanding;
        }
    }
}
