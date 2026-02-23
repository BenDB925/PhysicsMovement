using System.Collections;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Outcome-based gait tests that run in the Arena_01 scene and assert observable
    /// real-world results of sustained movement input. Unlike unit tests, these tests
    /// catch system-level regressions: weak springs, zeroed baselines, broken gait
    /// gating, or any other failure that prevents the character from actually walking.
    ///
    /// Design philosophy:
    ///   These tests exist because subtle bugs (e.g. CaptureBaselineDrives() running
    ///   before RagdollSetup.Awake, causing springs captured as 0) pass all unit tests
    ///   but produce "cat-pulsing-paws" behaviour in the editor — joints receiving
    ///   targetRotation but too limp to act on it. Outcome tests catch that class of
    ///   bug by asserting on physical results, not internal state.
    ///
    /// Timing: 5 seconds = 500 FixedUpdate frames at 100 Hz.
    /// </summary>
    public class GaitOutcomeTests
    {
        // ─── Constants ────────────────────────────────────────────────────────

        private const string ArenaSceneName = "Arena_01";

        /// <summary>100 Hz × 5 seconds = 500 FixedUpdate ticks for the walk runs.</summary>
        private const int WalkFrames = 500;

        /// <summary>Settle before input: let BC stabilise from spawn (1 second @ 100 Hz).</summary>
        private const int SettleFrames = 100;

        /// <summary>
        /// Minimum horizontal displacement expected after 5 seconds of held move input.
        /// At max speed 5 m/s this would be ~25 m; we require 2.5 m as a threshold that
        /// proves continuous forward progress without stalling mid-stride.
        /// </summary>
        private const float MinDisplacementMetres = 2.5f;

        /// <summary>
        /// Minimum peak upper-leg rotation observed during the walk run.
        /// LegAnimator targets stepAngle ~50° on forward swing; we require at least 15°
        /// of actual rotation so the joint is clearly responding to targetRotation.
        /// A value of 0–5° would indicate "pulsing paws" (joint receiving target but
        /// spring too weak to follow it).
        /// </summary>
        private const float MinPeakLegRotationDeg = 15f;

        /// <summary>
        /// Minimum SLERP spring required on leg joints after Start().
        /// Must match or exceed RagdollSetup._lowerLegSpring default so any zeroing
        /// regression is caught before physics even begins.
        /// </summary>
        private const float MinLegSpringAfterStart = 800f;

        // ─── Scene Setup ──────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator AfterStart_LegJointSprings_AreNonZero()
        {
            // Purpose: catch CaptureBaselineDrives() running before RagdollSetup.Awake
            // sets the authoritative spring values. If LegAnimator captures spring=0
            // and later calls SetLegSpringMultiplier(1f), it restores 0 × 1 = 0.
            // This was the root cause of the "cat pulsing paws" regression in Phase 3F2.
            //
            // Note: we wait SettleFrames (1s) before checking so the ragdoll has time to
            // land, transition Airborne→Standing, and restore springs to baseline.
            // Checking immediately after Start can catch the airborne-multiplier (0.15×)
            // rather than the true baseline, giving a false failure.

            yield return LoadArenaScene();

            // Wait for ragdoll to land and springs to be restored to baseline
            for (int i = 0; i < SettleFrames; i++)
                yield return new WaitForFixedUpdate();

            LegAnimator legAnimator = FindLegAnimator();
            ConfigurableJoint upperLegL = FindJointByName(legAnimator.gameObject, "UpperLeg_L");
            ConfigurableJoint upperLegR = FindJointByName(legAnimator.gameObject, "UpperLeg_R");
            ConfigurableJoint lowerLegL = FindJointByName(legAnimator.gameObject, "LowerLeg_L");
            ConfigurableJoint lowerLegR = FindJointByName(legAnimator.gameObject, "LowerLeg_R");

            Assert.That(upperLegL, Is.Not.Null, "UpperLeg_L joint not found on PlayerRagdoll hierarchy.");
            Assert.That(lowerLegL, Is.Not.Null, "LowerLeg_L joint not found on PlayerRagdoll hierarchy.");

            Assert.That(upperLegL.slerpDrive.positionSpring, Is.GreaterThanOrEqualTo(MinLegSpringAfterStart),
                $"UpperLeg_L spring={upperLegL.slerpDrive.positionSpring} after settle — must be >= {MinLegSpringAfterStart}. " +
                $"Likely cause: CaptureBaselineDrives() captured spring=0 before RagdollSetup.Awake ran, " +
                $"or springs not restored after Airborne→Standing transition.");

            Assert.That(upperLegR.slerpDrive.positionSpring, Is.GreaterThanOrEqualTo(MinLegSpringAfterStart),
                $"UpperLeg_R spring={upperLegR.slerpDrive.positionSpring} after settle — must be >= {MinLegSpringAfterStart}.");

            Assert.That(lowerLegL.slerpDrive.positionSpring, Is.GreaterThanOrEqualTo(MinLegSpringAfterStart),
                $"LowerLeg_L spring={lowerLegL.slerpDrive.positionSpring} after settle — must be >= {MinLegSpringAfterStart}.");

            Assert.That(lowerLegR.slerpDrive.positionSpring, Is.GreaterThanOrEqualTo(MinLegSpringAfterStart),
                $"LowerLeg_R spring={lowerLegR.slerpDrive.positionSpring} after settle — must be >= {MinLegSpringAfterStart}.");
        }

        [UnityTest]
        public IEnumerator HoldingMoveInput_For5Seconds_CharacterMovesForward()
        {
            // Purpose: catch any regression that causes zero net displacement under
            // sustained input. Covers: broken input gate, isMoving always false,
            // _isAirborne stuck true, BalanceController always fallen, etc.

            yield return LoadArenaScene();

            LegAnimator legAnimator = FindLegAnimator();
            PlayerMovement movement = legAnimator.GetComponent<PlayerMovement>();
            Rigidbody hipsRb = legAnimator.GetComponent<Rigidbody>();

            Assert.That(movement, Is.Not.Null, "PlayerMovement component not found.");
            Assert.That(hipsRb, Is.Not.Null, "Rigidbody not found on hips.");

            // Settle first
            for (int i = 0; i < SettleFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Vector3 startPos = hipsRb.position;

            // Apply rightward input for WalkFrames
            movement.SetMoveInputForTest(Vector2.right);
            for (int i = 0; i < WalkFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            movement.SetMoveInputForTest(Vector2.zero);

            Vector3 endPos = hipsRb.position;
            float displacement = Vector3.Distance(
                new Vector3(startPos.x, 0f, startPos.z),
                new Vector3(endPos.x, 0f, endPos.z));

            Assert.That(displacement, Is.GreaterThanOrEqualTo(MinDisplacementMetres),
                $"After 5s of move input, horizontal displacement was only {displacement:F2}m " +
                $"(minimum expected: {MinDisplacementMetres}m). Character may be frozen, " +
                $"constantly falling, or spring too weak to push off ground.");
        }

        [UnityTest]
        public IEnumerator HoldingMoveInput_For5Seconds_UpperLegsActuallyRotate()
        {
            // Purpose: catch "cat pulsing paws" — joints receive targetRotation but spring
            // is too weak to actually move the limb. This test observes the actual physical
            // rotation of UpperLeg_L during the walk and asserts a meaningful peak angle.

            yield return LoadArenaScene();

            LegAnimator legAnimator = FindLegAnimator();
            PlayerMovement movement = legAnimator.GetComponent<PlayerMovement>();
            ConfigurableJoint upperLegL = FindJointByName(legAnimator.gameObject, "UpperLeg_L");

            Assert.That(upperLegL, Is.Not.Null, "UpperLeg_L joint not found.");

            // Settle
            for (int i = 0; i < SettleFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            float peakRotation = 0f;

            movement.SetMoveInputForTest(Vector2.right);
            for (int i = 0; i < WalkFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                // Measure rotation on the primary (X) axis in local space
                float xAngle = upperLegL.transform.localEulerAngles.x;
                // Normalise to [-180, 180] so forward-swing (positive) and back-swing
                // (negative, wraps to ~310–350°) are both captured correctly.
                if (xAngle > 180f) xAngle -= 360f;
                peakRotation = Mathf.Max(peakRotation, Mathf.Abs(xAngle));
            }
            movement.SetMoveInputForTest(Vector2.zero);

            Assert.That(peakRotation, Is.GreaterThanOrEqualTo(MinPeakLegRotationDeg),
                $"UpperLeg_L peak rotation during 5s walk was only {peakRotation:F1}° " +
                $"(minimum expected: {MinPeakLegRotationDeg}°). This suggests the joint is " +
                $"receiving targetRotation but the SLERP spring is too weak to follow it " +
                $"(spring zeroed by CaptureBaselineDrives/SetLegSpringMultiplier bug).");
        }

        [UnityTest]
        public IEnumerator HoldingMoveInput_For5Seconds_LegsAlternate_NotBothPeakingTogether()
        {
            // Purpose: catch "frozen stride" — both legs stuck in the same phase, swinging
            // together instead of alternating. This indicates the gait phase is broken,
            // the sine wave is always returning the same value for both legs, or the
            // left/right phase offset is missing.
            //
            // Method: count frames where left and right upper legs are both simultaneously
            // at a high forward angle (>10°). In correct alternating gait this should be
            // rare (near the crossover point). If it's consistently high, legs are in sync.

            yield return LoadArenaScene();

            LegAnimator legAnimator = FindLegAnimator();
            PlayerMovement movement = legAnimator.GetComponent<PlayerMovement>();
            ConfigurableJoint upperLegL = FindJointByName(legAnimator.gameObject, "UpperLeg_L");
            ConfigurableJoint upperLegR = FindJointByName(legAnimator.gameObject, "UpperLeg_R");

            Assert.That(upperLegL, Is.Not.Null, "UpperLeg_L joint not found.");
            Assert.That(upperLegR, Is.Not.Null, "UpperLeg_R joint not found.");

            for (int i = 0; i < SettleFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            int bothForwardFrames = 0;
            int activeFrames = 0;
            const float syncDetectionThreshold = 10f;

            movement.SetMoveInputForTest(Vector2.right);
            for (int i = 0; i < WalkFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                float lAngle = upperLegL.transform.localEulerAngles.x;
                float rAngle = upperLegR.transform.localEulerAngles.x;
                if (lAngle > 180f) lAngle -= 360f;
                if (rAngle > 180f) rAngle -= 360f;

                if (Mathf.Abs(lAngle) > 5f || Mathf.Abs(rAngle) > 5f)
                {
                    activeFrames++;
                    if (lAngle > syncDetectionThreshold && rAngle > syncDetectionThreshold)
                    {
                        bothForwardFrames++;
                    }
                }
            }
            movement.SetMoveInputForTest(Vector2.zero);

            // Allow up to 10% of active frames to have both legs simultaneously forward
            // (natural at the crossover). More than that indicates synchronised gait.
            float syncFraction = activeFrames > 0 ? (float)bothForwardFrames / activeFrames : 0f;
            Assert.That(syncFraction, Is.LessThan(0.10f),
                $"Both upper legs were simultaneously forward for {syncFraction:P0} of active frames " +
                $"({bothForwardFrames}/{activeFrames}). Expected < 10% — legs should alternate. " +
                $"This suggests the left/right phase offset (π) is missing or the gait phase is broken.");
        }

        // ─── Helpers ──────────────────────────────────────────────────────────

        private static IEnumerator LoadArenaScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(ArenaSceneName, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, $"Failed to start async load for scene '{ArenaSceneName}'.");
            while (!load.isDone) yield return null;
            yield return null;
            yield return new WaitForFixedUpdate();
        }

        private static LegAnimator FindLegAnimator()
        {
            LegAnimator[] animators = Object.FindObjectsByType<LegAnimator>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Assert.That(animators.Length, Is.GreaterThan(0), "No active LegAnimator found in scene.");
            return animators[0];
        }

        private static ConfigurableJoint FindJointByName(GameObject root, string name)
        {
            ConfigurableJoint[] joints = root.GetComponentsInChildren<ConfigurableJoint>(false);
            foreach (ConfigurableJoint j in joints)
            {
                if (j.gameObject.name == name) return j;
            }
            return null;
        }
    }
}
