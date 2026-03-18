using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode integration tests for Phase 3F2 — Airborne Leg Spring Scaling.
    ///
    /// Tests covered:
    /// - WhenAirborne_LegSpringIsReduced: entering Airborne reduces UpperLeg_L slerpDrive
    ///   positionSpring below the baseline captured at Start.
    /// - WhenLanded_LegSpringIsRestored: exiting Airborne restores spring to baseline.
    /// - WhenAirborne_GaitPhaseDoesNotAdvance: _phase does not advance while Airborne,
    ///   even with non-zero move input and velocity.
    ///
    /// Design notes:
    /// - RagdollSetup is added first so its Awake assigns authoritative spring values
    ///   before LegAnimator.Start captures the baseline.
    /// - SetGroundStateForTest(false, false) triggers the CharacterState → Airborne
    ///   transition on the next FixedUpdate; one additional tick is waited to ensure
    ///   the OnStateChanged handler has fired and springs have been adjusted.
    /// - All assertions are outcome-based (no reflection into private spring state —
    ///   we read the actual ConfigurableJoint.slerpDrive.positionSpring).
    /// - State injection is done in SetUp BEFORE the first physics tick via the
    ///   SetGroundStateForTest test seam.
    /// </summary>
    public class AirborneSpringTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";
        private static readonly Vector3 TestOrigin = new Vector3(1200f, 0f, 1200f);

        // ── Test Rig ────────────────────────────────────────────────────────

        private GameObject _ground;
        private GameObject _hips;
        private Rigidbody _hipsRb;
        private BalanceController _balance;
        private PlayerMovement _movement;
        private CharacterState _characterState;
        private LegAnimator _legAnimator;
        private float _savedFixedDeltaTime;
        private int _savedSolverIterations;
        private int _savedSolverVelocityIterations;
        private bool[,] _savedLayerCollisionMatrix;

        // Leg joints (created as children of Hips in the same topology used by runtime)
        private GameObject _upperLegL;
        private GameObject _upperLegR;
        private GameObject _lowerLegL;
        private GameObject _lowerLegR;
        private ConfigurableJoint _upperLegLJoint;
        private ConfigurableJoint _upperLegRJoint;
        private ConfigurableJoint _lowerLegLJoint;
        private ConfigurableJoint _lowerLegRJoint;

        [SetUp]
        public void SetUp()
        {
            _savedFixedDeltaTime = Time.fixedDeltaTime;
            _savedSolverIterations = Physics.defaultSolverIterations;
            _savedSolverVelocityIterations = Physics.defaultSolverVelocityIterations;
            _savedLayerCollisionMatrix = CaptureLayerCollisionMatrix();

            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;

            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "AirborneSpringTests_Ground";
            _ground.transform.position = TestOrigin + new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(40f, 1f, 40f);
            _ground.layer = PhysicsDrivenMovement.Core.GameSettings.LayerEnvironment;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null,
                $"PlayerRagdoll prefab must be loadable from '{PlayerRagdollPrefabPath}'.");

            _hips = UnityEngine.Object.Instantiate(prefab, TestOrigin + new Vector3(0f, 1.1f, 0f), Quaternion.identity);
            _hipsRb = _hips.GetComponent<Rigidbody>();
            _balance = _hips.GetComponent<BalanceController>();
            _movement = _hips.GetComponent<PlayerMovement>();
            _characterState = _hips.GetComponent<CharacterState>();
            _legAnimator = _hips.GetComponent<LegAnimator>();

            _upperLegL = FindRequiredChild(_hips.transform, "UpperLeg_L").gameObject;
            _upperLegR = FindRequiredChild(_hips.transform, "UpperLeg_R").gameObject;
            _lowerLegL = FindRequiredChild(_hips.transform, "LowerLeg_L").gameObject;
            _lowerLegR = FindRequiredChild(_hips.transform, "LowerLeg_R").gameObject;

            _upperLegLJoint = _upperLegL.GetComponent<ConfigurableJoint>();
            _upperLegRJoint = _upperLegR.GetComponent<ConfigurableJoint>();
            _lowerLegLJoint = _lowerLegL.GetComponent<ConfigurableJoint>();
            _lowerLegRJoint = _lowerLegR.GetComponent<ConfigurableJoint>();

            Assert.That(_hipsRb, Is.Not.Null, "PlayerRagdoll prefab must include a root Rigidbody.");
            Assert.That(_balance, Is.Not.Null, "PlayerRagdoll prefab must include BalanceController.");
            Assert.That(_movement, Is.Not.Null, "PlayerRagdoll prefab must include PlayerMovement.");
            Assert.That(_characterState, Is.Not.Null, "PlayerRagdoll prefab must include CharacterState.");
            Assert.That(_legAnimator, Is.Not.Null, "PlayerRagdoll prefab must include LegAnimator.");

            _movement.SetMoveInputForTest(Vector2.zero);
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Standing);
            SetPrivateField(_legAnimator, "_spinSuppressFrames", 5);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hips != null)
            {
                UnityEngine.Object.Destroy(_hips);
            }

            if (_ground != null)
            {
                UnityEngine.Object.Destroy(_ground);
            }

            Time.fixedDeltaTime = _savedFixedDeltaTime;
            Physics.defaultSolverIterations = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
            RestoreLayerCollisionMatrix(_savedLayerCollisionMatrix);
        }

        // ── Test 1: Airborne reduces leg spring ──────────────────────────────

        /// <summary>
        /// When the character transitions to Airborne, the UpperLeg_L ConfigurableJoint
        /// positionSpring (slerpDrive) must be reduced below its baseline value.
        ///
        /// Mechanism:
        ///   1. Wait one frame for Start() to capture the baseline spring values.
        ///   2. Record the current (baseline) positionSpring from the joint.
        ///   3. Trigger Airborne: SetGroundStateForTest(false, false) → CharacterState
        ///      transitions to Airborne on the next FixedUpdate → OnStateChanged fires
        ///      → LegAnimator.OnCharacterStateChanged multiplies spring by _airborneSpringMultiplier.
        ///   4. Assert the spring is lower than baseline.
        /// </summary>
        [UnityTest]
        public IEnumerator WhenAirborne_LegSpringIsReduced()
        {
            // ── Arrange ──────────────────────────────────────────────────────
            // Wait one frame so Awake + Start complete (RagdollSetup applied springs;
            // LegAnimator.Start captured baselines).
            yield return null;

            // Record baseline spring directly from the joint (same source LegAnimator captured).
            float baselineSpring = _upperLegLJoint.slerpDrive.positionSpring;

            // Pre-condition: RagdollSetup must have set a non-zero spring so the reduction
            // is measurable. If this fails, the rig is mis-configured, not the feature.
            Assume.That(baselineSpring, Is.GreaterThan(1f),
                $"Pre-condition: UpperLeg_L slerpDrive.positionSpring must be > 1 after RagdollSetup. " +
                $"Got: {baselineSpring}. RagdollSetup may not have been added first.");

            // ── Act ──────────────────────────────────────────────────────────
            // Trigger the Airborne transition: make CharacterState see !grounded && !fallen.
            _balance.SetGroundStateForTest(isGrounded: false, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Airborne);

            // Wait one FixedUpdate so LegAnimator.FixedUpdate processes the new state.
            yield return new WaitForFixedUpdate();

            // ── Assert ───────────────────────────────────────────────────────
            float airborneSpring = _upperLegLJoint.slerpDrive.positionSpring;

            Assert.That(airborneSpring, Is.LessThan(baselineSpring),
                $"UpperLeg_L slerpDrive.positionSpring must be reduced when Airborne. " +
                $"Baseline={baselineSpring:F2}  Airborne={airborneSpring:F2}. " +
                $"Check that LegAnimator subscribes to OnStateChanged and calls SetLegSpringMultiplier.");

            // Also verify the reduction is approximately the _airborneSpringMultiplier (0.15 default).
            float expectedMax = baselineSpring * 0.9f;  // generous: must be < 90% of baseline
            Assert.That(airborneSpring, Is.LessThan(expectedMax),
                $"Spring reduction must be meaningful (< 90% of baseline). " +
                $"Baseline={baselineSpring:F2}  Airborne={airborneSpring:F2}  ExpectedMax={expectedMax:F2}. " +
                $"Default _airborneSpringMultiplier=0.15 should produce ~15% of baseline.");
        }

        // ── Test 2: Landing restores leg spring ─────────────────────────────

        /// <summary>
        /// When the character exits Airborne (landing → Standing or Moving), all four
        /// leg joint SLERP drive positionSpring values must be restored to their
        /// baseline values (captured at Start, before any airborne scaling).
        ///
        /// This verifies the restore path: OnCharacterStateChanged sees
        /// previousState == Airborne and newState != Airborne → calls
        /// SetLegSpringMultiplier(1f) to restore full stiffness.
        /// </summary>
        [UnityTest]
        public IEnumerator WhenLanded_LegSpringIsRestored()
        {
            // ── Arrange ──────────────────────────────────────────────────────
            yield return null;

            // Capture baseline springs for all four joints.
            float baselineUL = _upperLegLJoint.slerpDrive.positionSpring;
            float baselineUR = _upperLegRJoint.slerpDrive.positionSpring;
            float baselineLL = _lowerLegLJoint.slerpDrive.positionSpring;
            float baseLR     = _lowerLegRJoint.slerpDrive.positionSpring;

            Assume.That(baselineUL, Is.GreaterThan(1f),
                $"Pre-condition: UpperLeg_L spring must be > 1 after RagdollSetup. Got: {baselineUL}.");

            // Enter Airborne.
            _balance.SetGroundStateForTest(isGrounded: false, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Airborne);
            yield return new WaitForFixedUpdate();

            // Verify springs are reduced (confirms the feature activated).
            float airborneUL = _upperLegLJoint.slerpDrive.positionSpring;
            Assume.That(airborneUL, Is.LessThan(baselineUL),
                $"Pre-condition: spring must be reduced while Airborne. " +
                $"Baseline={baselineUL:F2} Airborne={airborneUL:F2}.");

            // ── Act — land (go back to grounded) ─────────────────────────────
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Standing);
            yield return new WaitForFixedUpdate();

            // ── Assert — all four joint springs are restored to baseline ──────
            float restoredUL = _upperLegLJoint.slerpDrive.positionSpring;
            float restoredUR = _upperLegRJoint.slerpDrive.positionSpring;
            float restoredLL = _lowerLegLJoint.slerpDrive.positionSpring;
            float restoredLR = _lowerLegRJoint.slerpDrive.positionSpring;

            const float tolerance = 0.01f;

            Assert.That(restoredUL, Is.EqualTo(baselineUL).Within(tolerance),
                $"UpperLeg_L spring must be restored to baseline after landing. " +
                $"Baseline={baselineUL:F2} Restored={restoredUL:F2}.");

            Assert.That(restoredUR, Is.EqualTo(baselineUR).Within(tolerance),
                $"UpperLeg_R spring must be restored to baseline after landing. " +
                $"Baseline={baselineUR:F2} Restored={restoredUR:F2}.");

            Assert.That(restoredLL, Is.EqualTo(baselineLL).Within(tolerance),
                $"LowerLeg_L spring must be restored to baseline after landing. " +
                $"Baseline={baselineLL:F2} Restored={restoredLL:F2}.");

            Assert.That(restoredLR, Is.EqualTo(baseLR).Within(tolerance),
                $"LowerLeg_R spring must be restored to baseline after landing. " +
                $"Baseline={baseLR:F2} Restored={restoredLR:F2}.");
        }

        [UnityTest]
        public IEnumerator WhenLandingBounces_LandingAbsorptionDoesNotRestart()
        {
            yield return null;

            float landingAbsorbDuration = (float)GetPrivateField(_legAnimator, "_landingAbsorbDuration");
            float landingAbsorbBlendOutDuration = (float)GetPrivateField(_legAnimator, "_landingAbsorbBlendOutDuration");
            int framesBeforeBounce = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    (landingAbsorbDuration + landingAbsorbBlendOutDuration - landingAbsorbBlendOutDuration * 0.25f)
                    / Time.fixedDeltaTime));
            int framesUntilOriginalWindowExpires = Mathf.CeilToInt(landingAbsorbBlendOutDuration / Time.fixedDeltaTime) + 2;

            _balance.SetGroundStateForTest(isGrounded: false, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Airborne);
            yield return new WaitForFixedUpdate();

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Moving);
            yield return new WaitForFixedUpdate();

            float initialTimer = (float)GetPrivateField(_legAnimator, "_landingAbsorbTimer");
            Assert.That(initialTimer, Is.GreaterThan(0f),
                "A clean Airborne -> Moving landing should start the landing absorption timer.");

            for (int frame = 0; frame < framesBeforeBounce; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            float agedTimer = (float)GetPrivateField(_legAnimator, "_landingAbsorbTimer");
            Assert.That(agedTimer, Is.GreaterThan(0f).And.LessThan(initialTimer),
                "The landing absorption timer should still be active but already decaying before the bounce.");

            _balance.SetGroundStateForTest(isGrounded: false, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Airborne);
            yield return new WaitForFixedUpdate();

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Moving);
            yield return new WaitForFixedUpdate();

            float postBounceTimer = (float)GetPrivateField(_legAnimator, "_landingAbsorbTimer");
            Assert.That(postBounceTimer, Is.LessThan(agedTimer + Time.fixedDeltaTime * 1.5f),
                $"A same-landing bounce must not restart the leg landing absorption timer. " +
                $"Aged={agedTimer:F3}s PostBounce={postBounceTimer:F3}s.");

            for (int frame = 0; frame < framesUntilOriginalWindowExpires; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            float resolvedTimer = (float)GetPrivateField(_legAnimator, "_landingAbsorbTimer");
            Assert.That(resolvedTimer, Is.LessThan(0.0001f),
                "The original landing absorption window should still expire without extra bounce time being added.");
        }

        // ── Test 3: Gait phase does not advance while Airborne ───────────────

        /// <summary>
        /// While the character is Airborne, the gait phase accumulator (_phase) must
        /// NOT advance, even with non-zero move input and non-zero velocity.
        ///
        /// Rationale: legs shouldn't keep cycling mid-air. The _isAirborne flag forces
        /// isMoving = false in FixedUpdate, preventing phase from advancing.
        ///
        /// Design:
        ///   Set non-zero velocity and move input so that on the ground, phase would
        ///   advance. Then enter Airborne and verify phase stays constant over several ticks.
        /// </summary>
        [UnityTest]
        public IEnumerator WhenAirborne_GaitPhaseDoesNotAdvance()
        {
            // ── Arrange ──────────────────────────────────────────────────────
            yield return null;

            // Give the hips velocity and move input so that if grounded, phase would advance.
            _hipsRb.isKinematic = false;
            _hipsRb.linearVelocity = new Vector3(0f, 0f, 3f);
            _movement.enabled = false;
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Set a minimum step frequency so phase advances even at zero velocity.
            SetPrivateField(_legAnimator, "_stepFrequency", 2f);

            // Let the system settle and advance phase for a couple of frames.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // ── Act — enter Airborne ──────────────────────────────────────────
            _balance.SetGroundStateForTest(isGrounded: false, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Airborne);
            yield return new WaitForFixedUpdate();  // CharacterState transitions to Airborne

            // Capture phase immediately after entering Airborne.
            float phaseAtAirborneEntry = GetPhase();

            // Run several more FixedUpdate ticks while airborne.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // ── Assert ───────────────────────────────────────────────────────
            float phaseAfterAirborneTicks = GetPhase();

            Assert.That(phaseAfterAirborneTicks, Is.LessThanOrEqualTo(phaseAtAirborneEntry + 0.001f),
                $"Gait phase must not ADVANCE while Airborne (it may decay toward zero but must not increase). " +
                $"PhaseAtEntry={phaseAtAirborneEntry:F4} PhaseAfterTicks={phaseAfterAirborneTicks:F4}. " +
                $"Check that LegAnimator forces isMoving=false when _isAirborne is true.");
        }

        // ── Rig builder helpers ──────────────────────────────────────────────

        /// <summary>
        /// Creates a child GameObject with a Rigidbody (gravity enabled), a BoxCollider,
        /// and a ConfigurableJoint connected to <paramref name="parentRb"/>.
        /// Linear axes are locked; rotation is free (SLERP drive). RagdollSetup will
        /// override the initial drive with its authoritative spring/damper values.
        /// </summary>
        private static GameObject CreatePhysicsLegJoint(
            GameObject parent, string name, Rigidbody parentRb, Vector3 localPos, float mass)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = localPos;

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.useGravity = true;

            go.AddComponent<BoxCollider>();

            ConfigurableJoint joint = go.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parentRb;

            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;

            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = new JointDrive
            {
                positionSpring = 100f,
                positionDamper = 10f,
                maximumForce   = float.MaxValue,
            };
            joint.targetRotation = Quaternion.identity;

            return go;
        }

        // ── Reflection helpers ───────────────────────────────────────────────

        private float GetPhase()
        {
            object val = GetPrivateField(_legAnimator, "_phase");
            if (val == null)
            {
                throw new InvalidOperationException(
                    "LegAnimator must have a private float field named '_phase' for the gait phase accumulator.");
            }
            return (float)val;
        }

        private static object GetPrivateField(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException(
                    $"Missing expected private field '{fieldName}' on {instance.GetType().Name}.");
            }
            return field.GetValue(instance);
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException(
                    $"Missing expected private field '{fieldName}' on {instance.GetType().Name}.");
            }
            field.SetValue(instance, value);
        }

        private static Transform FindRequiredChild(Transform root, string name)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == name)
                {
                    return children[i];
                }
            }

            throw new InvalidOperationException($"Required child '{name}' not found under '{root.name}'.");
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

        // ── GAP-3: Multi-cycle spring restoration ────────────────────────────

        /// <summary>
        /// GAP-3: Verifies that leg springs are restored to the TRUE original baseline
        /// after EACH of three consecutive jump-land cycles. Guards against a subtle
        /// state-mutation bug where SetLegSpringMultiplier stores the current (already-
        /// multiplied) spring value as the new baseline, causing each successive landing
        /// to restore to only 15% of the original — compounding toward zero over cycles.
        ///
        /// Method:
        ///   1. Capture baseline spring after RagdollSetup.Awake (in Start).
        ///   2. For each of 3 cycles:
        ///      a. Enter Airborne → assert spring reduced.
        ///      b. Land → assert spring == baseline ±1 % (not the reduced value).
        /// </summary>
        [UnityTest]
        public IEnumerator MultiCycleJump_SpringRestoredToTrueBaselineAfterEachLanding()
        {
            // Wait for Start() to run so CaptureBaselineDrives has executed.
            yield return null;
            yield return new WaitForFixedUpdate();

            // Capture baseline spring value.
            float baseline = _upperLegLJoint.slerpDrive.positionSpring;
            Assert.That(baseline, Is.GreaterThan(0f),
                "Baseline spring must be positive after RagdollSetup.Awake. " +
                "Check that RagdollSetup is added before LegAnimator in SetUp.");

            const float toleranceFraction = 0.01f; // 1 % tolerance

            for (int cycle = 1; cycle <= 3; cycle++)
            {
                // ── Airborne entry ──────────────────────────────────────────
                _balance.SetGroundStateForTest(isGrounded: false, isFallen: false);
                _characterState.SetStateForTest(CharacterStateType.Airborne);
                yield return new WaitForFixedUpdate(); // CharacterState → Airborne
                yield return new WaitForFixedUpdate(); // OnStateChanged handler fires

                float airborneSpring = _upperLegLJoint.slerpDrive.positionSpring;
                Assert.That(airborneSpring, Is.LessThan(baseline),
                    $"Cycle {cycle}: spring must be reduced while Airborne. " +
                    $"Got {airborneSpring:F2}, baseline={baseline:F2}.");

                // ── Landing ─────────────────────────────────────────────────
                _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
                _characterState.SetStateForTest(CharacterStateType.Standing);
                yield return new WaitForFixedUpdate(); // CharacterState → Standing/Moving
                yield return new WaitForFixedUpdate(); // spring restore handler fires

                float landedSpring = _upperLegLJoint.slerpDrive.positionSpring;
                float toleranceAbs = baseline * toleranceFraction;

                Assert.That(landedSpring, Is.EqualTo(baseline).Within(toleranceAbs),
                    $"Cycle {cycle}: spring must be restored to ORIGINAL baseline ({baseline:F2}) " +
                    $"after landing. Got {landedSpring:F2} (tolerance ±{toleranceAbs:F2}). " +
                    "Likely cause: SetLegSpringMultiplier overwrote the baseline instead of " +
                    "preserving CaptureBaselineDrives values.");
            }
        }
    }
}
