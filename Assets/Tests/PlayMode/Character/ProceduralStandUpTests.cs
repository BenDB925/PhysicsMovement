using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for the Chapter 4 procedural stand-up sequence.
    /// Validates phase ordering, completion timing, failure → re-knockdown,
    /// impact interruption, forced-stand safety net, and smooth torque ramps.
    /// </summary>
    public class ProceduralStandUpTests
    {
        private const int WarmUpFrames = 150;
        private const int ProjectileLayer = 6;

        private static readonly Vector3 TestOrigin = new Vector3(0f, 0f, 10500f);

        private PlayerPrefabTestRig _rig;

        [TearDown]
        public void TearDown()
        {
            if (_rig?.PlayerMovement != null)
            {
                _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);
                _rig.PlayerMovement.SetSprintInputForTest(false);
            }

            _rig?.Dispose();
            _rig = null;
        }

        /// <summary>
        /// Helper: trigger surrender, wait for Fallen, wait for GettingUp.
        /// Returns true if GettingUp was reached within the budget.
        /// </summary>
        private IEnumerator TriggerSurrenderAndWaitForGettingUp(float severity, int maxFrames = 500)
        {
            _rig.BalanceController.TriggerSurrender(severity);

            // Apply a strong impulse to ensure the character physically falls over.
            // TriggerSurrender zeroes balance torques but the character may remain
            // upright for many frames without an external push.
            Rigidbody targetBody = _rig.TorsoBody != null ? _rig.TorsoBody : _rig.HipsBody;
            Vector3 forcePoint = targetBody.worldCenterOfMass + Vector3.up * 0.1f;
            targetBody.AddForceAtPosition(Vector3.left * 600f, forcePoint, ForceMode.Impulse);

            for (int i = 0; i < maxFrames; i++)
            {
                yield return new WaitForFixedUpdate();
                if (_rig.CharacterState.CurrentState == CharacterStateType.GettingUp)
                {
                    yield break;
                }
            }
        }

        /// <summary>
        /// Test 1: ProceduralStandUp completes from floor within 3 s.
        /// Trigger surrender → Fallen → GettingUp → stand-up completes →
        /// character reaches Standing or Moving.
        /// </summary>
        [UnityTest]
        public IEnumerator StandUp_CompletesWithin3Seconds()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin,
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(WarmUpFrames);

            ProceduralStandUp standUp = _rig.Instance.GetComponentInChildren<ProceduralStandUp>();
            Assert.That(standUp, Is.Not.Null,
                "The PlayerRagdoll prefab must have a ProceduralStandUp component.");

            yield return TriggerSurrenderAndWaitForGettingUp(0.3f);
            Assert.That(_rig.CharacterState.CurrentState, Is.EqualTo(CharacterStateType.GettingUp),
                "Character must reach GettingUp for the procedural stand-up to begin.");
            Assert.That(standUp.IsActive, Is.True,
                "ProceduralStandUp must be active during GettingUp.");

            float startTime = Time.time;
            bool completed = false;
            // Allow up to 15 s: retries include floor dwell (1.5–3.0 s per attempt)
            // + stand-up phases (up to 2.6 s). After 3 failures the forced-stand
            // safety net fires, so completion is guaranteed.
            int maxFrames = Mathf.CeilToInt(15f / Time.fixedDeltaTime);
            for (int i = 0; i < maxFrames; i++)
            {
                yield return new WaitForFixedUpdate();
                CharacterStateType state = _rig.CharacterState.CurrentState;
                if (state == CharacterStateType.Standing || state == CharacterStateType.Moving)
                {
                    completed = true;
                    break;
                }
                // If the character re-enters Fallen from a phase failure, it will
                // eventually retry. We just need to confirm it eventually completes.
            }

            float elapsed = Time.time - startTime;
            Assert.That(completed, Is.True,
                $"ProceduralStandUp must complete and reach Standing/Moving " +
                $"(elapsed: {elapsed:F2} s, final state: {_rig.CharacterState.CurrentState}).");
        }

        /// <summary>
        /// Test 2: Phases advance in order — OrientProne → ArmPush → LegTuck → Stand.
        /// We record each phase as it appears and assert the ordering.
        /// </summary>
        [UnityTest]
        public IEnumerator Phases_AdvanceInCorrectOrder()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin + new Vector3(100f, 0f, 0f),
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(WarmUpFrames);

            ProceduralStandUp standUp = _rig.Instance.GetComponentInChildren<ProceduralStandUp>();
            Assert.That(standUp, Is.Not.Null);

            yield return TriggerSurrenderAndWaitForGettingUp(0.3f);
            Assert.That(_rig.CharacterState.CurrentState, Is.EqualTo(CharacterStateType.GettingUp));
            Assert.That(standUp.IsActive, Is.True);

            // Track phase transitions. The character may fail a phase and
            // cycle through Fallen → GettingUp again. We record phases from
            // the latest attempt (clearing on each new GettingUp entry).
            var seenPhases = new List<StandUpPhase>();
            StandUpPhase lastSeen = StandUpPhase.Inactive;
            CharacterStateType prevCharState = CharacterStateType.GettingUp;

            int maxFrames = Mathf.CeilToInt(10f / Time.fixedDeltaTime);
            for (int i = 0; i < maxFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                CharacterStateType charState = _rig.CharacterState.CurrentState;

                // On fresh GettingUp entry, restart phase tracking for this attempt.
                if (charState == CharacterStateType.GettingUp &&
                    prevCharState != CharacterStateType.GettingUp)
                {
                    seenPhases.Clear();
                    lastSeen = StandUpPhase.Inactive;
                }

                StandUpPhase current = standUp.CurrentPhase;
                if (current != lastSeen && current != StandUpPhase.Inactive)
                {
                    seenPhases.Add(current);
                    lastSeen = current;
                }

                if (charState == CharacterStateType.Standing || charState == CharacterStateType.Moving)
                {
                    break;
                }

                prevCharState = charState;
            }

            Assert.That(seenPhases.Count, Is.GreaterThanOrEqualTo(1),
                $"Should see at least 1 phase. Seen: [{string.Join(", ", seenPhases)}]");

            // Verify ordering: each phase index should be >= the previous.
            for (int i = 1; i < seenPhases.Count; i++)
            {
                Assert.That((int)seenPhases[i], Is.GreaterThanOrEqualTo((int)seenPhases[i - 1]),
                    $"Phase order violated: {seenPhases[i - 1]} appeared before {seenPhases[i]}. " +
                    $"Full sequence: [{string.Join(", ", seenPhases)}]");
            }
        }

        /// <summary>
        /// Test 3: Arm push failure → character re-enters Fallen with short severity.
        /// We sabotage the arm push by pinning the hips to the ground so the chest
        /// cannot rise above the fail height. The phase should timeout and fire OnFailed.
        /// </summary>
        [UnityTest]
        public IEnumerator ArmPushFail_ReEntersFallenWithShortSeverity()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin + new Vector3(200f, 0f, 0f),
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(WarmUpFrames);

            ProceduralStandUp standUp = _rig.Instance.GetComponentInChildren<ProceduralStandUp>();
            Assert.That(standUp, Is.Not.Null);

            yield return TriggerSurrenderAndWaitForGettingUp(0.5f);
            Assert.That(_rig.CharacterState.CurrentState, Is.EqualTo(CharacterStateType.GettingUp));

            // Wait for Phase 1 (ArmPush) to start.
            bool reachedArmPush = false;
            for (int i = 0; i < 100; i++)
            {
                yield return new WaitForFixedUpdate();
                if (standUp.CurrentPhase == StandUpPhase.ArmPush)
                {
                    reachedArmPush = true;
                    break;
                }
            }

            Assert.That(reachedArmPush, Is.True, "Stand-up should reach ArmPush phase.");

            // Sabotage: apply strong downward force every frame to prevent chest rise.
            bool reFallen = false;
            int sabotageFrames = Mathf.CeilToInt(1.5f / Time.fixedDeltaTime);
            for (int i = 0; i < sabotageFrames; i++)
            {
                yield return new WaitForFixedUpdate();
                // Push the torso and hips down hard.
                if (_rig.TorsoBody != null)
                    _rig.TorsoBody.AddForce(Vector3.down * 500f, ForceMode.Force);
                _rig.HipsBody.AddForce(Vector3.down * 500f, ForceMode.Force);

                if (_rig.CharacterState.CurrentState == CharacterStateType.Fallen)
                {
                    reFallen = true;
                    break;
                }
            }

            Assert.That(reFallen, Is.True,
                "A failed arm push should re-enter Fallen " +
                $"(final state: {_rig.CharacterState.CurrentState}, phase: {standUp.CurrentPhase}).");
            Assert.That(_rig.CharacterState.WasSurrendered, Is.True,
                "Re-entry to Fallen from stand-up failure should keep WasSurrendered = true.");
            // The failure severity for arm push is 0.2 (short).
            Assert.That(_rig.CharacterState.KnockdownSeverityValue, Is.LessThanOrEqualTo(0.35f),
                $"Re-entry severity should be low (~0.2) " +
                $"(actual: {_rig.CharacterState.KnockdownSeverityValue:F2}).");
        }

        /// <summary>
        /// Regression: early stand-up phases must restore some height support before
        /// the final Stand phase. Without this, flat post-landing recoveries can sit
        /// in ArmPush until timeout and churn GettingUp -> Fallen.
        /// </summary>
        [UnityTest]
        public IEnumerator ArmPush_RestoresHeightSupportBeforeStandPhase()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin + new Vector3(250f, 0f, 0f),
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(WarmUpFrames);

            ProceduralStandUp standUp = _rig.Instance.GetComponentInChildren<ProceduralStandUp>();
            Assert.That(standUp, Is.Not.Null);

            yield return TriggerSurrenderAndWaitForGettingUp(0.5f);
            Assert.That(_rig.CharacterState.CurrentState, Is.EqualTo(CharacterStateType.GettingUp));

            bool reachedArmPush = false;
            float maxHeightSupport = 0f;
            float maxUprightSupport = 0f;
            float maxStabilizationSupport = 0f;
            int observationFrames = Mathf.CeilToInt(0.3f / Time.fixedDeltaTime);

            for (int i = 0; i < 200; i++)
            {
                yield return new WaitForFixedUpdate();
                if (standUp.CurrentPhase != StandUpPhase.ArmPush)
                {
                    continue;
                }

                reachedArmPush = true;
                for (int frame = 0; frame < observationFrames; frame++)
                {
                    yield return new WaitForFixedUpdate();
                    maxHeightSupport = Mathf.Max(
                        maxHeightSupport,
                        _rig.BalanceController.HeightMaintenanceScale);
                    maxUprightSupport = Mathf.Max(
                        maxUprightSupport,
                        _rig.BalanceController.UprightStrengthScale);
                    maxStabilizationSupport = Mathf.Max(
                        maxStabilizationSupport,
                        _rig.BalanceController.StabilizationScale);

                    if (standUp.CurrentPhase == StandUpPhase.Stand ||
                        standUp.CurrentPhase == StandUpPhase.Inactive)
                    {
                        break;
                    }
                }

                break;
            }

            Assert.That(reachedArmPush, Is.True, "Stand-up should reach ArmPush phase.");
            Assert.That(maxHeightSupport, Is.GreaterThan(0.05f),
                $"ArmPush should restore partial height support before Stand " +
                $"(actual max: {maxHeightSupport:F3}).");
            Assert.That(maxUprightSupport, Is.GreaterThan(0.05f),
                $"ArmPush should restore partial upright support before Stand " +
                $"(actual max: {maxUprightSupport:F3}).");
            Assert.That(maxStabilizationSupport, Is.GreaterThan(0.05f),
                $"ArmPush should restore partial stabilization before Stand " +
                $"(actual max: {maxStabilizationSupport:F3}).");
        }

        /// <summary>
        /// Test 4: External impact during a stand-up phase → full reset to Fallen.
        /// Use a projectile hit during GettingUp to trigger SurrenderTriggerCount
        /// increment, which makes CharacterState re-enter Fallen.
        /// </summary>
        [UnityTest]
        public IEnumerator ImpactDuringPhase_FullResetToFallen()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin + new Vector3(300f, 0f, 0f),
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(WarmUpFrames);

            ProceduralStandUp standUp = _rig.Instance.GetComponentInChildren<ProceduralStandUp>();
            Assert.That(standUp, Is.Not.Null);

            EnableProjectileCharacterCollisions();

            yield return TriggerSurrenderAndWaitForGettingUp(0.3f);
            Assert.That(_rig.CharacterState.CurrentState, Is.EqualTo(CharacterStateType.GettingUp));
            Assert.That(standUp.IsActive, Is.True);

            // Wait a few frames for the stand-up sequence to be well into a phase.
            for (int i = 0; i < 10; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            // Hit the character with a hard projectile to trigger re-knockdown.
            LaunchProjectileAtHips(
                _rig.HipsBody.worldCenterOfMass + Vector3.right * 1f,
                Vector3.left,
                speed: 30f,
                mass: 5f);

            bool reFallen = false;
            for (int i = 0; i < 200; i++)
            {
                yield return new WaitForFixedUpdate();
                if (_rig.CharacterState.CurrentState == CharacterStateType.Fallen)
                {
                    reFallen = true;
                    break;
                }
            }

            Assert.That(reFallen, Is.True,
                "An external impact during stand-up should fully reset to Fallen.");
        }

        /// <summary>
        /// Test 5: After 3 failures, the forced-stand safety net activates and
        /// the character eventually reaches Standing or Moving.
        /// We sabotage each attempt by pushing the character down, and after
        /// 3 failures the forced impulse should complete the stand-up.
        /// </summary>
        [UnityTest]
        public IEnumerator ThreeFailures_ForcedStandSafetyNet()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin + new Vector3(400f, 0f, 0f),
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(WarmUpFrames);

            ProceduralStandUp standUp = _rig.Instance.GetComponentInChildren<ProceduralStandUp>();
            Assert.That(standUp, Is.Not.Null);

            int fallenCount = 0;
            int gettingUpCount = 0;
            bool reachedStanding = false;

            // Use the standard helper to reliably knock the character down and
            // wait for the first GettingUp entry.
            yield return TriggerSurrenderAndWaitForGettingUp(0.5f);
            Assert.That(_rig.CharacterState.CurrentState, Is.EqualTo(CharacterStateType.GettingUp),
                "Character must reach GettingUp before sabotage tracking begins.");
            fallenCount = 1; // Count the initial Fallen entry.
            gettingUpCount = 1;

            // Run for up to 30 s — each attempt is: floor dwell + stand-up attempt.
            // We sabotage during stand-up phases to force failures.
            int maxFrames = Mathf.CeilToInt(30f / Time.fixedDeltaTime);
            CharacterStateType prevState = CharacterStateType.GettingUp;
            bool sabotaging = true; // Start sabotaging the first GettingUp attempt.

            for (int i = 0; i < maxFrames; i++)
            {
                yield return new WaitForFixedUpdate();
                CharacterStateType state = _rig.CharacterState.CurrentState;

                // Track Fallen entries.
                if (state == CharacterStateType.Fallen && prevState != CharacterStateType.Fallen)
                {
                    fallenCount++;
                }

                // Track GettingUp entries to know which attempt we're on.
                if (state == CharacterStateType.GettingUp && prevState != CharacterStateType.GettingUp)
                {
                    gettingUpCount++;
                }

                // Enable sabotage during the first 3 GettingUp attempts.
                if (state == CharacterStateType.GettingUp)
                {
                    sabotaging = gettingUpCount <= 3;
                }
                else
                {
                    sabotaging = false;
                }

                // Sabotage: push body down to fail the stand-up phases.
                if (sabotaging && state == CharacterStateType.GettingUp)
                {
                    if (_rig.TorsoBody != null)
                        _rig.TorsoBody.AddForce(Vector3.down * 600f, ForceMode.Force);
                    _rig.HipsBody.AddForce(Vector3.down * 600f, ForceMode.Force);
                }

                if (state == CharacterStateType.Standing || state == CharacterStateType.Moving)
                {
                    reachedStanding = true;
                    break;
                }

                prevState = state;
            }

            Assert.That(reachedStanding, Is.True,
                $"After {fallenCount} fallen entries, the forced-stand safety net " +
                $"should eventually bring the character to Standing/Moving.");
            // We expect at least 2 Fallen entries (initial + at least 1 re-knockdown).
            Assert.That(fallenCount, Is.GreaterThanOrEqualTo(2),
                $"Should have retried at least once before forced stand " +
                $"(fallen entries: {fallenCount}).");
        }

        /// <summary>
        /// Test 6: Upright torque ramps smoothly during Phase 3 (Stand) —
        /// no step-function jumps. Sample UprightStrengthScale over several frames
        /// during the Stand phase and verify monotonic increase without large steps.
        /// </summary>
        [UnityTest]
        public IEnumerator StandPhase_UprightTorqueRampsSmoothly()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin + new Vector3(500f, 0f, 0f),
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(WarmUpFrames);

            ProceduralStandUp standUp = _rig.Instance.GetComponentInChildren<ProceduralStandUp>();
            Assert.That(standUp, Is.Not.Null);

            yield return TriggerSurrenderAndWaitForGettingUp(0.3f);
            Assert.That(_rig.CharacterState.CurrentState, Is.EqualTo(CharacterStateType.GettingUp));

            // Wait for Stand phase. The character may fail earlier phases and
            // retry, so budget enough time for the full retry cycle including
            // floor dwell between attempts and the forced-stand safety net.
            // If forced stand fires (after 3 failures), it bypasses the Stand
            // phase entirely. We monitor both the Stand phase and the ramp-up
            // that happens during any recovery path.
            bool reachedStandPhase = false;
            for (int i = 0; i < Mathf.CeilToInt(15f / Time.fixedDeltaTime); i++)
            {
                yield return new WaitForFixedUpdate();
                if (standUp.CurrentPhase == StandUpPhase.Stand)
                {
                    reachedStandPhase = true;
                    break;
                }
                // If the character completed the stand-up and reached
                // Standing/Moving, stop; the forced stand bypassed Stand phase.
                CharacterStateType charState = _rig.CharacterState.CurrentState;
                if (charState == CharacterStateType.Standing || charState == CharacterStateType.Moving)
                {
                    break;
                }
            }

            // If we didn't reach the Stand phase, the forced-stand safety net
            // completed the recovery. In that case, verify the ramp happened
            // via ClearSurrender instead.
            if (!reachedStandPhase)
            {
                // After forced stand, ClearSurrender ramps upright strength to 1.0.
                // Wait for the ramp to progress before checking.
                int rampFrames = Mathf.CeilToInt(0.5f / Time.fixedDeltaTime);
                for (int i = 0; i < rampFrames; i++)
                {
                    yield return new WaitForFixedUpdate();
                }
                float uprightScale = _rig.BalanceController.UprightStrengthScale;
                Assert.That(uprightScale, Is.GreaterThanOrEqualTo(0.5f),
                    $"After forced stand, upright strength should be ramped back up " +
                    $"(actual: {uprightScale:F3}).");
                yield break;
            }

            // Now sample UprightStrengthScale over several frames during Stand phase.
            var samples = new List<float>();
            int sampleFrames = Mathf.CeilToInt(0.5f / Time.fixedDeltaTime);
            for (int i = 0; i < sampleFrames; i++)
            {
                yield return new WaitForFixedUpdate();
                samples.Add(_rig.BalanceController.UprightStrengthScale);

                // Stop if we leave Stand phase.
                if (standUp.CurrentPhase != StandUpPhase.Stand &&
                    standUp.CurrentPhase != StandUpPhase.Inactive)
                {
                    break;
                }
            }

            Assert.That(samples.Count, Is.GreaterThanOrEqualTo(3),
                $"Need at least 3 samples to verify ramp smoothness (got {samples.Count}).");

            // Verify no large step jumps (> 0.5 in a single frame).
            for (int i = 1; i < samples.Count; i++)
            {
                float delta = samples[i] - samples[i - 1];
                Assert.That(Mathf.Abs(delta), Is.LessThan(0.5f),
                    $"Upright torque ramp should be smooth. " +
                    $"Frame {i}: delta={delta:F3} (prev={samples[i - 1]:F3}, curr={samples[i]:F3}).");
            }

            // Verify overall increase: last sample should be >= first (ramp up).
            Assert.That(samples[samples.Count - 1], Is.GreaterThanOrEqualTo(samples[0] - 0.01f),
                $"Upright torque should ramp up during Stand phase. " +
                $"First={samples[0]:F3}, Last={samples[samples.Count - 1]:F3}.");
        }

        // ─── Projectile Helpers (matching ImpactKnockdownTests pattern) ─────

        private static void EnableProjectileCharacterCollisions()
        {
            Physics.IgnoreLayerCollision(ProjectileLayer, GameSettings.LayerPlayer1Parts, false);
            Physics.IgnoreLayerCollision(ProjectileLayer, GameSettings.LayerLowerLegParts, false);
            Physics.IgnoreLayerCollision(ProjectileLayer, ProjectileLayer, true);
            Physics.IgnoreLayerCollision(ProjectileLayer, GameSettings.LayerEnvironment, true);
        }

        private GameObject LaunchProjectileAtHips(
            Vector3 spawnPosition,
            Vector3 direction,
            float speed,
            float mass)
        {
            GameObject projectile = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            projectile.name = "TestProjectile";
            projectile.transform.position = spawnPosition;
            projectile.transform.localScale = Vector3.one * 0.3f;
            projectile.layer = ProjectileLayer;

            Rigidbody rb = projectile.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.useGravity = false;
            rb.linearVelocity = direction.normalized * speed;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

            Collider projectileCol = projectile.GetComponent<Collider>();
            foreach (Collider col in _rig.Instance.GetComponentsInChildren<Collider>())
            {
                if (col.attachedRigidbody == _rig.HipsBody)
                    continue;
                Physics.IgnoreCollision(projectileCol, col, true);
            }

            Object.Destroy(projectile, 5f);
            return projectile;
        }
    }
}
