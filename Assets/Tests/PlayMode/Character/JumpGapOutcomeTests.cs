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
        private const float SpawnHipsHeightAbovePlatformTop = 0.5f;
        private const float StandingSpawnEdgeInset = -0.6f;
        private const float SprintSpawnEdgeInset = 2f;

        private const int StandingTouchdownBudgetFrames = 240;
        private const int SprintTouchdownBudgetFrames = 300;
        private const int FallenWindowSampleFrames = 90;

        private const float TouchdownProbeHeightOffset = 0.5f;
        private const float TouchdownProbeDistance = 2f;
        private const float TouchdownProbeForwardOffset = 0.2f;
        private const float TouchdownProbeLateralOffset = 0.25f;
        private const float TouchdownPlatformEdgeTolerance = 0.2f;
        private const float TouchdownHipsHeightTolerance = 1.25f;
        private const int FallenWindowMaxFrames = 20;
        private const int SprintRampFrames = 520;
        private const int ApexSampleFrames = 180;
        private const float ApexHeightBudgetMultiplier = 1.2f;
        private const float AirControlPlatformLength = 20f;
        private const float AirControlPlatformWidth = 20f;
        private const int AirControlLandingBudgetFrames = 240;
        private const float AirControlMinimumLateralDisplacement = 0.08f;
        private const float AirControlMaximumLateralDisplacement = 0.4f;
        private const float AirControlReverseTravelRetentionFloor = 0.7f;

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

            Vector3 spawnPosition = GetLaunchSpawnPosition(StandingSpawnEdgeInset);
            RepositionRagdoll(_rig.RagdollSetup, _rig.HipsBody, spawnPosition);
            yield return new WaitForFixedUpdate();

            // Act
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            _rig.PlayerMovement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();
            _rig.PlayerMovement.SetJumpInputForTest(false);

            JumpGapOutcome outcome = new JumpGapOutcome();
            yield return CaptureJumpGapOutcome(outcome, StandingTouchdownBudgetFrames);

            float launchImpulse = GetPrivateField<float>(_rig.PlayerMovement, "_jumpLaunchHorizontalImpulse");
            TestContext.Out.WriteLine($"[METRIC] StandingGap SpawnProgress={MeasureFarEdgeProgress(spawnPosition):F2}");
            TestContext.Out.WriteLine($"[METRIC] StandingGap LaunchImpulse={launchImpulse:F1}");
            TestContext.Out.WriteLine($"[METRIC] StandingGap MaxProgress={outcome.MaxFarEdgeProgress:F2}");

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

            Vector3 spawnPosition = GetLaunchSpawnPosition(SprintSpawnEdgeInset);
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

            float velocityPreservation = GetPrivateField<float>(_rig.PlayerMovement, "_jumpAirborneVelocityPreservationFactor");
            TestContext.Out.WriteLine($"[METRIC] SprintGap SpawnProgress={MeasureFarEdgeProgress(spawnPosition):F2}");
            TestContext.Out.WriteLine($"[METRIC] SprintGap VelocityPreservation={velocityPreservation:F2}");
            TestContext.Out.WriteLine($"[METRIC] SprintGap MaxProgress={outcome.MaxFarEdgeProgress:F2}");
            TestContext.Out.WriteLine($"[METRIC] SprintGap LandingFrame={outcome.LandingFrame}");

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

        [UnityTest]
        public IEnumerator StandingJump_LaunchImpulse_KeepsApexWithinTwentyPercentBudget()
        {
            // Arrange
            CreateGapPlatforms(StandingShortGapMeters);
            float baselineApexHeight = 0f;
            float tunedApexHeight = 0f;

            // Act
            yield return MeasureJumpApexHeight(StandingSpawnEdgeInset, sprintHeld: false, sprintRampFrames: 0, 0f, null, result => baselineApexHeight = result);
            yield return MeasureJumpApexHeight(StandingSpawnEdgeInset, sprintHeld: false, sprintRampFrames: 0, null, null, result => tunedApexHeight = result);

            // Assert
            float allowedApexHeight = baselineApexHeight * ApexHeightBudgetMultiplier;
            Assert.That(tunedApexHeight, Is.LessThanOrEqualTo(allowedApexHeight),
                $"Standing jump apex should stay within +20% of the no-horizontal-impulse baseline. " +
                $"Baseline={baselineApexHeight:F3}m, tuned={tunedApexHeight:F3}m, ceiling={allowedApexHeight:F3}m.");
        }

        [UnityTest]
        public IEnumerator SprintJump_VelocityPreservation_KeepsApexWithinTwentyPercentBudget()
        {
            // Arrange
            CreateGapPlatforms(SprintGapMeters);
            float baselineApexHeight = 0f;
            float tunedApexHeight = 0f;

            // Act
            yield return MeasureJumpApexHeight(SprintSpawnEdgeInset, sprintHeld: true, sprintRampFrames: SprintRampFrames, null, 0f, result => baselineApexHeight = result);
            yield return MeasureJumpApexHeight(SprintSpawnEdgeInset, sprintHeld: true, sprintRampFrames: SprintRampFrames, null, null, result => tunedApexHeight = result);

            // Assert
            float allowedApexHeight = baselineApexHeight * ApexHeightBudgetMultiplier;
            Assert.That(tunedApexHeight, Is.LessThanOrEqualTo(allowedApexHeight),
                $"Sprint jump apex should stay within +20% of the no-velocity-preservation baseline. " +
                $"Baseline={baselineApexHeight:F3}m, tuned={tunedApexHeight:F3}m, ceiling={allowedApexHeight:F3}m.");
        }

        [UnityTest]
        public IEnumerator JumpAirControl_AirborneRightInput_ProducesBoundedLateralDisplacement()
        {
            // Arrange
            CreateAirControlPlatform();
            yield return _rig.WarmUp(SettleFrames);
            ClearGroundStateOverride();

            Vector3 spawnPosition = GetSinglePlatformCenterSpawnPosition();
            RepositionRagdoll(_rig.RagdollSetup, _rig.HipsBody, spawnPosition);
            yield return new WaitForFixedUpdate();

            float maxLateralDisplacement = 0f;

            // Act
            yield return MeasureAirControlLateralDisplacement(Vector2.right, result => maxLateralDisplacement = result);
            TestContext.Out.WriteLine($"[METRIC] AirControl MaxLateralDisplacement={maxLateralDisplacement:F3}");

            // Assert
            Assert.That(maxLateralDisplacement, Is.GreaterThanOrEqualTo(AirControlMinimumLateralDisplacement),
                $"Pure airborne right input should trim landing by at least {AirControlMinimumLateralDisplacement:F2}m " +
                $"(observed {maxLateralDisplacement:F3}m).");
            Assert.That(maxLateralDisplacement, Is.LessThanOrEqualTo(AirControlMaximumLateralDisplacement),
                $"Pure airborne right input should stay bounded below {AirControlMaximumLateralDisplacement:F2}m over one jump " +
                $"(observed {maxLateralDisplacement:F3}m).");
        }

        [UnityTest]
        public IEnumerator JumpAirControl_FullReverseInput_RetainsAtLeastSeventyPercentOfForwardTravel()
        {
            // Arrange
            yield return PrepareAirControlScenario(useNearEdgeSpawn: true);

            float zeroInputForwardTravel = 0f;
            float reverseInputForwardTravel = 0f;

            // Act
            yield return MeasureJumpAirControlForwardTravel(Vector2.zero, result => zeroInputForwardTravel = result);

            Vector3 spawnPosition = GetSinglePlatformNearEdgeSpawnPosition();
            RepositionRagdoll(_rig.RagdollSetup, _rig.HipsBody, spawnPosition);
            yield return _rig.WarmUp(SettleFrames);
            ClearGroundStateOverride();
            yield return new WaitForFixedUpdate();

            yield return MeasureJumpAirControlForwardTravel(Vector2.down, result => reverseInputForwardTravel = result);

            float retainedTravelRatio = reverseInputForwardTravel / Mathf.Max(0.0001f, zeroInputForwardTravel);
            TestContext.Out.WriteLine($"[METRIC] AirControl ZeroInputForwardTravel={zeroInputForwardTravel:F3}");
            TestContext.Out.WriteLine($"[METRIC] AirControl ReverseInputForwardTravel={reverseInputForwardTravel:F3}");
            TestContext.Out.WriteLine($"[METRIC] AirControl ReverseRetentionRatio={retainedTravelRatio:F3}");

            // Assert
            Assert.That(retainedTravelRatio, Is.GreaterThanOrEqualTo(AirControlReverseTravelRetentionFloor),
                $"Full reverse airborne input should still retain at least {AirControlReverseTravelRetentionFloor:P0} of the zero-input forward travel " +
                $"(baseline {zeroInputForwardTravel:F3}m, reverse {reverseInputForwardTravel:F3}m, ratio {retainedTravelRatio:F3}).");
        }

        private IEnumerator MeasureJumpApexHeight(
            float spawnEdgeInset,
            bool sprintHeld,
            int sprintRampFrames,
            float? horizontalLaunchImpulseOverride,
            float? velocityPreservationOverride,
            Action<float> onComplete)
        {
            yield return _rig.WarmUp(SettleFrames);
            ClearGroundStateOverride();

            float originalHorizontalLaunchImpulse = GetPrivateField<float>(_rig.PlayerMovement, "_jumpLaunchHorizontalImpulse");
            float originalVelocityPreservation = GetPrivateField<float>(_rig.PlayerMovement, "_jumpAirborneVelocityPreservationFactor");
            SetPrivateField(
                _rig.PlayerMovement,
                "_jumpLaunchHorizontalImpulse",
                horizontalLaunchImpulseOverride ?? originalHorizontalLaunchImpulse);
            SetPrivateField(
                _rig.PlayerMovement,
                "_jumpAirborneVelocityPreservationFactor",
                velocityPreservationOverride ?? originalVelocityPreservation);

            Vector3 spawnPosition = GetLaunchSpawnPosition(spawnEdgeInset);
            RepositionRagdoll(_rig.RagdollSetup, _rig.HipsBody, spawnPosition);
            yield return new WaitForFixedUpdate();

            float launchHipsHeight = _rig.Hips.position.y;
            float peakHipsHeight = launchHipsHeight;

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            _rig.PlayerMovement.SetSprintInputForTest(sprintHeld);
            for (int frame = 0; frame < sprintRampFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            _rig.PlayerMovement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();
            _rig.PlayerMovement.SetJumpInputForTest(false);

            int windUpFrames = Mathf.CeilToInt(WindUpDurationSeconds / Time.fixedDeltaTime) + 2;
            for (int frame = 0; frame < windUpFrames + ApexSampleFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                peakHipsHeight = Mathf.Max(peakHipsHeight, _rig.Hips.position.y);
            }

            SetPrivateField(_rig.PlayerMovement, "_jumpLaunchHorizontalImpulse", originalHorizontalLaunchImpulse);
            SetPrivateField(_rig.PlayerMovement, "_jumpAirborneVelocityPreservationFactor", originalVelocityPreservation);
            onComplete?.Invoke(peakHipsHeight - launchHipsHeight);
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

        private void CreateAirControlPlatform()
        {
            Vector3 platformCenter = new Vector3(TestOrigin.x, TestOrigin.y - PlatformHeight * 0.5f, TestOrigin.z);
            _launchPlatform = CreatePlatform("JumpAirControl_Platform", platformCenter, AirControlPlatformLength, AirControlPlatformWidth);
            _farPlatform = null;
        }

        private IEnumerator PrepareAirControlScenario(bool useNearEdgeSpawn)
        {
            if (_launchPlatform != null)
            {
                UnityEngine.Object.Destroy(_launchPlatform);
                _launchPlatform = null;
            }

            if (_farPlatform != null)
            {
                UnityEngine.Object.Destroy(_farPlatform);
                _farPlatform = null;
            }

            CreateAirControlPlatform();
            yield return _rig.WarmUp(SettleFrames);
            ClearGroundStateOverride();

            Vector3 spawnPosition = useNearEdgeSpawn
                ? GetSinglePlatformNearEdgeSpawnPosition()
                : GetSinglePlatformCenterSpawnPosition();
            RepositionRagdoll(_rig.RagdollSetup, _rig.HipsBody, spawnPosition);
            yield return new WaitForFixedUpdate();
        }

        private Vector3 GetLaunchSpawnPosition(float edgeInset)
        {
            // STEP 2a: Spawn on top of the launch platform and near the lip facing the gap.
            //          The earlier slice spawned from the back half and used the platform centre
            //          height, which buried the hips into the cube and left the standing case
            //          too far from the launch edge to exercise the gap at all.
            Vector3 launchCenter = _launchPlatform.transform.position;
            Vector3 platformTop = launchCenter + Vector3.up * (PlatformHeight * 0.5f);
            Vector3 launchNearEdge = platformTop + _forward * (LaunchPlatformLength * 0.5f - edgeInset);
            return launchNearEdge + Vector3.up * SpawnHipsHeightAbovePlatformTop;
        }

        private Vector3 GetSinglePlatformCenterSpawnPosition()
        {
            Vector3 launchCenter = _launchPlatform.transform.position;
            Vector3 platformTop = launchCenter + Vector3.up * (PlatformHeight * 0.5f);
            return platformTop + Vector3.up * SpawnHipsHeightAbovePlatformTop;
        }

        private Vector3 GetSinglePlatformNearEdgeSpawnPosition()
        {
            Vector3 launchCenter = _launchPlatform.transform.position;
            Vector3 platformTop = launchCenter + Vector3.up * (PlatformHeight * 0.5f);
            Vector3 launchNearEdge = platformTop + _forward * (AirControlPlatformLength * 0.5f - StandingSpawnEdgeInset);
            return launchNearEdge + Vector3.up * SpawnHipsHeightAbovePlatformTop;
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

                bool touchdownOnFarPlatform = wasAirborne &&
                                              landingFrame < 0 &&
                                              IsTouchdownOnPlatform(_farPlatform, _rig.Hips.position);
                if (touchdownOnFarPlatform)
                {
                    // STEP 3a: Once the jump has gone airborne, the far-platform ownership probe is the
                    //          most reliable touchdown signal. Sprint landings can pass through Moving or
                    //          Fallen before grounded/state flags settle, so latch the first positive
                    //          far-platform hit directly instead of waiting for those transient flags.
                    landingFrame = frame;
                    landedOnFarPlatform = true;
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

        private IEnumerator MeasureAirControlLateralDisplacement(Vector2 airborneInput, Action<float> onComplete)
        {
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);
            _rig.PlayerMovement.SetSprintInputForTest(false);
            _rig.PlayerMovement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();
            _rig.PlayerMovement.SetJumpInputForTest(false);

            float startLateralPosition = _rig.Hips.position.x;
            float maxLateralDisplacement = 0f;
            bool wasAirborne = false;
            int groundedFramesAfterAirborne = 0;

            int totalFrames = AirControlLandingBudgetFrames + FallenWindowSampleFrames;
            for (int frame = 0; frame < totalFrames; frame++)
            {
                yield return new WaitForFixedUpdate();

                bool isAirborne = _rig.CharacterState.CurrentState == CharacterStateType.Airborne || !_rig.BalanceController.IsGrounded;
                if (isAirborne)
                {
                    wasAirborne = true;
                    _rig.PlayerMovement.SetMoveInputForTest(airborneInput);
                }
                else if (wasAirborne)
                {
                    groundedFramesAfterAirborne++;
                    _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);
                }

                maxLateralDisplacement = Mathf.Max(
                    maxLateralDisplacement,
                    Mathf.Abs(_rig.Hips.position.x - startLateralPosition));

                if (wasAirborne && groundedFramesAfterAirborne >= 5)
                {
                    break;
                }
            }

            onComplete?.Invoke(maxLateralDisplacement);
        }

        private IEnumerator MeasureJumpAirControlForwardTravel(Vector2 airborneInput, Action<float> onComplete)
        {
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            _rig.PlayerMovement.SetSprintInputForTest(false);
            _rig.PlayerMovement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();
            _rig.PlayerMovement.SetJumpInputForTest(false);

            float jumpStartForwardPosition = _rig.Hips.position.z;
            float maxForwardTravel = 0f;
            bool wasAirborne = false;
            int groundedFramesAfterAirborne = 0;

            int totalFrames = AirControlLandingBudgetFrames + FallenWindowSampleFrames;
            for (int frame = 0; frame < totalFrames; frame++)
            {
                yield return new WaitForFixedUpdate();

                bool isAirborne = _rig.CharacterState.CurrentState == CharacterStateType.Airborne || !_rig.BalanceController.IsGrounded;
                if (isAirborne)
                {
                    wasAirborne = true;
                    _rig.PlayerMovement.SetMoveInputForTest(airborneInput);
                }
                else if (wasAirborne)
                {
                    groundedFramesAfterAirborne++;
                    _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);
                }

                maxForwardTravel = Mathf.Max(maxForwardTravel, _rig.Hips.position.z - jumpStartForwardPosition);

                if (wasAirborne && groundedFramesAfterAirborne >= 5)
                {
                    break;
                }
            }

            onComplete?.Invoke(maxForwardTravel);
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

            Bounds expandedBounds = collider.bounds;
            expandedBounds.Expand(new Vector3(
                TouchdownPlatformEdgeTolerance * 2f,
                0f,
                TouchdownPlatformEdgeTolerance * 2f));

            Vector3 projectedHipsPosition = new Vector3(hipsPosition.x, expandedBounds.center.y, hipsPosition.z);
            float platformTop = collider.bounds.max.y;
            bool hipsOverPlatformBounds = expandedBounds.Contains(projectedHipsPosition) &&
                                         hipsPosition.y >= platformTop - 0.1f &&
                                         hipsPosition.y <= platformTop + TouchdownHipsHeightTolerance;
            if (hipsOverPlatformBounds)
            {
                return true;
            }

            int mask = 1 << GameSettings.LayerEnvironment;
            Vector3[] probeOffsets =
            {
                Vector3.zero,
                Vector3.forward * TouchdownProbeForwardOffset,
                Vector3.back * TouchdownProbeForwardOffset,
                Vector3.right * TouchdownProbeLateralOffset,
                Vector3.left * TouchdownProbeLateralOffset,
            };

            for (int i = 0; i < probeOffsets.Length; i++)
            {
                Ray ray = new Ray(hipsPosition + Vector3.up * TouchdownProbeHeightOffset + probeOffsets[i], Vector3.down);
                if (Physics.Raycast(ray, out RaycastHit hit, TouchdownProbeDistance, mask, QueryTriggerInteraction.Ignore) &&
                    hit.collider != null &&
                    hit.collider.gameObject == platform)
                {
                    return true;
                }
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

        private static T GetPrivateField<T>(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Could not find private field '{fieldName}' on {instance.GetType().Name}.");
            object value = field.GetValue(instance);
            Assert.That(value, Is.Not.Null, $"Private field '{fieldName}' on {instance.GetType().Name} should not be null.");
            return (T)value;
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
