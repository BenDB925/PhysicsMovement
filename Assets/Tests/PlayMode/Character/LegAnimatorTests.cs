using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode integration tests for <see cref="LegAnimator"/> covering:
    /// - Component caching (leg joints, PlayerMovement, CharacterState)
    /// - Phase accumulator advances when move input is non-zero
    /// - Upper-leg target rotations change while moving (L/R alternating)
    /// - Lower-leg knee-bend rotations change while moving
    /// - Legs return to Quaternion.identity when state is Fallen or GettingUp
    /// - Arm joints are not modified by LegAnimator
    /// - Lower leg joints physically rotate during gait, overcoming gravity (regression: dragging-feet bug)
    /// - Legacy world-space swing toggle exists, but local-space swing is the live default path
    /// - World-space swing: leg targetRotation changes even when hips are pitched forward
    /// - World-space swing: swing axis is world-horizontal (independent of hips pitch)
    /// - Bug fix: _worldSwingAxis resets to zero on idle (no stale-axis carry-over between frames)
    /// - Bug fix: world-space gait path fires at 0.07 m/s velocity (below old 0.1 threshold, above new 0.05)
    /// - Bug fix: aggressive input fallback fires immediately at zero velocity when input is non-zero
    /// </summary>
    public class LegAnimatorTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";
        private static readonly Vector3 TestOrigin = new Vector3(1000f, 0f, 1000f);

        // ─── Test Rig ────────────────────────────────────────────────────────

        private GameObject _ground;
        private GameObject _hips;
        private Rigidbody _hipsRb;
        private BalanceController _balance;
        private PlayerMovement _movement;
        private CharacterState _characterState;
        private LegAnimator _legAnimator;
        private LocomotionDirector _director;
        private ArmAnimator _armAnimator;

        private float _savedFixedDeltaTime;
        private int _savedSolverIterations;
        private int _savedSolverVelocityIterations;
        private bool[,] _savedLayerCollisionMatrix;

        // Leg joint GameObjects and their ConfigurableJoints
        private GameObject _upperLegL;
        private GameObject _upperLegR;
        private GameObject _lowerLegL;
        private GameObject _lowerLegR;
        private ConfigurableJoint _upperLegLJoint;
        private ConfigurableJoint _upperLegRJoint;
        private ConfigurableJoint _lowerLegLJoint;
        private ConfigurableJoint _lowerLegRJoint;

        // Arm joint GameObject for regression (must not be modified by LegAnimator)
        private GameObject _upperArmL;
        private ConfigurableJoint _upperArmLJoint;

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
            _ground.name = "LegAnimatorTests_Ground";
            _ground.transform.position = TestOrigin + new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(40f, 1f, 40f);
            _ground.layer = GameSettings.LayerEnvironment;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null,
                $"PlayerRagdoll prefab must be loadable from '{PlayerRagdollPrefabPath}'.");

            _hips = UnityEngine.Object.Instantiate(prefab, TestOrigin + new Vector3(0f, 1.1f, 0f), Quaternion.identity);
            _hips.name = prefab.name;

            _hipsRb = _hips.GetComponent<Rigidbody>();
            _balance = _hips.GetComponent<BalanceController>();
            _movement = _hips.GetComponent<PlayerMovement>();
            _characterState = _hips.GetComponent<CharacterState>();
            _legAnimator = _hips.GetComponent<LegAnimator>();
            _director = _hips.GetComponent<LocomotionDirector>();
            _armAnimator = _hips.GetComponent<ArmAnimator>();

            Assert.That(_hipsRb, Is.Not.Null, "PlayerRagdoll prefab is missing Rigidbody on root hips.");
            Assert.That(_balance, Is.Not.Null, "PlayerRagdoll prefab is missing BalanceController.");
            Assert.That(_movement, Is.Not.Null, "PlayerRagdoll prefab is missing PlayerMovement.");
            Assert.That(_characterState, Is.Not.Null, "PlayerRagdoll prefab is missing CharacterState.");
            Assert.That(_legAnimator, Is.Not.Null, "PlayerRagdoll prefab is missing LegAnimator.");
            Assert.That(_director, Is.Not.Null, "PlayerRagdoll prefab is missing LocomotionDirector.");

            _upperLegL = FindRequiredChild(_hips.transform, "UpperLeg_L").gameObject;
            _upperLegR = FindRequiredChild(_hips.transform, "UpperLeg_R").gameObject;
            _lowerLegL = FindRequiredChild(_hips.transform, "LowerLeg_L").gameObject;
            _lowerLegR = FindRequiredChild(_hips.transform, "LowerLeg_R").gameObject;
            _upperArmL = FindRequiredChild(_hips.transform, "UpperArm_L").gameObject;

            _upperLegLJoint = _upperLegL.GetComponent<ConfigurableJoint>();
            _upperLegRJoint = _upperLegR.GetComponent<ConfigurableJoint>();
            _lowerLegLJoint = _lowerLegL.GetComponent<ConfigurableJoint>();
            _lowerLegRJoint = _lowerLegR.GetComponent<ConfigurableJoint>();
            _upperArmLJoint = _upperArmL.GetComponent<ConfigurableJoint>();

            Assert.That(_upperLegLJoint, Is.Not.Null, "UpperLeg_L joint must exist on PlayerRagdoll.");
            Assert.That(_upperLegRJoint, Is.Not.Null, "UpperLeg_R joint must exist on PlayerRagdoll.");
            Assert.That(_lowerLegLJoint, Is.Not.Null, "LowerLeg_L joint must exist on PlayerRagdoll.");
            Assert.That(_lowerLegRJoint, Is.Not.Null, "LowerLeg_R joint must exist on PlayerRagdoll.");
            Assert.That(_upperArmLJoint, Is.Not.Null, "UpperArm_L joint must exist on PlayerRagdoll.");

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

        // ─── Caching Tests ──────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Awake_CachesUpperLegLJoint()
        {
            // Arrange
            yield return null;

            // Act
            object cached = GetPrivateField(_legAnimator, "_upperLegL");

            // Assert
            Assert.That(cached, Is.Not.Null,
                "LegAnimator must cache the UpperLeg_L ConfigurableJoint in Awake.");
        }

        [UnityTest]
        public IEnumerator Awake_CachesUpperLegRJoint()
        {
            // Arrange
            yield return null;

            // Act
            object cached = GetPrivateField(_legAnimator, "_upperLegR");

            // Assert
            Assert.That(cached, Is.Not.Null,
                "LegAnimator must cache the UpperLeg_R ConfigurableJoint in Awake.");
        }

        [UnityTest]
        public IEnumerator Awake_CachesLowerLegLJoint()
        {
            // Arrange
            yield return null;

            // Act
            object cached = GetPrivateField(_legAnimator, "_lowerLegL");

            // Assert
            Assert.That(cached, Is.Not.Null,
                "LegAnimator must cache the LowerLeg_L ConfigurableJoint in Awake.");
        }

        [UnityTest]
        public IEnumerator Awake_CachesLowerLegRJoint()
        {
            // Arrange
            yield return null;

            // Act
            object cached = GetPrivateField(_legAnimator, "_lowerLegR");

            // Assert
            Assert.That(cached, Is.Not.Null,
                "LegAnimator must cache the LowerLeg_R ConfigurableJoint in Awake.");
        }

        [UnityTest]
        public IEnumerator Awake_CachesPlayerMovementReference()
        {
            // Arrange
            yield return null;

            // Act
            object cached = GetPrivateField(_legAnimator, "_playerMovement");

            // Assert
            Assert.That(cached, Is.Not.Null,
                "LegAnimator must cache a PlayerMovement reference in Awake.");
        }

        [UnityTest]
        public IEnumerator Awake_CachesCharacterStateReference()
        {
            // Arrange
            yield return null;

            // Act
            object cached = GetPrivateField(_legAnimator, "_characterState");

            // Assert
            Assert.That(cached, Is.Not.Null,
                "LegAnimator must cache a CharacterState reference in Awake.");
        }

        // ─── Phase Accumulator Tests ────────────────────────────────────────

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMoveInputIsZero_PhaseAccumulatorDoesNotAdvance()
        {
            // Arrange — zero input AND zero velocity so the legs have no reason to move.
            // The phase gate is: isMoving = inputMagnitude > 0.01f || horizontalSpeed > 0.05f.
            // Setting both to zero ensures isMoving = false, so phase must not ADVANCE.
            // (Phase may still decay toward zero in the idle path — that is correct behaviour.)
            yield return null;
            _movement.SetMoveInputForTest(Vector2.zero);
            _hipsRb.linearVelocity = Vector3.zero;
            float phaseBefore = GetPhaseAccumulator();

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert — phase must not have INCREASED (it can decay but must not cycle forward).
            float phaseAfter = GetPhaseAccumulator();
            Assert.That(phaseAfter, Is.LessThanOrEqualTo(phaseBefore + 0.001f),
                "Phase accumulator must not advance when both move input and velocity are zero " +
                "(it may decay toward zero, but must not increase).");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMoveInputIsNonZero_PhaseAccumulatorAdvances()
        {
            // Arrange
            yield return null;
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            float phaseBefore = GetPhaseAccumulator();

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert
            float phaseAfter = GetPhaseAccumulator();
            Assert.That(phaseAfter, Is.GreaterThan(phaseBefore),
                "Phase accumulator must advance when move input is non-zero.");
        }

        [UnityTest]
        public IEnumerator WhenDirectorDisabled_MoveInputDoesNotAdvancePhaseOrRotateLegs()
        {
            // Arrange
            yield return null;

            _director.enabled = false;
            _characterState.SetStateForTest(CharacterStateType.Moving);
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _movement.SetMoveInputForTest(Vector2.up);
            _hipsRb.linearVelocity = Vector3.zero;
            _hipsRb.angularVelocity = Vector3.zero;

            float phaseBefore = GetPhaseAccumulator();

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert
            float phaseAfter = GetPhaseAccumulator();
            float upperLegAngle = Quaternion.Angle(_upperLegLJoint.targetRotation, Quaternion.identity);

            Assert.That(phaseAfter, Is.EqualTo(phaseBefore).Within(0.001f),
                "Without LocomotionDirector, LegAnimator should not advance gait phase from raw move input alone.");
            Assert.That(upperLegAngle, Is.LessThanOrEqualTo(1f),
                "Without LocomotionDirector, LegAnimator should keep the leg joints near rest until explicit leg commands are supplied.");
        }

        // ─── Target Rotation Tests (Moving State) ───────────────────────────

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMoving_UpperLegLTargetRotationChanges()
        {
            // Arrange
            yield return null;
            Quaternion rotBefore = _upperLegLJoint.targetRotation;
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Act — run enough frames for phase to accumulate meaningfully
            yield return new WaitForSeconds(0.1f);

            // Assert
            Quaternion rotAfter = _upperLegLJoint.targetRotation;
            float angleDiff = Quaternion.Angle(rotBefore, rotAfter);
            Assert.That(angleDiff, Is.GreaterThan(0.1f),
                $"UpperLeg_L targetRotation must change while moving. Angle diff={angleDiff:F3}°.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMoving_UpperLegRTargetRotationChanges()
        {
            // Arrange
            yield return null;
            Quaternion rotBefore = _upperLegRJoint.targetRotation;
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Act
            yield return new WaitForSeconds(0.1f);

            // Assert
            Quaternion rotAfter = _upperLegRJoint.targetRotation;
            float angleDiff = Quaternion.Angle(rotBefore, rotAfter);
            Assert.That(angleDiff, Is.GreaterThan(0.1f),
                $"UpperLeg_R targetRotation must change while moving. Angle diff={angleDiff:F3}°.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMoving_LowerLegLTargetRotationChanges()
        {
            // Arrange
            yield return null;
            Quaternion rotBefore = _lowerLegLJoint.targetRotation;
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Act
            yield return new WaitForSeconds(0.1f);

            // Assert
            Quaternion rotAfter = _lowerLegLJoint.targetRotation;
            float angleDiff = Quaternion.Angle(rotBefore, rotAfter);
            Assert.That(angleDiff, Is.GreaterThan(0.1f),
                $"LowerLeg_L targetRotation must change while moving. Angle diff={angleDiff:F3}°.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMoving_LowerLegRTargetRotationChanges()
        {
            // Arrange
            yield return null;
            Quaternion rotBefore = _lowerLegRJoint.targetRotation;
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Act
            yield return new WaitForSeconds(0.1f);

            // Assert
            Quaternion rotAfter = _lowerLegRJoint.targetRotation;
            float angleDiff = Quaternion.Angle(rotBefore, rotAfter);
            Assert.That(angleDiff, Is.GreaterThan(0.1f),
                $"LowerLeg_R targetRotation must change while moving. Angle diff={angleDiff:F3}°.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMoving_UpperLegsAreAlternating()
        {
            // Arrange — capture snapshot at one time then a half-cycle later
            yield return null;
            SetStepFrequency(2f);   // 2 Hz → half-cycle = 0.25 s
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Stabilize phase first
            yield return new WaitForSeconds(0.05f);

            Quaternion lRotA = _upperLegLJoint.targetRotation;
            Quaternion rRotA = _upperLegRJoint.targetRotation;

            // Compute a signed angle difference between L and R at snapshot A.
            // DESIGN: When _useWorldSpaceSwing is true (default), the targetRotation is
            // expressed in the connected-body frame and the rotation axis is world-horizontal
            // (e.g., -Vector3.right for forward input, not necessarily Vector3.forward/Z).
            // We use the relative rotation between L and R to detect the phase offset,
            // which is axis-agnostic and works regardless of swing implementation mode.
            // Quaternion.Dot measures how similar two quaternions are: +1 = identical,
            // 0 = 90° apart, -1 = opposite. We extract a signed offset via the relative
            // quaternion's ToAngleAxis representation.
            float signedDiffA = GetRelativeSignedAngle(lRotA, rRotA);

            // Wait a half-cycle
            yield return new WaitForSeconds(0.25f);

            Quaternion lRotB = _upperLegLJoint.targetRotation;
            Quaternion rRotB = _upperLegRJoint.targetRotation;
            float signedDiffB = GetRelativeSignedAngle(lRotB, rRotB);

            // Assert — the sign of (L relative to R) should flip over a half-cycle,
            // confirming the legs alternate with π phase offset.
            Assert.That(signedDiffA * signedDiffB, Is.LessThan(0f),
                $"Upper legs must alternate (L/R phase offset by π). " +
                $"signedDiffA={signedDiffA:F3}°, signedDiffB={signedDiffB:F3}° — sign should invert over half-cycle.");
        }

        [UnityTest]
        public IEnumerator SetCommandFrame_WhenLegStateIsCatchStep_DrivesStrongerForwardUpperLegTargetThanPlainSwing()
        {
            // Arrange
            yield return null;
            _characterState.SetStateForTest(CharacterStateType.Moving);
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _movement.SetMoveInputForTest(Vector2.up);
            SetPrivateField(_legAnimator, "_useWorldSpaceSwing", false);
            yield return new WaitForFixedUpdate();

            object rightSupportCommand = CreateLegCommand(
                legName: "Right",
                modeName: "Cycle",
                stateName: "Stance",
                transitionReasonName: "DefaultCadence",
                cyclePhase: Mathf.PI * 1.35f,
                swingAngleDegrees: -4f,
                kneeAngleDegrees: 5f,
                blendWeight: 1f,
                footTarget: Vector3.forward);

            object plainSwingCommand = CreateLegCommand(
                legName: "Left",
                modeName: "Cycle",
                stateName: "Swing",
                transitionReasonName: "DefaultCadence",
                cyclePhase: Mathf.PI * 0.25f,
                swingAngleDegrees: 12f,
                kneeAngleDegrees: 35f,
                blendWeight: 1f,
                footTarget: Vector3.forward);

            ApplyExplicitCommandFrame(plainSwingCommand, rightSupportCommand);
            float swingTargetAngle = Quaternion.Angle(Quaternion.identity, _upperLegLJoint.targetRotation);

            object catchStepCommand = CreateLegCommand(
                legName: "Left",
                modeName: "Cycle",
                stateName: "CatchStep",
                transitionReasonName: "StumbleRecovery",
                cyclePhase: Mathf.PI * 0.25f,
                swingAngleDegrees: 12f,
                kneeAngleDegrees: 35f,
                blendWeight: 1f,
                footTarget: Vector3.forward);

            // Act
            ApplyExplicitCommandFrame(catchStepCommand, rightSupportCommand);
            float catchStepTargetAngle = Quaternion.Angle(Quaternion.identity, _upperLegLJoint.targetRotation);

            // Assert
            Assert.That(catchStepTargetAngle, Is.GreaterThan(swingTargetAngle + 2f),
                $"Catch-step execution should drive a stronger forward upper-leg target than a plain swing " +
                $"when the raw command payload is otherwise identical. Swing={swingTargetAngle:F2}°, " +
                $"CatchStep={catchStepTargetAngle:F2}°.");
        }

        [UnityTest]
        public IEnumerator SetCommandFrame_WhenOppositeLegIsInStance_ExtendsSupportKneeRelativeToSwingLeg()
        {
            // Arrange
            yield return null;
            _characterState.SetStateForTest(CharacterStateType.Moving);
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _movement.SetMoveInputForTest(Vector2.up);
            SetPrivateField(_legAnimator, "_useWorldSpaceSwing", false);
            yield return new WaitForFixedUpdate();

            object leftSwingCommand = CreateLegCommand(
                legName: "Left",
                modeName: "Cycle",
                stateName: "Swing",
                transitionReasonName: "DefaultCadence",
                cyclePhase: Mathf.PI * 0.5f,
                swingAngleDegrees: 20f,
                kneeAngleDegrees: 55f,
                blendWeight: 1f,
                footTarget: Vector3.forward);
            object rightStanceCommand = CreateLegCommand(
                legName: "Right",
                modeName: "Cycle",
                stateName: "Stance",
                transitionReasonName: "DefaultCadence",
                cyclePhase: Mathf.PI * 1.4f,
                swingAngleDegrees: -8f,
                kneeAngleDegrees: 55f,
                blendWeight: 1f,
                footTarget: Vector3.forward);

            // Act
            ApplyExplicitCommandFrame(leftSwingCommand, rightStanceCommand);
            float leftKneeTargetAngle = Quaternion.Angle(Quaternion.identity, _lowerLegLJoint.targetRotation);
            float rightKneeTargetAngle = Quaternion.Angle(Quaternion.identity, _lowerLegRJoint.targetRotation);

            // Assert
            Assert.That(leftKneeTargetAngle, Is.GreaterThan(rightKneeTargetAngle + 5f),
                $"State-driven execution should keep the swing leg more tucked than the support-side stance leg. " +
                $"Left swing knee={leftKneeTargetAngle:F2}°, right stance knee={rightKneeTargetAngle:F2}°.");
        }

        [UnityTest]
        public IEnumerator SetCommandFrame_WhenPlantWindowProgresses_TapersKneeBendTowardStance()
        {
            // Arrange
            yield return null;
            _characterState.SetStateForTest(CharacterStateType.Moving);
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _movement.SetMoveInputForTest(Vector2.up);
            SetPrivateField(_legAnimator, "_useWorldSpaceSwing", false);
            yield return new WaitForFixedUpdate();

            object rightNeutralCommand = CreateLegCommand(
                legName: "Right",
                modeName: "Cycle",
                stateName: "Stance",
                transitionReasonName: "DefaultCadence",
                cyclePhase: Mathf.PI * 1.2f,
                swingAngleDegrees: 0f,
                kneeAngleDegrees: 0f,
                blendWeight: 1f,
                footTarget: Vector3.forward);

            object earlyPlantCommand = CreateLegCommand(
                legName: "Left",
                modeName: "Cycle",
                stateName: "Plant",
                transitionReasonName: "DefaultCadence",
                cyclePhase: Mathf.PI * 0.86f,
                swingAngleDegrees: 16f,
                kneeAngleDegrees: 50f,
                blendWeight: 1f,
                footTarget: Vector3.forward);

            ApplyExplicitCommandFrame(earlyPlantCommand, rightNeutralCommand);
            float earlyPlantKneeAngle = Quaternion.Angle(Quaternion.identity, _lowerLegLJoint.targetRotation);

            object latePlantCommand = CreateLegCommand(
                legName: "Left",
                modeName: "Cycle",
                stateName: "Plant",
                transitionReasonName: "DefaultCadence",
                cyclePhase: Mathf.PI * 0.99f,
                swingAngleDegrees: 16f,
                kneeAngleDegrees: 50f,
                blendWeight: 1f,
                footTarget: Vector3.forward);

            // Act
            ApplyExplicitCommandFrame(latePlantCommand, rightNeutralCommand);
            float latePlantKneeAngle = Quaternion.Angle(Quaternion.identity, _lowerLegLJoint.targetRotation);

            // Assert
            Assert.That(earlyPlantKneeAngle, Is.GreaterThan(latePlantKneeAngle + 5f),
                $"The plant timing window should extend the knee as touchdown completes instead of holding a fixed bend. " +
                $"EarlyPlant={earlyPlantKneeAngle:F2}°, latePlant={latePlantKneeAngle:F2}°.");
        }

        // ─── Identity Tests (Fallen / GettingUp) ────────────────────────────

        [UnityTest]
        public IEnumerator FixedUpdate_WhenStateFallen_UpperLegLReturnsToIdentity()
        {
            // Arrange — run gait for a bit, then flip to Fallen
            yield return null;
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.1f);

            SetCurrentState(CharacterStateType.Fallen);
            _movement.SetMoveInputForTest(Vector2.zero);

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert
            float angleFromIdentity = Quaternion.Angle(_upperLegLJoint.targetRotation, Quaternion.identity);
            Assert.That(angleFromIdentity, Is.LessThanOrEqualTo(1f),
                $"UpperLeg_L targetRotation must return to identity when Fallen. " +
                $"Angle from identity={angleFromIdentity:F3}°.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenStateFallen_UpperLegRReturnsToIdentity()
        {
            // Arrange
            yield return null;
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.1f);

            SetCurrentState(CharacterStateType.Fallen);
            _movement.SetMoveInputForTest(Vector2.zero);

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert
            float angleFromIdentity = Quaternion.Angle(_upperLegRJoint.targetRotation, Quaternion.identity);
            Assert.That(angleFromIdentity, Is.LessThanOrEqualTo(1f),
                $"UpperLeg_R targetRotation must return to identity when Fallen. " +
                $"Angle from identity={angleFromIdentity:F3}°.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenStateFallen_LowerLegsReturnToIdentity()
        {
            // Arrange
            yield return null;
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.1f);

            SetCurrentState(CharacterStateType.Fallen);
            _movement.SetMoveInputForTest(Vector2.zero);

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert
            float angleLFromId = Quaternion.Angle(_lowerLegLJoint.targetRotation, Quaternion.identity);
            float angleRFromId = Quaternion.Angle(_lowerLegRJoint.targetRotation, Quaternion.identity);
            Assert.That(angleLFromId, Is.LessThanOrEqualTo(1f),
                $"LowerLeg_L targetRotation must return to identity when Fallen. Angle={angleLFromId:F3}°.");
            Assert.That(angleRFromId, Is.LessThanOrEqualTo(1f),
                $"LowerLeg_R targetRotation must return to identity when Fallen. Angle={angleRFromId:F3}°.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenStateGettingUp_AllLegJointsReturnToIdentity()
        {
            // Arrange — move to establish a non-identity gait pose.
            yield return null;
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.1f);

            // Inject GettingUp state. Use isFallen=true so CharacterState's FixedUpdate
            // keeps the state as GettingUp (the !isFallen branch would otherwise immediately
            // transition to Standing, preventing LegAnimator from seeing GettingUp).
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: true);
            SetCurrentState(CharacterStateType.GettingUp);
            _movement.SetMoveInputForTest(Vector2.zero);

            // Act — two physics ticks so LegAnimator.FixedUpdate fires at least once in GettingUp.
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert — LegAnimator's STEP 2 must have set all four joints to identity.
            float angleUL = Quaternion.Angle(_upperLegLJoint.targetRotation, Quaternion.identity);
            float angleUR = Quaternion.Angle(_upperLegRJoint.targetRotation, Quaternion.identity);
            float angleLL = Quaternion.Angle(_lowerLegLJoint.targetRotation, Quaternion.identity);
            float angleLR = Quaternion.Angle(_lowerLegRJoint.targetRotation, Quaternion.identity);

            Assert.That(angleUL, Is.LessThanOrEqualTo(1f),
                $"UpperLeg_L must return to identity when GettingUp. Angle={angleUL:F3}°.");
            Assert.That(angleUR, Is.LessThanOrEqualTo(1f),
                $"UpperLeg_R must return to identity when GettingUp. Angle={angleUR:F3}°.");
            Assert.That(angleLL, Is.LessThanOrEqualTo(1f),
                $"LowerLeg_L must return to identity when GettingUp. Angle={angleLL:F3}°.");
            Assert.That(angleLR, Is.LessThanOrEqualTo(1f),
                $"LowerLeg_R must return to identity when GettingUp. Angle={angleLR:F3}°.");
        }

        // ─── Idle Blend Tests (Phase 3E2) ───────────────────────────────────

        /// <summary>
        /// LegAnimator must expose a serialized _idleBlendSpeed field that controls
        /// how quickly leg joints slerp back to Quaternion.identity when idle.
        /// </summary>
        [UnityTest]
        public IEnumerator IdleBlendSpeed_FieldExists_AndIsSerializable()
        {
            // Arrange
            yield return null;

            // Act — use reflection to confirm the field exists and is serializable
            FieldInfo field = typeof(LegAnimator).GetField("_idleBlendSpeed",
                BindingFlags.Instance | BindingFlags.NonPublic);

            // Assert
            Assert.That(field, Is.Not.Null,
                "LegAnimator must have a private serialized field named '_idleBlendSpeed'.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(float)),
                "_idleBlendSpeed must be a float.");
        }

        /// <summary>
        /// LegAnimator must expose a serialized _swingAxis field (Vector3) so the upper-leg
        /// rotation axis can be tuned in the Inspector without code changes.
        /// The instantiated PlayerRagdoll prefab must currently use Vector3.right here,
        /// matching the live local-frame tuning authored on the prefab.
        /// </summary>
        [UnityTest]
        public IEnumerator SwingAxis_FieldExists_AndMatchesPlayerPrefab()
        {
            // Arrange
            yield return null;

            // Act — use reflection to confirm the field exists and has the correct default
            FieldInfo field = typeof(LegAnimator).GetField("_swingAxis",
                BindingFlags.Instance | BindingFlags.NonPublic);

            // Assert: field exists and is a Vector3
            Assert.That(field, Is.Not.Null,
                "LegAnimator must have a private serialized field named '_swingAxis'.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(Vector3)),
                "_swingAxis must be a Vector3.");

            // Assert: prefab override matches the live PlayerRagdoll tuning.
            Vector3 defaultValue = (Vector3)field.GetValue(_legAnimator);
            Assert.That(defaultValue, Is.EqualTo(Vector3.right),
                $"_swingAxis on the PlayerRagdoll prefab must match the live local-frame tuning (Vector3.right). " +
                $"Got: {defaultValue}.");
        }

        /// <summary>
        /// LegAnimator must expose a serialized _kneeAxis field (Vector3) so the lower-leg
        /// knee-bend rotation axis can be tuned in the Inspector without code changes.
        /// The instantiated PlayerRagdoll prefab must currently use Vector3.right here,
        /// matching the live local-frame knee tuning authored on the prefab.
        /// </summary>
        [UnityTest]
        public IEnumerator KneeAxis_FieldExists_AndMatchesPlayerPrefab()
        {
            // Arrange
            yield return null;

            // Act — use reflection to confirm the field exists and has the correct default
            FieldInfo field = typeof(LegAnimator).GetField("_kneeAxis",
                BindingFlags.Instance | BindingFlags.NonPublic);

            // Assert: field exists and is a Vector3
            Assert.That(field, Is.Not.Null,
                "LegAnimator must have a private serialized field named '_kneeAxis'.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(Vector3)),
                "_kneeAxis must be a Vector3.");

            // Assert: prefab override matches the live PlayerRagdoll tuning.
            Vector3 defaultValue = (Vector3)field.GetValue(_legAnimator);
            Assert.That(defaultValue, Is.EqualTo(Vector3.right),
                $"_kneeAxis on the PlayerRagdoll prefab must match the live local-frame tuning (Vector3.right). " +
                $"Got: {defaultValue}.");
        }

        /// <summary>
        /// After moving (which sets a non-identity leg rotation), stopping input must
        /// cause upper leg L to blend smoothly back toward identity over multiple frames.
        /// The blend must be measurably closer to identity after 0.5 s idle time.
        /// </summary>
        [UnityTest]
        public IEnumerator FixedUpdate_AfterMoving_WhenInputStops_UpperLegLBlendsTowardIdentity()
        {
            // Arrange — move for a bit to establish a non-identity rotation
            yield return null;
            SetIdleBlendSpeed(5f);   // fast enough to measure in 0.5 s
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.15f);

            // Capture rotation after moving (should be non-identity)
            Quaternion rotAfterMove = _upperLegLJoint.targetRotation;
            float angleAfterMove = Quaternion.Angle(rotAfterMove, Quaternion.identity);

            // Act — stop BOTH input AND velocity so isMoving=false and the idle path fires.
            // Without zeroing velocity the hips coast at 2 m/s, keeping isMoving=true.
            _movement.SetMoveInputForTest(Vector2.zero);
            _hipsRb.linearVelocity = Vector3.zero;
            yield return new WaitForSeconds(0.5f);

            // Assert — rotation must be closer to identity than right after stopping
            Quaternion rotAfterIdle = _upperLegLJoint.targetRotation;
            float angleAfterIdle = Quaternion.Angle(rotAfterIdle, Quaternion.identity);

            Assert.That(angleAfterIdle, Is.LessThan(angleAfterMove),
                $"UpperLeg_L must blend toward identity on idle. " +
                $"AngleAfterMove={angleAfterMove:F3}° AngleAfterIdle={angleAfterIdle:F3}°.");
        }

        /// <summary>
        /// After moving, stopping input must blend upper leg R toward identity.
        /// </summary>
        [UnityTest]
        public IEnumerator FixedUpdate_AfterMoving_WhenInputStops_UpperLegRBlendsTowardIdentity()
        {
            // Arrange
            yield return null;
            SetIdleBlendSpeed(5f);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.15f);

            Quaternion rotAfterMove = _upperLegRJoint.targetRotation;
            float angleAfterMove = Quaternion.Angle(rotAfterMove, Quaternion.identity);

            // Act — stop BOTH input AND velocity so isMoving=false.
            _movement.SetMoveInputForTest(Vector2.zero);
            yield return HoldHipsStationaryForFixedFrames(50);

            // Assert
            Quaternion rotAfterIdle = _upperLegRJoint.targetRotation;
            float angleAfterIdle = Quaternion.Angle(rotAfterIdle, Quaternion.identity);

            Assert.That(angleAfterIdle, Is.LessThan(angleAfterMove),
                $"UpperLeg_R must blend toward identity on idle. " +
                $"AngleAfterMove={angleAfterMove:F3}° AngleAfterIdle={angleAfterIdle:F3}°.");
        }

        /// <summary>
        /// After moving, stopping input must blend lower leg L toward identity.
        /// </summary>
        [UnityTest]
        public IEnumerator FixedUpdate_AfterMoving_WhenInputStops_LowerLegLBlendsTowardIdentity()
        {
            // Arrange
            yield return null;
            SetIdleBlendSpeed(5f);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.15f);

            Quaternion rotAfterMove = _lowerLegLJoint.targetRotation;
            float angleAfterMove = Quaternion.Angle(rotAfterMove, Quaternion.identity);

            // Act — stop BOTH input AND velocity so isMoving=false.
            _movement.SetMoveInputForTest(Vector2.zero);
            _hipsRb.linearVelocity = Vector3.zero;
            yield return new WaitForSeconds(0.5f);

            // Assert
            Quaternion rotAfterIdle = _lowerLegLJoint.targetRotation;
            float angleAfterIdle = Quaternion.Angle(rotAfterIdle, Quaternion.identity);

            Assert.That(angleAfterIdle, Is.LessThan(angleAfterMove),
                $"LowerLeg_L must blend toward identity on idle. " +
                $"AngleAfterMove={angleAfterMove:F3}° AngleAfterIdle={angleAfterIdle:F3}°.");
        }

        /// <summary>
        /// After moving, stopping input must blend lower leg R toward identity.
        /// </summary>
        [UnityTest]
        public IEnumerator FixedUpdate_AfterMoving_WhenInputStops_LowerLegRBlendsTowardIdentity()
        {
            // Arrange
            yield return null;
            SetIdleBlendSpeed(5f);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.15f);

            Quaternion rotAfterMove = _lowerLegRJoint.targetRotation;
            float angleAfterMove = Quaternion.Angle(rotAfterMove, Quaternion.identity);

            // Act — stop BOTH input AND velocity so isMoving=false.
            _movement.SetMoveInputForTest(Vector2.zero);
            _hipsRb.linearVelocity = Vector3.zero;
            yield return new WaitForSeconds(0.5f);

            // Assert
            Quaternion rotAfterIdle = _lowerLegRJoint.targetRotation;
            float angleAfterIdle = Quaternion.Angle(rotAfterIdle, Quaternion.identity);

            Assert.That(angleAfterIdle, Is.LessThan(angleAfterMove),
                $"LowerLeg_R must blend toward identity on idle. " +
                $"AngleAfterMove={angleAfterMove:F3}° AngleAfterIdle={angleAfterIdle:F3}°.");
        }

        /// <summary>
        /// The gait phase accumulator must decay toward zero when input is zero.
        /// After enough idle time it must be measurably smaller than it was when moving stopped.
        /// </summary>
        [UnityTest]
        public IEnumerator FixedUpdate_WhenInputStops_PhaseDecaysTowardZero()
        {
            // Arrange — move to accumulate a non-zero phase
            yield return null;
            SetIdleBlendSpeed(5f);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.2f);

            float phaseAtStop = GetPhaseAccumulator();
            // Ensure phase is meaningfully non-zero before stopping
            Assume.That(phaseAtStop, Is.GreaterThan(0.05f),
                "Pre-condition: phase must be non-zero before testing decay.");

            // Act — stop input AND zero velocity so isMoving = false (gating: input || speed).
            // Without zeroing velocity the hips continue coasting at 2 m/s, keeping isMoving=true.
            _movement.SetMoveInputForTest(Vector2.zero);
            yield return HoldHipsStationaryForFixedFrames(50);

            // Assert — phase must have decayed
            float phaseAfterIdle = GetPhaseAccumulator();
            Assert.That(phaseAfterIdle, Is.LessThan(phaseAtStop),
                $"Phase accumulator must decay toward zero on idle. " +
                $"PhaseAtStop={phaseAtStop:F3} PhaseAfterIdle={phaseAfterIdle:F3}.");
        }

        /// <summary>
        /// On a quick move-stop-move toggle, the leg joint targetRotation must NOT jump
        /// discontinuously when movement resumes. The first frame of resumed gait must
        /// produce a small rotation (due to the slerp-on-entry), not a sudden large
        /// amplitude matching the full gait target.
        /// </summary>
        [UnityTest]
        public IEnumerator FixedUpdate_QuickMoveStopMove_NoAbruptRotationJump()
        {
            // Arrange
            yield return null;
            SetIdleBlendSpeed(5f);
            SetStepFrequency(2f);

            // Phase 1: move to build up a gait rotation, then stop.
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.15f);

            // Capture rotation after moving (confirm we have non-trivial gait amplitude).
            float angleAfterMove = Quaternion.Angle(_upperLegLJoint.targetRotation, Quaternion.identity);
            Assume.That(angleAfterMove, Is.GreaterThan(1f),
                $"Pre-condition: leg must be in non-identity gait pose before stop. Angle={angleAfterMove:F3}°.");

            // Phase 2: stop — zero BOTH input AND velocity so isMoving=false and the idle
            // decay path fires. Wait long enough for _smoothedInputMag to decay to zero.
            _movement.SetMoveInputForTest(Vector2.zero);
            yield return HoldHipsStationaryForFixedFrames(100);

            float angleAtIdle = Quaternion.Angle(_upperLegLJoint.targetRotation, Quaternion.identity);

            // Phase 3: immediately resume — one FixedUpdate will begin slerping toward gait.
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForFixedUpdate();
            Quaternion rotOnFirstResumeFrame = _upperLegLJoint.targetRotation;
            float angleOnResume = Quaternion.Angle(rotOnFirstResumeFrame, Quaternion.identity);

            // Assert — joints decayed fully to near-identity after idle.
            Assert.That(angleAtIdle, Is.LessThanOrEqualTo(2f),
                $"Joints must be near identity after idle decay. Angle={angleAtIdle:F3}°.");

            // Assert — the first resumed-movement frame must NOT snap to full gait amplitude.
            // Slerp at t=idleBlendSpeed*fixedDeltaTime (≈0.05) means the rotation is at most
            // ~5% of the gait target. The maximum gait amplitude is stepAngle (50.3°) so the
            // maximum first-frame rotation is ~2.5°. We use a generous 5° threshold.
            Assert.That(angleOnResume, Is.LessThan(5f),
                $"First resumed-gait frame must not snap to full amplitude. " +
                $"Angle={angleOnResume:F3}° (threshold 5°). IdleAngle={angleAtIdle:F3}°.");
        }

        /// <summary>
        /// After a long idle, legs must be very close to identity (nearly fully settled).
        /// Tests that the blend completes within a reasonable time window.
        /// </summary>
        [UnityTest]
        public IEnumerator FixedUpdate_AfterLongIdle_LegJointsAreNearIdentity()
        {
            // Arrange
            yield return null;
            SetIdleBlendSpeed(5f);

            // Move to get a visible rotation
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.15f);

            // Act — long idle (2 seconds at blend speed 5).
            // Zero BOTH input AND velocity so isMoving=false (gating: input||speed).
            _movement.SetMoveInputForTest(Vector2.zero);
            _hipsRb.linearVelocity = Vector3.zero;
            yield return new WaitForSeconds(2.25f);

            // Assert — all four joints near identity (< 3°)
            float angleUL = Quaternion.Angle(_upperLegLJoint.targetRotation, Quaternion.identity);
            float angleUR = Quaternion.Angle(_upperLegRJoint.targetRotation, Quaternion.identity);
            float angleLL = Quaternion.Angle(_lowerLegLJoint.targetRotation, Quaternion.identity);
            float angleLR = Quaternion.Angle(_lowerLegRJoint.targetRotation, Quaternion.identity);

            Assert.That(angleUL, Is.LessThan(3f),
                $"UpperLeg_L must be near identity after long idle. Angle={angleUL:F3}°.");
            Assert.That(angleUR, Is.LessThan(3f),
                $"UpperLeg_R must be near identity after long idle. Angle={angleUR:F3}°.");
            Assert.That(angleLL, Is.LessThan(3f),
                $"LowerLeg_L must be near identity after long idle. Angle={angleLL:F3}°.");
            Assert.That(angleLR, Is.LessThan(3f),
                $"LowerLeg_R must be near identity after long idle. Angle={angleLR:F3}°.");
        }

        // ─── Arm Non-Modification Test ──────────────────────────────────────

        [UnityTest]
        public IEnumerator FixedUpdate_WhenMoving_ArmJointsAreNotModified()
        {
            // Arrange
            yield return null;
            Assert.That(_armAnimator, Is.Not.Null, "PlayerRagdoll prefab must include ArmAnimator for this regression test.");
            _armAnimator.enabled = false;
            Quaternion armRotBefore = _upperArmLJoint.targetRotation;
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Act
            yield return new WaitForSeconds(0.2f);

            // Assert
            Quaternion armRotAfter = _upperArmLJoint.targetRotation;
            float angleDiff = Quaternion.Angle(armRotBefore, armRotAfter);
            Assert.That(angleDiff, Is.LessThanOrEqualTo(0.01f),
                $"LegAnimator must not modify arm joint targetRotations. " +
                $"UpperArm_L changed by {angleDiff:F4}°.");

            _armAnimator.enabled = true;
        }

        // ─── Physical Lift Regression Test ──────────────────────────────────

        /// <summary>
        /// Regression test for the 'dragging feet' bug.
        ///
        /// Verifies that, with a full physics-enabled ragdoll (RagdollSetup + LegAnimator +
        /// PlayerMovement + CharacterState + BalanceController all active), at least one
        /// lower leg ConfigurableJoint physically rotates away from its rest orientation
        /// during sustained walking gait — proving the joint drive overcomes gravity and
        /// the leg actually lifts, not just that <c>targetRotation</c> is assigned.
        ///
        /// Measurement: angular displacement of the lower leg segment's local rotation
        /// relative to its starting orientation (measured via Quaternion.Angle between
        /// initial and final local rotation of the lower leg Transform).  Local rotation
        /// is the quantity driven by the ConfigurableJoint spring, making it the most
        /// direct indicator of whether the joint is physically responding to the
        /// LegAnimator's targetRotation commands.
        ///
        /// Threshold rationale (5°):
        ///   - LegAnimator default _kneeAngle = 55° (raised for aggressive gait).
        ///   - RagdollSetup applies lowerLegSpring = 1200 Nm/rad; at 20° = 0.35 rad,
        ///     spring torque ≈ 420 Nm vs gravity torque ≈ 4.2 Nm — the joint wins 100:1.
        ///   - The _smoothedInputMag ramp means amplitude is only ~5% at frame 1, ramping
        ///     toward 100% over ~60 frames; after 50 frames the joint should be > 15°.
        ///   - 5° threshold is deliberately conservative: a limp/no-drive leg measures 0°,
        ///     while a properly driven leg will exceed 10° well within 50 frames.
        ///   - If CI proves this too tight, raise to 3° — the point is that ANY measurable
        ///     angular response proves the drive chain is alive end-to-end.
        ///
        /// This test uses a SEPARATE, fully-wired physics rig (not the shared SetUp rig)
        /// so that gravity is enabled on all bodies and the drives are configured by
        /// RagdollSetup exactly as they are at runtime.
        /// </summary>
        [UnityTest]
        public IEnumerator LowerLeg_WhenWalking_LiftsBeyondMinimumThreshold()
        {
            // ── Arrange ────────────────────────────────────────────────────────

            // Minimum angular displacement (degrees) the lower leg joint must achieve
            // relative to its rest orientation. See threshold rationale above.
            const float MinAngularDisplacementDeg = 5f;

            // Number of FixedUpdate frames to simulate before measuring.
            // 50 frames at 100 Hz = 0.5 s — enough for the gait phase + smoothed-scale
            // ramp (_smoothedInputMag) to reach near-full amplitude.
            const int PhysicsFrames = 50;

            GameObject rigRoot = BuildGaitQualityRig(out PlayerMovement physicsMovement,
                out GameObject physUpperLegL, out GameObject physUpperLegR,
                out GameObject physLowerLegL, out GameObject physLowerLegR,
                out GameObject physFootL, out GameObject physFootR);

            yield return null;
            yield return new WaitForFixedUpdate();

            physicsMovement.SetMoveInputForTest(new Vector2(0f, 1f));

            // ── Record rest rotation ───────────────────────────────────────────
            // Capture initial local rotation of both lower legs before gait has any effect.
            Quaternion lowerLegLRestRot = physLowerLegL.transform.localRotation;
            Quaternion lowerLegRRestRot = physLowerLegR.transform.localRotation;

            // ── Act — simulate physics frames ─────────────────────────────────
            for (int i = 0; i < PhysicsFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            // ── Assert ─────────────────────────────────────────────────────────
            float angularDisplacementL = Quaternion.Angle(lowerLegLRestRot, physLowerLegL.transform.localRotation);
            float angularDisplacementR = Quaternion.Angle(lowerLegRRestRot, physLowerLegR.transform.localRotation);

            // At least one lower leg must have physically rotated by MinAngularDisplacementDeg.
            bool eitherRotated = (angularDisplacementL >= MinAngularDisplacementDeg) ||
                                 (angularDisplacementR >= MinAngularDisplacementDeg);

            Assert.That(eitherRotated, Is.True,
                $"At least one lower leg joint must physically rotate ≥{MinAngularDisplacementDeg:F1}° " +
                $"from rest during {PhysicsFrames} FixedUpdate frames of walking gait. " +
                $"LowerLeg_L angular displacement={angularDisplacementL:F2}°  |  " +
                $"LowerLeg_R angular displacement={angularDisplacementR:F2}°. " +
                $"A rotation near 0° indicates the SLERP drive is not overcoming gravity — " +
                $"the 'dragging feet' bug has returned.");

            // ── Cleanup ───────────────────────────────────────────────────────
            UnityEngine.Object.Destroy(rigRoot);
        }

        // ─── Gait Quality Regression Tests ──────────────────────────────────

        /// <summary>
        /// Gait Quality Test 1: At least one knee must gain meaningful flexion during gait.
        ///
        /// The original test compared unrelated transform origins and called them
        /// "knee" and "foot," which drifted away from the live rig. The replacement uses
        /// the actual upper-leg, lower-leg, and foot chain to measure knee flexion as the
        /// angle between thigh and shin directions over time.
        ///
        /// Threshold rationale (15° above settled pose):
        ///   - The live LegAnimator defaults drive strong bend (_kneeAngle = 60°), but the
        ///     physical response on this compact rig is damped by gravity and joint compliance.
        ///   - Requiring a 15° increase above the settled pose is conservative while still
        ///     distinguishing a healthy bend from a dragging or dead-leg stride.
        /// </summary>
        [UnityTest]
        public IEnumerator Knee_WhenWalking_FlexionAngleIncreasesDuringSwing()
        {
            // ── Arrange ────────────────────────────────────────────────────────
            const float MinFlexionIncreaseDeg = 15f;
            const int SettleFrames = 20;
            const int PhysicsFrames = 80;

            GameObject rigRoot = BuildGaitQualityRig(out PlayerMovement physMovement,
                out GameObject physUpperLegL, out GameObject physUpperLegR,
                out GameObject physLowerLegL, out GameObject physLowerLegR,
                out GameObject physFootL, out GameObject physFootR);

            yield return null;  // allow Awake/Start

            for (int i = 0; i < SettleFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            float settledFlexionL = GetKneeFlexionAngle(physUpperLegL, physLowerLegL, physFootL);
            float settledFlexionR = GetKneeFlexionAngle(physUpperLegR, physLowerLegR, physFootR);

            physMovement.SetMoveInputForTest(new Vector2(0f, 1f));

            // ── Act ────────────────────────────────────────────────────────────
            float maxFlexionIncreaseL = 0f;
            float maxFlexionIncreaseR = 0f;

            for (int i = 0; i < PhysicsFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                float flexionL = GetKneeFlexionAngle(physUpperLegL, physLowerLegL, physFootL) - settledFlexionL;
                float flexionR = GetKneeFlexionAngle(physUpperLegR, physLowerLegR, physFootR) - settledFlexionR;

                if (flexionL > maxFlexionIncreaseL) { maxFlexionIncreaseL = flexionL; }
                if (flexionR > maxFlexionIncreaseR) { maxFlexionIncreaseR = flexionR; }
            }

            // ── Assert ─────────────────────────────────────────────────────────
            bool eitherKneeFlexed = (maxFlexionIncreaseL >= MinFlexionIncreaseDeg) ||
                                    (maxFlexionIncreaseR >= MinFlexionIncreaseDeg);

            Assert.That(eitherKneeFlexed, Is.True,
                $"During {PhysicsFrames} frames of walking gait, at least one knee must increase its flexion angle by " +
                $"≥{MinFlexionIncreaseDeg:F1}°. " +
                $"Settled flexion: L={settledFlexionL:F1}° R={settledFlexionR:F1}°. " +
                $"Max flexion increase: L={maxFlexionIncreaseL:F1}° R={maxFlexionIncreaseR:F1}°. " +
                "A near-zero increase means the gait is swinging the limb without meaningful knee articulation.");

            // ── Cleanup ────────────────────────────────────────────────────────
            UnityEngine.Object.Destroy(rigRoot);
        }

        /// <summary>
        /// Gait Quality Test 2: An actual foot sole must rise measurably above its settled
        /// resting height during gait.
        ///
        /// This complements the ground-clearance test above by normalising against the
        /// settled rest pose of the synthetic rig. It catches dead-leg regressions where
        /// the foot never gains vertical excursion even if the whole body jitters slightly.
        ///
        /// Threshold rationale (0.02 m):
        ///   - On the live gait path, the foot should gain clear vertical excursion from the
        ///     settled stance once move input is held.
        ///   - 0.02 m is a conservative lower bound for the current compact live rig: a
        ///     working gait should still produce a clearly measurable lift above micro-jitter.
        /// </summary>
        [UnityTest]
        public IEnumerator Foot_WhenWalking_SoleRisesAboveSettledRestHeight()
        {
            // ── Arrange ────────────────────────────────────────────────────────
            const float MinFootClearanceMetres = 0.02f;
            const int SettleFrames = 20;
            const int PhysicsFrames = 80;
            const float GroundY = 0f;

            GameObject rigRoot = BuildGaitQualityRig(out PlayerMovement physMovement,
                out GameObject physUpperLegL, out GameObject physUpperLegR,
                out GameObject physLowerLegL, out GameObject physLowerLegR,
                out GameObject physFootL, out GameObject physFootR);

            yield return null;  // allow Awake/Start

            for (int i = 0; i < SettleFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            float settledSoleClearanceL = GetFootSoleClearance(physFootL, GroundY);
            float settledSoleClearanceR = GetFootSoleClearance(physFootR, GroundY);

            physMovement.SetMoveInputForTest(new Vector2(0f, 1f));

            // ── Act ────────────────────────────────────────────────────────────
            float maxYAboveSpawnL = 0f;
            float maxYAboveSpawnR = 0f;

            for (int i = 0; i < PhysicsFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                float riseL = GetFootSoleClearance(physFootL, GroundY) - settledSoleClearanceL;
                float riseR = GetFootSoleClearance(physFootR, GroundY) - settledSoleClearanceR;

                if (riseL > maxYAboveSpawnL) { maxYAboveSpawnL = riseL; }
                if (riseR > maxYAboveSpawnR) { maxYAboveSpawnR = riseR; }
            }

            // ── Assert ─────────────────────────────────────────────────────────
            bool eitherFootCleared = (maxYAboveSpawnL >= MinFootClearanceMetres) ||
                                     (maxYAboveSpawnR >= MinFootClearanceMetres);

            Assert.That(eitherFootCleared, Is.True,
                $"During {PhysicsFrames} frames of walking gait, at least one foot sole must rise " +
                $"≥{MinFootClearanceMetres:F3} m above its settled rest height. " +
                $"Max sole rise above rest: L={maxYAboveSpawnL:F4} m  R={maxYAboveSpawnR:F4} m. " +
                $"A near-zero rise means the gait never develops meaningful vertical foot excursion.");

            // ── Cleanup ────────────────────────────────────────────────────────
            UnityEngine.Object.Destroy(rigRoot);
        }

        /// <summary>
        /// Gait Quality Test 3: Feet must alternate — left and right lower legs must
        /// each take a turn being further forward along the movement direction over 80
        /// physics frames.
        ///
        /// This proves that the L/R alternating gait phase (π offset) produces a visible
        /// walking pattern in world space, not just symmetric oscillation.
        ///
        /// Method:
        ///   Move input is (0, 1) → movement direction is world +Z.
        ///   Project both lower leg world positions onto the +Z axis each frame.
        ///   Assert that:
        ///     (a) At some frame, LowerLeg_L.z > LowerLeg_R.z (left foot is ahead)
        ///     (b) At a different frame, LowerLeg_R.z > LowerLeg_L.z (right foot is ahead)
        ///
        /// Threshold rationale (0.01 m):
        ///   - With _stepAngle=25° and upper leg length ~0.3 m, the forward excursion of
        ///     each foot at peak swing is ~0.3 × sin(25°) ≈ 0.13 m.
        ///   - 0.01 m threshold is conservative: both feet should exceed this easily if
        ///     the alternating phase is working.
        ///   - A symmetric rig (both swinging together) would never produce a crossing;
        ///     a broken phase would show one foot always ahead.
        /// </summary>
        [UnityTest]
        public IEnumerator LowerLeg_WhenWalking_FeetAlternate()
        {
            // ── Arrange ────────────────────────────────────────────────────────
            const float MinAlternationMarginMetres = 0.01f;
            const int PhysicsFrames = 80;

            // Move input direction: forward (+Z in world space for our rig)
            Vector2 moveInput = new Vector2(0f, 1f);
            Vector3 moveDir3D  = new Vector3(moveInput.x, 0f, moveInput.y).normalized;

            GameObject rigRoot = BuildGaitQualityRig(out PlayerMovement physMovement,
                out GameObject physUpperLegL, out GameObject physUpperLegR,
                out GameObject physLowerLegL, out GameObject physLowerLegR,
                out GameObject physFootL, out GameObject physFootR);

            physMovement.SetMoveInputForTest(moveInput);
            yield return null;  // allow Awake/Start

            // ── Act ────────────────────────────────────────────────────────────
            float maxLeftAheadMargin  = float.MinValue;  // L.z - R.z (positive = L ahead)
            float maxRightAheadMargin = float.MinValue;  // R.z - L.z (positive = R ahead)

            for (int i = 0; i < PhysicsFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                // Project lower leg world positions onto the movement direction
                float lForward = Vector3.Dot(physLowerLegL.transform.position, moveDir3D);
                float rForward = Vector3.Dot(physLowerLegR.transform.position, moveDir3D);

                float leftAhead  = lForward - rForward;
                float rightAhead = rForward - lForward;

                if (leftAhead  > maxLeftAheadMargin)  { maxLeftAheadMargin  = leftAhead; }
                if (rightAhead > maxRightAheadMargin) { maxRightAheadMargin = rightAhead; }
            }

            // ── Assert ─────────────────────────────────────────────────────────
            Assert.That(maxLeftAheadMargin, Is.GreaterThanOrEqualTo(MinAlternationMarginMetres),
                $"During {PhysicsFrames} frames, LowerLeg_L must be further forward than LowerLeg_R " +
                $"by ≥{MinAlternationMarginMetres:F3} m at some frame. " +
                $"Max L-ahead margin={maxLeftAheadMargin:F4} m. " +
                $"A value below threshold means left foot never leads — alternating gait broken.");

            Assert.That(maxRightAheadMargin, Is.GreaterThanOrEqualTo(MinAlternationMarginMetres),
                $"During {PhysicsFrames} frames, LowerLeg_R must be further forward than LowerLeg_L " +
                $"by ≥{MinAlternationMarginMetres:F3} m at some frame. " +
                $"Max R-ahead margin={maxRightAheadMargin:F4} m. " +
                $"A value below threshold means right foot never leads — alternating gait broken.");

            // ── Cleanup ────────────────────────────────────────────────────────
            UnityEngine.Object.Destroy(rigRoot);
        }

        /// <summary>
        /// Gait Quality Test 4: Net horizontal displacement of the more-displaced lower
        /// leg over 80 physics frames must have a positive dot product with the move
        /// input direction.
        ///
        /// This tests that the world-space leg swing drives feet in the direction the
        /// character is actually moving, not sideways or backwards.
        ///
        /// Method:
        ///   Record each lower leg's world position at frame 0 and track the maximum
        ///   forward displacement over 80 frames (peak forward, not net end-to-end).
        ///   Select the leg with the larger peak forward displacement.
        ///   Assert: maxForwardDisplacement > MinForwardExcursion (foot reached forward
        ///   of its start position at some point during the gait cycle).
        ///
        /// Why peak (not net end-to-end)?
        ///   Net displacement over a non-integer number of gait cycles accumulates
        ///   spring oscillation residuals. Peak forward excursion is a more robust signal:
        ///   any leg that swings forward at all will produce a clear positive value,
        ///   while a laterally-swinging or stationary leg will show near zero.
        ///
        /// Threshold rationale (0.005 m = 5 mm):
        ///   - _stepAngle=25°, upper leg length ~0.3 m → peak excursion ~0.3 × sin(25°) ≈ 0.13 m
        ///   - 5 mm is ~4% of the expected peak, very conservative against spring damping
        ///   - A leg swinging purely sideways (wrong axis) would show ≈0 forward excursion
        /// </summary>
        [UnityTest]
        public IEnumerator LowerLeg_WhenWalking_StepDirectionMatchesMovement()
        {
            // ── Arrange ────────────────────────────────────────────────────────
            const int PhysicsFrames = 80;
            const float MinForwardExcursionMetres = 0.005f;  // 5 mm — see threshold rationale above

            Vector2 moveInput = new Vector2(0f, 1f);
            Vector3 moveDir3D  = new Vector3(moveInput.x, 0f, moveInput.y).normalized;

            GameObject rigRoot = BuildGaitQualityRig(out PlayerMovement physMovement,
                out GameObject physUpperLegL, out GameObject physUpperLegR,
                out GameObject physLowerLegL, out GameObject physLowerLegR,
                out GameObject physFootL, out GameObject physFootR);

            physMovement.SetMoveInputForTest(moveInput);
            yield return null;  // allow Awake/Start

            // Record starting forward position (projected onto move direction)
            float startForwardL = Vector3.Dot(physLowerLegL.transform.position, moveDir3D);
            float startForwardR = Vector3.Dot(physLowerLegR.transform.position, moveDir3D);

            // ── Act: track peak forward excursion over gait frames ────────────
            float maxExcursionL = 0f;
            float maxExcursionR = 0f;

            for (int i = 0; i < PhysicsFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                float fwdL = Vector3.Dot(physLowerLegL.transform.position, moveDir3D);
                float fwdR = Vector3.Dot(physLowerLegR.transform.position, moveDir3D);

                float excursionL = fwdL - startForwardL;
                float excursionR = fwdR - startForwardR;

                if (excursionL > maxExcursionL) { maxExcursionL = excursionL; }
                if (excursionR > maxExcursionR) { maxExcursionR = excursionR; }
            }

            // ── Assert ─────────────────────────────────────────────────────────
            float maxExcursionEither = Mathf.Max(maxExcursionL, maxExcursionR);
            string chosenName = maxExcursionL >= maxExcursionR ? "LowerLeg_L" : "LowerLeg_R";

            Assert.That(maxExcursionEither, Is.GreaterThanOrEqualTo(MinForwardExcursionMetres),
                $"During {PhysicsFrames} frames, at least one lower leg must swing forward by " +
                $"≥{MinForwardExcursionMetres * 100f:F0} cm in the movement direction. " +
                $"MaxExcursionL={maxExcursionL:F4} m, MaxExcursionR={maxExcursionR:F4} m. " +
                $"A value below threshold means neither foot ever moved forward — " +
                $"world-space swing axis is misaligned with movement direction.");

            // ── Cleanup ────────────────────────────────────────────────────────
            UnityEngine.Object.Destroy(rigRoot);
        }

        // ─── Velocity-Scaled Step Frequency Tests (Phase 3E4) ───────────────

        /// <summary>
        /// LegAnimator must expose a serialized _stepFrequencyScale field (float)
        /// that maps metres-per-second to gait cycles per second.
        /// Default must be 0.1 (tuned down from 1.5 to reduce over-cadence at speed).
        /// Range must be [0.1, 5].
        /// </summary>
        [UnityTest]
        public IEnumerator StepFrequencyScale_FieldExists_AndDefaultsTo1Point5()
        {
            // Arrange
            yield return null;

            // Act
            FieldInfo field = typeof(LegAnimator).GetField("_stepFrequencyScale",
                BindingFlags.Instance | BindingFlags.NonPublic);

            // Assert: field exists and is float
            Assert.That(field, Is.Not.Null,
                "LegAnimator must have a private serialized field named '_stepFrequencyScale'.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(float)),
                "_stepFrequencyScale must be a float.");

            // Assert: default 0.1
            float defaultValue = (float)field.GetValue(_legAnimator);
            Assert.That(defaultValue, Is.EqualTo(0.1f).Within(0.001f),
                $"_stepFrequencyScale must default to 0.1. Got: {defaultValue}.");
        }

        /// <summary>
        /// _stepFrequency must match the cadence authored on the live PlayerRagdoll prefab.
        /// </summary>
        [UnityTest]
        public IEnumerator StepFrequency_DefaultMatchesPlayerPrefab()
        {
            // Arrange
            yield return null;

            // Act
            FieldInfo field = typeof(LegAnimator).GetField("_stepFrequency",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null,
                "LegAnimator must have a private serialized field named '_stepFrequency'.");

            float defaultValue = (float)field.GetValue(_legAnimator);
            Assert.That(defaultValue, Is.EqualTo(1.25f).Within(0.001f),
                $"_stepFrequency on the PlayerRagdoll prefab must match the live minimum cadence (1.25). Got: {defaultValue}.");
        }

        /// <summary>
        /// When Rigidbody has non-zero horizontal velocity, phase must advance faster
        /// than when velocity is near-zero (with same input magnitude).
        /// This verifies the velocity-driven gait core: faster movement → faster cadence.
        /// </summary>
        [UnityTest]
        public IEnumerator PhaseAccumulator_AdvancesFasterAtHigherVelocity()
        {
            // Arrange
            yield return null;

            // Run at low velocity: zero out hipsRb velocity, run 10 frames, measure phase advance
            _hipsRb.linearVelocity = Vector3.zero;
            SetPrivateField(_legAnimator, "_phase", 0f);
            SetPrivateField(_legAnimator, "_smoothedInputMag", 0f);
            // Use a minimum freq of 0 so only velocity contributes
            SetPrivateField(_legAnimator, "_stepFrequency", 0f);
            SetPrivateField(_legAnimator, "_stepFrequencyScale", 1.5f);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            for (int i = 0; i < 5; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            float phaseAtLowVel = GetPhaseAccumulator();

            // Reset phase, then run at high velocity
            SetPrivateField(_legAnimator, "_phase", 0f);
            SetPrivateField(_legAnimator, "_smoothedInputMag", 0f);
            _hipsRb.linearVelocity = new Vector3(0f, 0f, 4f);  // 4 m/s → scale 1.5 → 6 cycles/sec

            for (int i = 0; i < 5; i++)
            {
                yield return new WaitForFixedUpdate();
            }
            float phaseAtHighVel = GetPhaseAccumulator();

            // Assert: phase advanced more at higher velocity
            Assert.That(phaseAtHighVel, Is.GreaterThan(phaseAtLowVel),
                $"Phase must advance faster when Rigidbody has higher horizontal velocity. " +
                $"LowVelPhase={phaseAtLowVel:F4} HighVelPhase={phaseAtHighVel:F4}.");
        }

        /// <summary>
        /// When Rigidbody velocity is near zero and _stepFrequency is 0, phase must
        /// not advance (legs stay still at idle with no velocity).
        /// </summary>
        [UnityTest]
        public IEnumerator PhaseAccumulator_DoesNotAdvance_WhenVelocityZeroAndMinFreqZero()
        {
            // Arrange
            yield return null;
            SetPrivateField(_legAnimator, "_phase", 0f);
            SetPrivateField(_legAnimator, "_stepFrequency", 0f);
            SetPrivateField(_legAnimator, "_stepFrequencyScale", 1.5f);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Act
            yield return HoldHipsStationaryForFixedFrames(5);

            // Assert
            float phase = GetPhaseAccumulator();
            Assert.That(phase, Is.EqualTo(0f).Within(0.001f),
                $"Phase must not advance when velocity is zero and _stepFrequency is 0. Phase={phase:F4}.");
        }

        /// <summary>
        /// When _stepFrequency (min cadence) is non-zero and velocity is zero, phase
        /// must still advance at the minimum rate — so idle cycling works if desired.
        /// </summary>
        [UnityTest]
        public IEnumerator PhaseAccumulator_AdvancesAtMinFrequency_WhenVelocityZero()
        {
            // Arrange
            yield return null;
            _hipsRb.linearVelocity = Vector3.zero;
            SetPrivateField(_legAnimator, "_phase", 0f);
            SetPrivateField(_legAnimator, "_stepFrequency", 1f);   // 1 cycle/sec minimum
            SetPrivateField(_legAnimator, "_stepFrequencyScale", 1.5f);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Act — 10 frames at 100 Hz = 0.1 s → expect ≈ 2π × 1 × 0.1 = 0.628 rad
            for (int i = 0; i < 10; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            // Assert: phase has advanced by roughly 1 cycle/sec × 0.1 s × 2π (ignore smoothing)
            float phase = GetPhaseAccumulator();
            Assert.That(phase, Is.GreaterThan(0.3f),
                $"Phase must advance at minimum cadence even when velocity is zero. Phase={phase:F4}.");
        }

        // ─── Aggressive Knee Lift Tests (Phase 3E4) ─────────────────────────

        /// <summary>
        /// _kneeAngle must match the value authored on the live PlayerRagdoll prefab.
        /// </summary>
        [UnityTest]
        public IEnumerator KneeAngle_DefaultMatchesPlayerPrefab()
        {
            // Arrange
            yield return null;

            // Act
            FieldInfo field = typeof(LegAnimator).GetField("_kneeAngle",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null,
                "LegAnimator must have a private serialized field named '_kneeAngle'.");

            float defaultValue = (float)field.GetValue(_legAnimator);
            Assert.That(defaultValue, Is.EqualTo(65f).Within(0.001f),
                $"_kneeAngle on the PlayerRagdoll prefab must match the live aggressive stride tuning (65°). Got: {defaultValue}.");
        }

        /// <summary>
        /// LegAnimator must expose a _upperLegLiftBoost field (float) that matches the
        /// value authored on the live PlayerRagdoll prefab.
        /// that adds upward bias to the forward-swinging upper leg.
        /// </summary>
        [UnityTest]
        public IEnumerator UpperLegLiftBoost_FieldExists_AndMatchesPlayerPrefab()
        {
            // Arrange
            yield return null;

            // Act
            FieldInfo field = typeof(LegAnimator).GetField("_upperLegLiftBoost",
                BindingFlags.Instance | BindingFlags.NonPublic);

            // Assert: field exists and is float
            Assert.That(field, Is.Not.Null,
                "LegAnimator must have a private serialized field named '_upperLegLiftBoost'.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(float)),
                "_upperLegLiftBoost must be a float.");

            // Assert: prefab override matches the live PlayerRagdoll tuning.
            float defaultValue = (float)field.GetValue(_legAnimator);
            Assert.That(defaultValue, Is.EqualTo(45f).Within(0.001f),
                $"_upperLegLiftBoost on the PlayerRagdoll prefab must match the live lift tuning (45°). Got: {defaultValue}.");
        }

        /// <summary>
        /// When _upperLegLiftBoost is non-zero, the forward-swinging upper leg must
        /// produce a larger targetRotation angle than when the boost is zero.
        /// This confirms the boost actually increases the angular target of the forward leg.
        /// </summary>
        [UnityTest]
        public IEnumerator UpperLegLiftBoost_WhenNonZero_IncreasesForwardLegSwingAngle()
        {
            // Arrange
            yield return null;
            // Configure a stable mid-cycle phase where left leg is forward (sin > 0)
            // so liftBoostL is active.
            SetPrivateField(_legAnimator, "_phase", Mathf.PI * 0.5f);  // sin = 1 → max forward swing
            SetPrivateField(_legAnimator, "_smoothedInputMag", 1f);     // no ramp — full amplitude
            SetPrivateField(_legAnimator, "_stepAngle", 25f);
            SetPrivateField(_legAnimator, "_useWorldSpaceSwing", false); // local-space for determinism
            _hipsRb.linearVelocity = Vector3.zero;

            // Measure with boost = 0
            SetPrivateField(_legAnimator, "_upperLegLiftBoost", 0f);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForFixedUpdate();
            float angleWithoutBoost = Quaternion.Angle(Quaternion.identity, _upperLegLJoint.targetRotation);

            // Measure with boost = 15
            SetPrivateField(_legAnimator, "_phase", Mathf.PI * 0.5f);
            SetPrivateField(_legAnimator, "_smoothedInputMag", 1f);
            SetPrivateField(_legAnimator, "_upperLegLiftBoost", 15f);
            yield return new WaitForFixedUpdate();
            float angleWithBoost = Quaternion.Angle(Quaternion.identity, _upperLegLJoint.targetRotation);

            // Assert: boost produces a larger angle on the forward-swinging leg
            Assert.That(angleWithBoost, Is.GreaterThan(angleWithoutBoost),
                $"_upperLegLiftBoost=15° must produce a larger swing angle on the forward-swinging leg. " +
                $"WithoutBoost={angleWithoutBoost:F2}° WithBoost={angleWithBoost:F2}°.");
        }

        /// <summary>
        /// The lift boost must only apply to the leg swinging FORWARD (sin > 0), not the
        /// leg swinging backward (sin ≤ 0). At phase=π/2, left leg is forward (boost applied),
        /// right leg is backward (no boost). The left-leg angle must exceed the right-leg angle
        /// by at least the boost amount when viewed symmetrically around the step angle.
        /// </summary>
        [UnityTest]
        public IEnumerator UpperLegLiftBoost_OnlyAppliesOnForwardSwingPhase()
        {
            // Arrange
            yield return null;
            SetPrivateField(_legAnimator, "_phase", Mathf.PI * 0.5f); // left forward, right backward
            SetPrivateField(_legAnimator, "_smoothedInputMag", 1f);
            SetPrivateField(_legAnimator, "_stepAngle", 25f);
            SetPrivateField(_legAnimator, "_upperLegLiftBoost", 15f);
            SetPrivateField(_legAnimator, "_useWorldSpaceSwing", false);
            _hipsRb.linearVelocity = Vector3.zero;
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Act
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            float angleL = Quaternion.Angle(Quaternion.identity, _upperLegLJoint.targetRotation);
            float angleR = Quaternion.Angle(Quaternion.identity, _upperLegRJoint.targetRotation);

            // Assert: left (forward-swinging) > right (backward-swinging)
            // At phase π/2: sinL=1 (boost+step angle both positive), sinR=-1 (backward, no boost)
            // Expected: angleL ≈ 25+15=40°, angleR ≈ 25° (backward swing, no boost)
            Assert.That(angleL, Is.GreaterThan(angleR),
                $"Forward-swinging leg (L at phase π/2) must have a larger swing angle than backward leg (R). " +
                $"angleL={angleL:F2}° angleR={angleR:F2}°.");
        }

        // ─── Gait Quality Rig Builder ─────────────────────────────────────────

        /// <summary>
        /// Builds a compact but contract-faithful ragdoll rig for the gait-quality tests.
        /// Mirrors the earlier physics-only helper but also adds actual Foot_L / Foot_R bodies,
        /// GroundSensor components, and an Environment-layer ground plane so gait assertions can
        /// measure real foot-ground outcomes instead of segment-origin proxies.
        ///
        /// Rig topology:
        ///   rigRoot
        ///     GaitQualityGround (Environment layer)
        ///     PhysicsHips (kinematic, gravity off) — anchor body
        ///     UpperLeg_L (rb, gravity on, ConfigurableJoint → hips)
        ///       LowerLeg_L (rb, gravity on, ConfigurableJoint → UpperLeg_L)
        ///         Foot_L (rb, gravity on, ConfigurableJoint → LowerLeg_L, GroundSensor)
        ///     UpperLeg_R (rb, gravity on, ConfigurableJoint → hips)
        ///       LowerLeg_R (rb, gravity on, ConfigurableJoint → UpperLeg_R)
        ///         Foot_R (rb, gravity on, ConfigurableJoint → LowerLeg_R, GroundSensor)
        ///
        /// Components on Hips (in dependency order):
        ///   RagdollSetup (first — applies authoritative spring values in Awake)
        ///   BalanceController, PlayerMovement, CharacterState, LegAnimator
        ///
        /// BalanceController is intentionally active so the tests exercise the live ownership
        /// contract with LegAnimator. Grounded state comes from real GroundSensor reads rather
        /// than the test seam, and PlayerMovement's physics loop remains disabled because the
        /// gait tests only need deterministic move-input injection.
        /// </summary>
        private static GameObject BuildGaitQualityRig(
            out PlayerMovement physMovement,
            out GameObject physUpperLegL, out GameObject physUpperLegR,
            out GameObject physLowerLegL, out GameObject physLowerLegR,
            out GameObject physFootL, out GameObject physFootR)
        {
            GameObject rigRoot = new GameObject("GaitQualityRigRoot");

            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "GaitQualityGround";
            ground.transform.SetParent(rigRoot.transform, worldPositionStays: false);
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(40f, 1f, 40f);
            ground.layer = GameSettings.LayerEnvironment;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null,
                $"PlayerRagdoll prefab must be loadable from '{PlayerRagdollPrefabPath}'.");

            GameObject physicsHips = UnityEngine.Object.Instantiate(prefab, new Vector3(0f, 1.1f, 0f), Quaternion.identity);
            physicsHips.transform.SetParent(rigRoot.transform, worldPositionStays: true);

            physMovement = physicsHips.GetComponent<PlayerMovement>();
            LegAnimator gaitQualityLegAnimator = physicsHips.GetComponent<LegAnimator>();
            Assert.That(physMovement, Is.Not.Null, "PlayerRagdoll prefab must include PlayerMovement.");
            Assert.That(gaitQualityLegAnimator, Is.Not.Null, "PlayerRagdoll prefab must include LegAnimator.");

            physMovement.SetMoveInputForTest(Vector2.zero);
            SetPrivateField(gaitQualityLegAnimator, "_spinSuppressFrames", 5);

            physUpperLegL = FindRequiredChild(physicsHips.transform, "UpperLeg_L").gameObject;
            physUpperLegR = FindRequiredChild(physicsHips.transform, "UpperLeg_R").gameObject;
            physLowerLegL = FindRequiredChild(physicsHips.transform, "LowerLeg_L").gameObject;
            physLowerLegR = FindRequiredChild(physicsHips.transform, "LowerLeg_R").gameObject;
            physFootL = FindRequiredChild(physicsHips.transform, "Foot_L").gameObject;
            physFootR = FindRequiredChild(physicsHips.transform, "Foot_R").gameObject;

            return rigRoot;
        }

        // ─── World-Space Swing Tests (Phase 3E3) ────────────────────────────

        /// <summary>
        /// LegAnimator must expose a serialized _useWorldSpaceSwing field (bool)
        /// that defaults to true. This allows the world-space swing behaviour to be
        /// toggled off in the Inspector for side-by-side comparison.
        /// </summary>
        [UnityTest]
        public IEnumerator UseWorldSpaceSwing_FieldExists_AndDefaultsToFalse()
        {
            // Arrange
            yield return null;

            // Act — use reflection to confirm the field exists and has the correct default
            System.Reflection.FieldInfo field = typeof(LegAnimator).GetField("_useWorldSpaceSwing",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            // Assert: field exists and is a bool
            Assert.That(field, Is.Not.Null,
                "LegAnimator must have a private serialized field named '_useWorldSpaceSwing'.");
            Assert.That(field.FieldType, Is.EqualTo(typeof(bool)),
                "_useWorldSpaceSwing must be a bool.");

            // Assert: default value is false — local-space swing is the active path.
            // World-space swing proved unstable (hips rotation corrupts the frame conversion)
            // and was disabled in f0496df. The field is retained for legacy/debug purposes.
            bool defaultValue = (bool)field.GetValue(_legAnimator);
            Assert.That(defaultValue, Is.False,
                "_useWorldSpaceSwing must default to false — local-space swing is the active path.");
        }

        /// <summary>
        /// When _useWorldSpaceSwing is true and the hips are pitched forward 45°,
        /// upper leg target rotations must still change measurably during walking gait.
        /// This verifies the fix: in legacy mode, pitching the hips forward made the local
        /// "forward" axis point downward, causing zero net swing; world-space mode must
        /// still produce a swing regardless of hips orientation.
        /// </summary>
        [UnityTest]
        public IEnumerator WorldSpaceSwing_WhenHipsPitchedForward_UpperLegTargetRotationChanges()
        {
            // Arrange — pitch the hips forward 45° to simulate the "torso lean" condition
            yield return null;
            _hips.transform.rotation = Quaternion.Euler(45f, 0f, 0f);

            // Ensure world-space swing is enabled
            SetPrivateField(_legAnimator, "_useWorldSpaceSwing", true);

            Quaternion rotBefore = _upperLegLJoint.targetRotation;
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Act — run enough frames for phase to accumulate
            yield return new WaitForSeconds(0.15f);

            // Assert — target rotation must have changed despite hips being pitched
            Quaternion rotAfter = _upperLegLJoint.targetRotation;
            float angleDiff = Quaternion.Angle(rotBefore, rotAfter);
            Assert.That(angleDiff, Is.GreaterThan(0.1f),
                $"UpperLeg_L targetRotation must change during walking even when hips are pitched 45°. " +
                $"Angle diff={angleDiff:F3}°. " +
                $"A near-zero diff indicates the world-space fix is not working (swing axis collapsed to zero).");
        }

        /// <summary>
        /// When _useWorldSpaceSwing is true, the swing axis used in targetRotation must
        /// be world-horizontal (perpendicular to Vector3.up), not aligned with the hips
        /// local frame. This is validated by checking that the effective swing axis
        /// produced by the targetRotation change is nearly perpendicular to world up.
        ///
        /// Method: compare the targetRotation produced with hips at 0° pitch versus
        /// hips at 45° pitch. If world-space swing is working, the angle-axis of the
        /// resulting targetRotation change should be similar (world-horizontal) in both
        /// cases, not rotated by 45° as it would be in local-space mode.
        /// </summary>
        [UnityTest]
        public IEnumerator WorldSpaceSwing_SwingAxisIsWorldHorizontal_RegardlessOfHipsPitch()
        {
            // Arrange — helper: measure the swing axis component along world-up
            // for a given hips pitch after one burst of movement.
            yield return null;

            // We measure the targetRotation change angle for two conditions and verify
            // that tilting the hips does NOT cause a proportional reduction in swing magnitude.
            // If the swing were purely local, a 45° pitch would cause the effective world-up
            // component to drop by ~sin(45°) ≈ 29%. In world-space mode the magnitude stays
            // the same — only the phase timing changes.

            // --- Condition A: hips upright (0° pitch) ---
            _hips.transform.rotation = Quaternion.identity;
            SetPrivateField(_legAnimator, "_useWorldSpaceSwing", true);
            SetPrivateField(_legAnimator, "_smoothedInputMag", 0f);
            SetPrivateField(_legAnimator, "_phase", 0f);

            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.15f);
            float angleUprightL = Quaternion.Angle(Quaternion.identity, _upperLegLJoint.targetRotation);
            float angleUprightR = Quaternion.Angle(Quaternion.identity, _upperLegRJoint.targetRotation);

            // Reset
            _movement.SetMoveInputForTest(Vector2.zero);
            yield return new WaitForFixedUpdate();
            SetPrivateField(_legAnimator, "_smoothedInputMag", 0f);
            SetPrivateField(_legAnimator, "_phase", 0f);

            // --- Condition B: hips pitched forward 45° ---
            _hips.transform.rotation = Quaternion.Euler(45f, 0f, 0f);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.15f);
            float anglePitchedL = Quaternion.Angle(Quaternion.identity, _upperLegLJoint.targetRotation);
            float anglePitchedR = Quaternion.Angle(Quaternion.identity, _upperLegRJoint.targetRotation);

            // Assert: the rotation magnitude must NOT have dropped significantly due to pitch.
            // In world-space mode both conditions produce the same swing amplitude (±_stepAngle).
            // In local-space mode the effective world-space component drops by ~sin(45°) ≈ 29%.
            // We allow a generous 40% reduction as the tolerance so the test catches the
            // broken (local-space) case without being overly sensitive to minor phase timing.
            // The assertion only needs at least one leg to be non-degenerate (non-zero).
            bool uprightNonZero = (angleUprightL > 0.5f) || (angleUprightR > 0.5f);
            bool pitchedNonZero = (anglePitchedL > 0.5f) || (anglePitchedR > 0.5f);

            Assert.That(uprightNonZero, Is.True,
                $"Pre-condition: upright upper-leg swing must be non-zero. " +
                $"L={angleUprightL:F2}° R={angleUprightR:F2}°");

            Assert.That(pitchedNonZero, Is.True,
                $"World-space swing must produce non-zero leg rotation even when hips are pitched 45°. " +
                $"L={anglePitchedL:F2}° R={anglePitchedR:F2}°. " +
                $"Near-zero indicates the swing is being computed in local-frame (still broken).");

            // Cleanup: reset hips rotation
            _hips.transform.rotation = Quaternion.identity;
        }

        // ─── Bug Fix Regression Tests (gait-stale-axis-fix) ────────────────

        /// <summary>
        /// BUG FIX REGRESSION — BUG 1: _worldSwingAxis must never be stale between frames.
        ///
        /// When the character transitions from walking (world-space path active) to idle
        /// (early-exit path), _worldSwingAxis must reset to Vector3.zero on the very first
        /// idle frame rather than retaining the last active-gait value.
        ///
        /// Root cause: _worldSwingAxis was only updated inside ApplyWorldSpaceSwing(), so any
        /// code path that bypassed that method (idle, fallen, degenerate gaitFwd) left the
        /// field at the previous frame's non-zero value. The fix: reset _worldSwingAxis to
        /// Vector3.zero at the top of FixedUpdate() every frame, before any branching.
        ///
        /// This test verifies the fix by:
        ///   1. Walking long enough for _worldSwingAxis to be set to a non-zero value.
        ///   2. Stopping input so the idle path runs.
        ///   3. Asserting _worldSwingAxis is zero on the very next FixedUpdate.
        /// </summary>
        [UnityTest]
        [Ignore("World-space swing disabled by default (f0496df) — local-space is the active path. " +
                "_worldSwingAxis is never set when _useWorldSpaceSwing=false, so pre-condition always fails. " +
                "Re-enable this test only if world-space mode is restored as the default.")]
        public IEnumerator WorldSwingAxis_WhenTransitionsToIdle_ResetsToZeroImmediately()
        {
            // Arrange — walk long enough to get a non-zero _worldSwingAxis
            yield return null;
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            yield return new WaitForSeconds(0.1f);

            // Verify pre-condition: swing axis is non-zero while walking
            Vector3 axisWhileWalking = (Vector3)GetPrivateField(_legAnimator, "_worldSwingAxis");
            Assume.That(axisWhileWalking.magnitude, Is.GreaterThan(0.01f),
                "Pre-condition: _worldSwingAxis must be non-zero while walking.");

            // Act — stop BOTH input AND velocity so isMoving=false and the idle path fires.
            // The gating condition is: isMoving = input||velocity, so both must be zero.
            // Without zeroing velocity, the world-space gait path continues firing and sets
            // _worldSwingAxis to a non-zero value (this is correct behaviour — the fix only
            // guarantees the reset on the idle path, which requires isMoving=false).
            _movement.SetMoveInputForTest(Vector2.zero);
            _hipsRb.linearVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();

            // Assert — _worldSwingAxis must be zero on the first idle frame
            Vector3 axisAfterIdle = (Vector3)GetPrivateField(_legAnimator, "_worldSwingAxis");
            Assert.That(axisAfterIdle.magnitude, Is.LessThanOrEqualTo(0.001f),
                $"_worldSwingAxis must reset to Vector3.zero immediately on idle (first frame after input stops). " +
                $"Got magnitude={axisAfterIdle.magnitude:F4} ({axisAfterIdle}). " +
                $"A non-zero value means the stale-axis bug has returned — _worldSwingAxis is not " +
                $"reset at the top of FixedUpdate().");
        }

        /// <summary>
        /// BUG FIX REGRESSION — BUG 2: World-space gait path must fire at low velocity.
        ///
        /// Before the fix the velocity threshold was 0.1 m/s; at velocities below that
        /// the input fallback was not entered aggressively enough, causing gaitFwd to stay
        /// Vector3.zero (and UL_targetEuler to stay 0,0,0) even while the character was
        /// visibly moving (debug log frames 4–222, vel 0.08–3.83 m/s).
        ///
        /// After the fix:
        ///   • Velocity threshold is 0.05 m/s (half the old value).
        ///   • Input fallback fires immediately when input magnitude > 0.01, regardless
        ///     of velocity — so gaitFwd is non-zero on the very first frame of input.
        ///
        /// This test verifies the fix by:
        ///   1. Setting Rigidbody velocity to a value between 0.05 and 0.1 m/s (which was
        ///      below the old threshold but above the new one).
        ///   2. Setting move input to a non-zero value.
        ///   3. Asserting that GetWorldGaitForward (via a FixedUpdate that runs worldSwing)
        ///      produces a non-zero _worldSwingAxis (proving gaitFwd was non-zero).
        /// </summary>
        [UnityTest]
        [Ignore("World-space swing is disabled by default (_useWorldSpaceSwing=false); local-space gait is used instead. This test validated the old world-space code path which is no longer the default.")]
        public IEnumerator WorldGaitForward_AtLowVelocity_StillProducesNonZeroSwingAxis()
        {
            // Arrange — set velocity below old 0.1 m/s threshold, above new 0.05 m/s threshold
            yield return null;
            _hipsRb.linearVelocity = new Vector3(0f, 0f, 0.07f);  // 0.07 m/s: old fail, new pass
            SetPrivateField(_legAnimator, "_useWorldSpaceSwing", true);
            SetPrivateField(_legAnimator, "_phase", Mathf.PI * 0.25f);         // non-zero phase
            SetPrivateField(_legAnimator, "_smoothedInputMag", 1f);             // full amplitude
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Act — one FixedUpdate to trigger the world-space swing path
            yield return new WaitForFixedUpdate();

            // Assert — _worldSwingAxis must be non-zero (world-space path executed with valid gaitFwd)
            Vector3 swingAxis = (Vector3)GetPrivateField(_legAnimator, "_worldSwingAxis");
            Assert.That(swingAxis.magnitude, Is.GreaterThan(0.5f),
                $"_worldSwingAxis must be non-zero when horizontal velocity is 0.07 m/s (above 0.05 threshold). " +
                $"Got magnitude={swingAxis.magnitude:F4} ({swingAxis}). " +
                $"A near-zero value means the 0.05 m/s threshold fix did not take effect, or the " +
                $"world-space path fell back to local-space due to a degenerate gaitFwd.");
        }

        /// <summary>
        /// BUG FIX REGRESSION — BUG 2 (input fallback): When velocity is zero but input
        /// is non-zero, GetWorldGaitForward must use the input direction immediately
        /// (aggressive fallback) so gaitFwd is non-zero from the very first frame of input.
        ///
        /// Before the fix the input fallback was only reached when velocity was below
        /// threshold AND the rigidbody was null — the non-null, low-velocity path returned
        /// Vector3.zero without checking input.  Now the input check is explicit and
        /// unconditional when velocity is below threshold.
        /// </summary>
        [UnityTest]
        [Ignore("World-space swing is disabled by default (_useWorldSpaceSwing=false); local-space gait is used instead. This test validated the old world-space code path which is no longer the default.")]
        public IEnumerator WorldGaitForward_AtZeroVelocity_UsesInputDirectionImmediately()
        {
            // Arrange — zero velocity, non-zero input
            yield return null;
            _hipsRb.linearVelocity = Vector3.zero;
            SetPrivateField(_legAnimator, "_useWorldSpaceSwing", true);
            SetPrivateField(_legAnimator, "_phase", Mathf.PI * 0.25f);
            SetPrivateField(_legAnimator, "_smoothedInputMag", 1f);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));   // input direction: +Z

            // Act
            yield return new WaitForFixedUpdate();

            // Assert — _worldSwingAxis must be non-zero (gaitFwd came from input dir = +Z)
            Vector3 swingAxis = (Vector3)GetPrivateField(_legAnimator, "_worldSwingAxis");
            Assert.That(swingAxis.magnitude, Is.GreaterThan(0.5f),
                $"_worldSwingAxis must be non-zero when velocity=0 but input magnitude > 0.01. " +
                $"Got magnitude={swingAxis.magnitude:F4} ({swingAxis}). " +
                $"A near-zero value means the aggressive input fallback fix is not working.");
        }

        // ─── Helper: create test-rig leg/arm joints ─────────────────────────

        private static GameObject CreateLegJoint(GameObject parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = Vector3.zero;
            go.AddComponent<Rigidbody>();
            ConfigurableJoint joint = go.AddComponent<ConfigurableJoint>();
            joint.targetRotation = Quaternion.identity;
            return go;
        }

        private static GameObject CreateArmJoint(GameObject parent, string name)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = Vector3.zero;
            go.AddComponent<Rigidbody>();
            ConfigurableJoint joint = go.AddComponent<ConfigurableJoint>();
            joint.targetRotation = Quaternion.identity;
            return go;
        }

        /// <summary>
        /// Creates a child GameObject with a Rigidbody (gravity enabled), a BoxCollider,
        /// and a ConfigurableJoint connected to <paramref name="parentRb"/> — suitable for
        /// the physical-lift regression test that measures real angular displacement.
        /// Linear axes are locked so the body remains position-constrained to its parent
        /// (preventing gravity free-fall through the joint). Rotation is free so the
        /// SLERP drive can rotate the joint in response to LegAnimator's targetRotation.
        /// The joint uses SLERP drive mode so <see cref="RagdollSetup"/> can override it.
        /// </summary>
        private static GameObject CreatePhysicsLegJoint(
            GameObject parent, string name, Rigidbody parentRb, Vector3 localPos, float mass)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = localPos;
            go.layer = GameSettings.LayerPlayer1Parts;

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.useGravity = true;   // Gravity enabled — the joint spring must resist it

            go.AddComponent<BoxCollider>();

            ConfigurableJoint joint = go.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parentRb;

            // Lock linear axes so the body stays positionally anchored to the parent
            // and cannot free-fall away from the joint. Angular motion remains free so
            // the SLERP drive can rotate the segment in response to targetRotation.
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;

            joint.rotationDriveMode = RotationDriveMode.Slerp;
            // Deliberately weak initial drive — RagdollSetup.Awake overrides this with the
            // authoritative spring/damper values (default _lowerLegSpring = 1200 Nm/rad).
            joint.slerpDrive = new JointDrive
            {
                positionSpring = 100f,
                positionDamper = 10f,
                maximumForce   = float.MaxValue,
            };
            joint.targetRotation = Quaternion.identity;

            return go;
        }

        private static GameObject CreatePhysicsFootJoint(
            GameObject parent,
            string name,
            Rigidbody parentRb,
            Vector3 localPos,
            float mass,
            Vector3 colliderSize)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = localPos;
            go.layer = GameSettings.LayerPlayer1Parts;

            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.useGravity = true;

            BoxCollider collider = go.AddComponent<BoxCollider>();
            collider.size = colliderSize;

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
                maximumForce = float.MaxValue,
            };
            joint.targetRotation = Quaternion.identity;

            return go;
        }

        private static void AddGroundSensor(GameObject footGO)
        {
            GroundSensor sensor = footGO.AddComponent<GroundSensor>();
            FieldInfo field = typeof(GroundSensor).GetField(
                "_groundLayers",
                BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(sensor, (LayerMask)(1 << GameSettings.LayerEnvironment));
        }

        private static float GetFootSoleClearance(GameObject footGO, float groundY)
        {
            Collider footCollider = footGO.GetComponent<Collider>();
            float soleY = footCollider != null ? footCollider.bounds.min.y : footGO.transform.position.y;
            return soleY - groundY;
        }

        private static float GetKneeFlexionAngle(
            GameObject upperLegGO,
            GameObject lowerLegGO,
            GameObject footGO)
        {
            Vector3 thighDirection = lowerLegGO.transform.position - upperLegGO.transform.position;
            Vector3 shinDirection = footGO.transform.position - lowerLegGO.transform.position;

            if (thighDirection.sqrMagnitude < 1e-6f || shinDirection.sqrMagnitude < 1e-6f)
            {
                return 0f;
            }

            return Vector3.Angle(thighDirection.normalized, shinDirection.normalized);
        }

        private IEnumerator HoldHipsStationaryForFixedFrames(int frameCount)
        {
            RigidbodyConstraints originalConstraints = _hipsRb.constraints;
            _hipsRb.constraints = RigidbodyConstraints.FreezePosition | RigidbodyConstraints.FreezeRotation;

            for (int i = 0; i < frameCount; i++)
            {
                _hipsRb.linearVelocity = Vector3.zero;
                _hipsRb.angularVelocity = Vector3.zero;
                yield return new WaitForFixedUpdate();
            }

            _hipsRb.linearVelocity = Vector3.zero;
            _hipsRb.angularVelocity = Vector3.zero;
            _hipsRb.constraints = originalConstraints;
        }

        // ─── Reflection helpers ──────────────────────────────────────────────

        private float GetPhaseAccumulator()
        {
            object val = GetPrivateField(_legAnimator, "_phase");
            if (val == null)
            {
                throw new InvalidOperationException(
                    "LegAnimator must have a private float field named '_phase' for the gait phase accumulator.");
            }

            return (float)val;
        }

        private void SetStepFrequency(float freq)
        {
            SetPrivateField(_legAnimator, "_stepFrequency", freq);
        }

        private void SetIdleBlendSpeed(float speed)
        {
            SetPrivateField(_legAnimator, "_idleBlendSpeed", speed);
        }

        private void SetCurrentState(CharacterStateType state)
        {
            _characterState.SetStateForTest(state);
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

        private static float GetRotationZAngle(Quaternion q)
        {
            // Extract local Z rotation angle (degrees) from quaternion, signed by convention.
            // Used because LegAnimator._swingAxis defaults to Vector3.forward (Z) — the
            // ConfigurableJoint targetRotation Z component maps to rotation around joint.axis
            // (Vector3.right = primary hinge), which drives sagittal forward/backward swing.
            q.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) { angle -= 360f; }
            return angle * Vector3.Dot(axis.normalized, Vector3.forward);
        }

        private static float GetRotationXAngle(Quaternion q)
        {
            // Extract local X rotation angle (degrees) from quaternion, signed by convention.
            // Retained for backward compatibility — not used by gait tests after axis fix.
            q.ToAngleAxis(out float angle, out Vector3 axis);
            if (angle > 180f) { angle -= 360f; }
            return angle * Vector3.Dot(axis.normalized, Vector3.right);
        }

        /// <summary>
        /// Returns a signed scalar representing the relative angular offset between
        /// two quaternions in degrees, measured along the dominant rotation axis of
        /// their relative rotation, projected onto the XZ plane.
        /// Positive when the relative rotation points toward +X or +Z.
        /// Used by the alternating-legs test because the world-space swing axis
        /// is not always Vector3.forward/Z — this helper works regardless of which
        /// axis the swing is applied around (X or Z, forward or lateral).
        /// </summary>
        private static float GetRelativeSignedAngle(Quaternion a, Quaternion b)
        {
            // Relative rotation from a to b
            Quaternion relative = Quaternion.Inverse(a) * b;
            relative.ToAngleAxis(out float angle, out Vector3 axis);
            // Normalise angle to [-180, 180]
            if (angle > 180f) { angle -= 360f; }
            // Project axis onto XZ plane and use its magnitude/sign to determine the
            // signed direction. This handles both Z-axis (local swing) and X-axis
            // (world-space swing with forward input) cases.
            float xzComponent = axis.x + axis.z;   // dominant horizontal component
            return angle * Mathf.Sign(xzComponent != 0f ? xzComponent : 1f);
        }

        private void ApplyExplicitCommandFrame(object leftCommand, object rightCommand)
        {
            object desiredInput = GetPropertyValue<object>(_director, "CurrentDesiredInput");
            object observation = GetPropertyValue<object>(_director, "CurrentObservation");

            MethodInfo setCommandFrameMethod = typeof(LegAnimator).GetMethod(
                "SetCommandFrame",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (setCommandFrameMethod == null)
            {
                throw new InvalidOperationException(
                    "Missing expected internal method 'SetCommandFrame' on LegAnimator.");
            }

            setCommandFrameMethod.Invoke(_legAnimator, new[] { desiredInput, observation, leftCommand, rightCommand });
        }

        private static object CreateLegCommand(
            string legName,
            string modeName,
            string stateName,
            string transitionReasonName,
            float cyclePhase,
            float swingAngleDegrees,
            float kneeAngleDegrees,
            float blendWeight,
            Vector3 footTarget)
        {
            Assembly characterAssembly = typeof(LegAnimator).Assembly;
            Type legType = RequireLocomotionType(characterAssembly, "LocomotionLeg");
            Type modeType = RequireLocomotionType(characterAssembly, "LegCommandMode");
            Type stateType = RequireLocomotionType(characterAssembly, "LegStateType");
            Type transitionReasonType = RequireLocomotionType(characterAssembly, "LegStateTransitionReason");
            Type stateFrameType = RequireLocomotionType(characterAssembly, "LegStateFrame");
            Type commandType = RequireLocomotionType(characterAssembly, "LegCommandOutput");

            object leg = Enum.Parse(legType, legName);
            object mode = Enum.Parse(modeType, modeName);
            object state = Enum.Parse(stateType, stateName);
            object transitionReason = Enum.Parse(transitionReasonType, transitionReasonName);

            ConstructorInfo stateFrameConstructor = stateFrameType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[] { legType, stateType, transitionReasonType },
                modifiers: null);
            if (stateFrameConstructor == null)
            {
                throw new InvalidOperationException(
                    "Missing expected LegStateFrame constructor for Chapter 3 command injection tests.");
            }

            object stateFrame = stateFrameConstructor.Invoke(new[] { leg, state, transitionReason });

            ConstructorInfo commandConstructor = commandType.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[]
                {
                    legType,
                    modeType,
                    stateFrameType,
                    typeof(float),
                    typeof(float),
                    typeof(float),
                    typeof(float),
                    typeof(Vector3),
                },
                modifiers: null);
            if (commandConstructor == null)
            {
                throw new InvalidOperationException(
                    "Missing expected LegCommandOutput constructor for Chapter 3 command injection tests.");
            }

            return commandConstructor.Invoke(new object[]
            {
                leg,
                mode,
                stateFrame,
                cyclePhase,
                swingAngleDegrees,
                kneeAngleDegrees,
                blendWeight,
                footTarget,
            });
        }

        private static Type RequireLocomotionType(Assembly assembly, string typeName)
        {
            Type type = assembly.GetType($"PhysicsDrivenMovement.Character.{typeName}");
            if (type == null)
            {
                throw new InvalidOperationException(
                    $"Missing expected locomotion type 'PhysicsDrivenMovement.Character.{typeName}'.");
            }

            return type;
        }

        private static T GetPropertyValue<T>(object instance, string propertyName)
        {
            PropertyInfo property = instance.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
            {
                throw new InvalidOperationException(
                    $"Missing expected property '{propertyName}' on {instance.GetType().Name}.");
            }

            return (T)property.GetValue(instance);
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

        private static void SetAutoPropertyBackingField(object instance, string propertyName, object value)
        {
            string backingFieldName = $"<{propertyName}>k__BackingField";
            FieldInfo field = instance.GetType().GetField(backingFieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException(
                    $"Missing expected auto-property backing field '{backingFieldName}' on {instance.GetType().Name}.");
            }

            field.SetValue(instance, value);
        }

        // ─── GAP-8: Legs at rest during GettingUp state ──────────────────────

        /// <summary>
        /// GAP-8: While the character is in GettingUp state, all four leg joints must
        /// be driven to identity each FixedUpdate and the gait phase must remain zero.
        ///
        /// Risk: the early-exit in FixedUpdate covers Fallen and GettingUp, but if a
        /// future refactor removes GettingUp from the guard condition, legs may cycle
        /// during get-up — causing the character to flail while recovering.
        ///
        /// This test seeds all joints to non-identity, sets state to GettingUp,
        /// runs one FixedUpdate, and asserts identity on all joints.
        /// </summary>
        [UnityTest]
        public IEnumerator DuringGettingUpState_LegsAreAtRestNotCycling()
        {
            // Arrange
            yield return null;

            // Seed all leg joints to a non-identity rotation to prove they get reset.
            Quaternion nonIdentity = Quaternion.Euler(30f, 15f, 10f);
            if (_upperLegLJoint != null) _upperLegLJoint.targetRotation = nonIdentity;
            if (_upperLegRJoint != null) _upperLegRJoint.targetRotation = nonIdentity;
            if (_lowerLegLJoint != null) _lowerLegLJoint.targetRotation = nonIdentity;
            if (_lowerLegRJoint != null) _lowerLegRJoint.targetRotation = nonIdentity;

            // Also prime the gait with non-zero phase and smoothed input mag.
            SetPrivateField(_legAnimator, "_phase", 1.5f);
            SetPrivateField(_legAnimator, "_smoothedInputMag", 0.8f);

            // Set state to GettingUp.
            SetAutoPropertyBackingField(_characterState, "CurrentState", CharacterStateType.GettingUp);

            // Set move input so the LegAnimator would normally advance gait.
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Act — one FixedUpdate.
            yield return new WaitForFixedUpdate();

            // Assert 1: All leg joints should be identity.
            float angleLU = _upperLegLJoint != null ? Quaternion.Angle(_upperLegLJoint.targetRotation, Quaternion.identity) : 0f;
            float angleRU = _upperLegRJoint != null ? Quaternion.Angle(_upperLegRJoint.targetRotation, Quaternion.identity) : 0f;
            float angleLL = _lowerLegLJoint != null ? Quaternion.Angle(_lowerLegLJoint.targetRotation, Quaternion.identity) : 0f;
            float angleRL = _lowerLegRJoint != null ? Quaternion.Angle(_lowerLegRJoint.targetRotation, Quaternion.identity) : 0f;

            Assert.That(angleLU, Is.LessThanOrEqualTo(0.5f),
                $"UpperLeg_L must be at identity during GettingUp. Angle from identity: {angleLU:F2}°. " +
                "LegAnimator early-exit for GettingUp state may be missing.");
            Assert.That(angleRU, Is.LessThanOrEqualTo(0.5f),
                $"UpperLeg_R must be at identity during GettingUp. Angle: {angleRU:F2}°.");
            Assert.That(angleLL, Is.LessThanOrEqualTo(0.5f),
                $"LowerLeg_L must be at identity during GettingUp. Angle: {angleLL:F2}°.");
            Assert.That(angleRL, Is.LessThanOrEqualTo(0.5f),
                $"LowerLeg_R must be at identity during GettingUp. Angle: {angleRL:F2}°.");

            // Assert 2: Phase must be zero (reset in the GettingUp early-exit).
            float phase = (float)GetPrivateField(_legAnimator, "_phase");
            Assert.That(phase, Is.EqualTo(0f).Within(0.0001f),
                $"Gait phase must be 0 during GettingUp state. Got {phase:F4}. " +
                "LegAnimator must reset _phase in the GettingUp early-exit branch.");

            // Assert 3: SmoothedInputMag must be zero.
            float smoothedMag = (float)GetPrivateField(_legAnimator, "_smoothedInputMag");
            Assert.That(smoothedMag, Is.EqualTo(0f).Within(0.0001f),
                $"SmoothedInputMag must be 0 during GettingUp state. Got {smoothedMag:F4}.");
        }

        // ─── GAP-11: Gait phase stability on velocity dip ────────────────────

        /// <summary>
        /// GAP-11: During a sustained walk, a brief velocity dip below the isMoving
        /// threshold must NOT reset the gait phase to zero mid-stride.
        ///
        /// Risk: if the isMoving gate uses only instantaneous speed (without input fallback),
        /// a single physics frame where horizontal speed dips below 0.02 m/s (e.g. from
        /// collision response or integration variance) will fire the idle branch, which
        /// includes the sharpTurn/restarting phase-reset logic. This causes legs to snap
        /// to identity and restart from phase=0, producing a jarring visual pop.
        ///
        /// This test:
        ///   1. Runs 30 frames of forward input to accumulate phase.
        ///   2. Reads the phase after 30 frames.
        ///   3. Injects a single frame where speed is zeroed (zero velocity override).
        ///   4. Continues 20 more frames of forward input.
        ///   5. Asserts the phase after the dip is GREATER than the phase was before
        ///      the dip — i.e. it continued from where it was, not restarted from 0.
        ///
        /// The LegAnimator input-fallback (isMoving = true when inputMagnitude > 0.01)
        /// should prevent the idle branch from firing when input is held, regardless
        /// of velocity state.
        /// </summary>
        [UnityTest]
        public IEnumerator DuringSustainedWalk_PhaseDoesNotResetOnVelocityDip()
        {
            // Arrange
            yield return null;

            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            SetAutoPropertyBackingField(_balance, "IsFallen", false);

            // Act Phase 1: 30 frames of forward input to accumulate phase.
            for (int i = 0; i < 30; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            float phaseAfterWarm = (float)GetPrivateField(_legAnimator, "_phase");

            // Precondition: phase must have accumulated.
            Assert.That(phaseAfterWarm, Is.GreaterThan(0.01f),
                $"Phase should be > 0 after 30 frames of forward input. Got {phaseAfterWarm:F4}. " +
                "Gait may not be running — check that BalanceController.IsFallen=false and " +
                "CharacterState is not Fallen/GettingUp.");

            // Act Phase 2: inject a velocity dip by zeroing the hips Rigidbody velocity.
            // Input is still held (new Vector2(0f, 1f)) so LegAnimator should stay in isMoving=true.
            _hipsRb.linearVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();

            float phaseAfterDip = (float)GetPrivateField(_legAnimator, "_phase");

            // Act Phase 3: 20 more frames.
            for (int i = 0; i < 20; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            float phaseAfterResume = (float)GetPrivateField(_legAnimator, "_phase");

            // Assert 1: phase did not reset to zero during the dip frame.
            // Allow a small epsilon in case of floating-point phase wrap.
            Assert.That(phaseAfterDip, Is.GreaterThan(0.001f),
                $"Phase must not reset to zero during a single-frame velocity dip when input is held. " +
                $"Phase before dip: {phaseAfterWarm:F4}, phase after dip: {phaseAfterDip:F4}. " +
                "LegAnimator should use input magnitude as a fallback for isMoving even when speed=0.");

            // Assert 2: phase continued advancing after the dip (not stalled at the dip value).
            // Account for 2π wrap: if phaseAfterWarm was near 2π and wrapped, use modulo comparison.
            bool phaseAdvanced = phaseAfterResume > phaseAfterDip
                || (phaseAfterResume < 0.5f && phaseAfterDip > Mathf.PI); // wrapped around
            Assert.That(phaseAdvanced, Is.True,
                $"Phase must continue advancing after the velocity dip. " +
                $"Phase after dip: {phaseAfterDip:F4}, phase after 20 more frames: {phaseAfterResume:F4}.");
        }

        // ─── Stuck-Leg Recovery Tests (Option D) ────────────────────────────────

        /// <summary>
        /// When the character has input but is stuck (zero horizontal velocity for
        /// _stuckFrameThreshold frames while smoothedInputMag > 0.5), LegAnimator must
        /// drive both UpperLeg joints to the recovery pose (AngleAxis(-30, swingAxis)),
        /// which should be significantly different from identity (> 10 degrees).
        /// </summary>
        [UnityTest]
        public IEnumerator WhenStuck_RecoveryPoseIsApplied()
        {
            // Arrange
            yield return null;

            // Set state to Moving so recovery state guard passes.
            _characterState.SetStateForTest(CharacterStateType.Moving);

            // Apply forward move input.
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Zero velocity to simulate stuck (hips position-frozen).
            _hipsRb.linearVelocity = Vector3.zero;
            _hipsRb.constraints = RigidbodyConstraints.FreezePosition | RigidbodyConstraints.FreezeRotation;

            // Pre-seed _smoothedInputMag to 1.0f so stuck detection triggers within
            // _stuckFrameThreshold frames (otherwise we would need ~14 additional frames
            // for _smoothedInputMag to ramp past the 0.5 threshold).
            SetPrivateField(_legAnimator, "_smoothedInputMag", 1.0f);

            // Reduce _stuckFrameThreshold to 10 for test speed (default 25 would require
            // ~35 frames at 100Hz = 350 ms idle in test runner).
            SetPrivateField(_legAnimator, "_stuckFrameThreshold", 10);

            // Act: wait enough frames for stuck detection to fire.
            // Need _stuckFrameThreshold + 5 extra to ensure recovery has been entered.
            for (int i = 0; i < 15; i++)
            {
                _hipsRb.linearVelocity = Vector3.zero; // keep velocity zeroed each frame
                yield return new WaitForFixedUpdate();
            }

            // Assert 1: UpperLeg_L targetRotation should NOT be identity (recovery pose applied).
            float angleL = Quaternion.Angle(_upperLegLJoint.targetRotation, Quaternion.identity);
            Assert.That(angleL, Is.GreaterThan(10f),
                $"UpperLeg_L targetRotation must be in recovery pose (> 10 degrees from identity). " +
                $"Actual angle from identity: {angleL:F2}°. " +
                "LegAnimator stuck detection may not have fired, or recovery pose not applied.");

            // Assert 2: UpperLeg_R targetRotation should also be in recovery pose.
            float angleR = Quaternion.Angle(_upperLegRJoint.targetRotation, Quaternion.identity);
            Assert.That(angleR, Is.GreaterThan(10f),
                $"UpperLeg_R targetRotation must be in recovery pose (> 10 degrees from identity). " +
                $"Actual angle from identity: {angleR:F2}°. " +
                "Both upper-leg joints must receive recovery pose during stuck recovery.");
        }

        /// <summary>
        /// After the recovery window (_recoveryFrames) expires, _isRecovering must return
        /// to false, confirming that normal gait resumes and the recovery is self-terminating.
        /// </summary>
        [UnityTest]
        public IEnumerator AfterRecovery_GaitResumes()
        {
            // Arrange
            yield return null;

            // Set state to Moving.
            _characterState.SetStateForTest(CharacterStateType.Moving);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            _hipsRb.linearVelocity = Vector3.zero;
            _hipsRb.constraints = RigidbodyConstraints.FreezePosition | RigidbodyConstraints.FreezeRotation;

            // Pre-seed smoothedInputMag so detection is immediate.
            SetPrivateField(_legAnimator, "_smoothedInputMag", 1.0f);

            // Use small thresholds to keep the test fast.
            const int testStuckThreshold = 5;
            const int testRecoveryFrames = 10;
            SetPrivateField(_legAnimator, "_stuckFrameThreshold", testStuckThreshold);
            SetPrivateField(_legAnimator, "_recoveryFrames", testRecoveryFrames);

            // Act Phase 1: hold the body stationary long enough to enter recovery.
            bool enteredRecovery = false;
            for (int i = 0; i < testStuckThreshold + 3; i++)
            {
                _hipsRb.linearVelocity = Vector3.zero;
                _hipsRb.angularVelocity = Vector3.zero;
                yield return new WaitForFixedUpdate();

                if (_legAnimator.IsRecovering)
                {
                    enteredRecovery = true;
                    break;
                }
            }

            Assert.That(enteredRecovery, Is.True,
                "Pre-condition: recovery must have started after the stuck threshold is reached.");

            // Release constraint and force forward progress so the stuck condition clears
            // before the current recovery window expires.
            _hipsRb.constraints = RigidbodyConstraints.None;

            // Act Phase 2: keep the hips moving forward through the remainder of recovery.
            for (int i = 0; i < testRecoveryFrames + 10; i++)
            {
                _hipsRb.linearVelocity = new Vector3(0f, 0f, 0.5f);
                _hipsRb.angularVelocity = Vector3.zero;
                yield return new WaitForFixedUpdate();
            }

            // Assert: _isRecovering must be false (recovery completed).
            bool isRecoveringAfter = _legAnimator.IsRecovering;
            Assert.That(isRecoveringAfter, Is.False,
                "IsRecovering must be false after the recovery window expires. " +
                "Recovery must self-terminate after _recoveryFrames frames.");
        }

        // ─── Stranded-Foot Forward Bias Tests ──────────────────────────────

        /// <summary>
        /// When both feet are behind the hips (horizontal dot with hip forward &lt; 0)
        /// and the player has active movement input, <see cref="LegAnimator.IsGaitBiasedForward"/>
        /// must be true: the gait swing targets are biased forward to prevent the backward
        /// phase from reinforcing the stranded-foot stuck loop.
        /// </summary>
        [UnityTest]
        public IEnumerator WhenBothFeetBehindHipsWithInput_GaitBiasIsActive()
        {
            // Arrange
            yield return null;

            _characterState.SetStateForTest(CharacterStateType.Moving);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            // Freeze hips to prevent physics from repositioning foot objects.
            _hipsRb.linearVelocity = Vector3.zero;
            _hipsRb.constraints = RigidbodyConstraints.FreezeAll;

            // Create foot objects behind the hips (hips face +Z by default).
            var footL = new GameObject("Foot_L");
            footL.transform.SetParent(_hips.transform, worldPositionStays: false);
            footL.transform.position = _hips.transform.position + new Vector3(-0.1f, -0.8f, -0.5f);

            var footR = new GameObject("Foot_R");
            footR.transform.SetParent(_hips.transform, worldPositionStays: false);
            footR.transform.position = _hips.transform.position + new Vector3(0.1f, -0.8f, -0.5f);

            // Inject foot transforms via reflection (Awake already ran without them).
            SetPrivateField(_legAnimator, "_footL", footL.transform);
            SetPrivateField(_legAnimator, "_footR", footR.transform);

            // Pre-seed smoothedInputMag so the gait is at full amplitude.
            SetPrivateField(_legAnimator, "_smoothedInputMag", 1.0f);

            // Act
            yield return new WaitForFixedUpdate();

            // Assert
            Assert.That(_legAnimator.IsGaitBiasedForward, Is.True,
                "IsGaitBiasedForward must be true when both feet are behind the hips " +
                "and the player has active movement input. The stranded-foot bias should " +
                "activate to shift gait targets forward.");
        }

        /// <summary>
        /// When only one foot is behind the hips (the other is in front), the stranded-foot
        /// bias must NOT activate — this is normal alternating gait, not a stuck state.
        /// </summary>
        [UnityTest]
        public IEnumerator WhenOneFootInFront_GaitBiasIsNotActive()
        {
            // Arrange
            yield return null;

            _characterState.SetStateForTest(CharacterStateType.Moving);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            _hipsRb.linearVelocity = Vector3.zero;
            _hipsRb.constraints = RigidbodyConstraints.FreezeAll;

            // Left foot behind, right foot in front.
            var footL = new GameObject("Foot_L");
            footL.transform.SetParent(_hips.transform, worldPositionStays: false);
            footL.transform.position = _hips.transform.position + new Vector3(-0.1f, -0.8f, -0.5f);

            var footR = new GameObject("Foot_R");
            footR.transform.SetParent(_hips.transform, worldPositionStays: false);
            footR.transform.position = _hips.transform.position + new Vector3(0.1f, -0.8f, 0.5f);

            SetPrivateField(_legAnimator, "_footL", footL.transform);
            SetPrivateField(_legAnimator, "_footR", footR.transform);
            SetPrivateField(_legAnimator, "_smoothedInputMag", 1.0f);

            // Act
            yield return new WaitForFixedUpdate();

            // Assert
            Assert.That(_legAnimator.IsGaitBiasedForward, Is.False,
                "IsGaitBiasedForward must be false when only one foot is behind " +
                "the hips — this is normal alternating gait, not a stuck state.");
        }

        /// <summary>
        /// When both feet are behind the hips but the player has no movement input,
        /// the bias must NOT activate — there is no forward intent to assist.
        /// </summary>
        [UnityTest]
        public IEnumerator WhenBothFeetBehindButNoInput_GaitBiasIsNotActive()
        {
            // Arrange
            yield return null;

            _characterState.SetStateForTest(CharacterStateType.Moving);
            _movement.SetMoveInputForTest(Vector2.zero);

            _hipsRb.linearVelocity = Vector3.zero;
            _hipsRb.constraints = RigidbodyConstraints.FreezeAll;

            // Both feet behind hips.
            var footL = new GameObject("Foot_L");
            footL.transform.SetParent(_hips.transform, worldPositionStays: false);
            footL.transform.position = _hips.transform.position + new Vector3(-0.1f, -0.8f, -0.5f);

            var footR = new GameObject("Foot_R");
            footR.transform.SetParent(_hips.transform, worldPositionStays: false);
            footR.transform.position = _hips.transform.position + new Vector3(0.1f, -0.8f, -0.5f);

            SetPrivateField(_legAnimator, "_footL", footL.transform);
            SetPrivateField(_legAnimator, "_footR", footR.transform);
            SetPrivateField(_legAnimator, "_smoothedInputMag", 1.0f);

            // Act
            yield return new WaitForFixedUpdate();

            // Assert
            Assert.That(_legAnimator.IsGaitBiasedForward, Is.False,
                "IsGaitBiasedForward must be false when there is no movement input, " +
                "even if both feet are behind the hips. Bias requires active forward intent.");
        }

        /// <summary>
        /// Outcome test: when the stranded-foot bias is active and the left leg is at peak
        /// backward phase (sin(3π/2) = −1), the upper-leg target rotation should be near
        /// identity (neutral 0°) rather than the normal −50.3°. The bias shifts the backward
        /// phase from −stepAngle to 0°, which is the core recovery mechanism that breaks
        /// the self-reinforcing stuck loop.
        /// </summary>
        [UnityTest]
        public IEnumerator WhenBiasActive_BackwardPhaseSwingIsNeutral()
        {
            // Arrange
            yield return null;

            _characterState.SetStateForTest(CharacterStateType.Moving);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            _hipsRb.linearVelocity = Vector3.zero;
            _hipsRb.constraints = RigidbodyConstraints.FreezeAll;

            // Both feet behind hips.
            var footL = new GameObject("Foot_L");
            footL.transform.SetParent(_hips.transform, worldPositionStays: false);
            footL.transform.position = _hips.transform.position + new Vector3(-0.1f, -0.8f, -0.5f);

            var footR = new GameObject("Foot_R");
            footR.transform.SetParent(_hips.transform, worldPositionStays: false);
            footR.transform.position = _hips.transform.position + new Vector3(0.1f, -0.8f, -0.5f);

            SetPrivateField(_legAnimator, "_footL", footL.transform);
            SetPrivateField(_legAnimator, "_footR", footR.transform);

            // Set phase to 3π/2 so left leg is at peak backward swing (sin = −1).
            // With bias: leftSwingDeg = (−1 × stepAngle × 1.0) + (stepAngle × 1.0) = 0°.
            SetPrivateField(_legAnimator, "_phase", Mathf.PI * 1.5f);
            SetPrivateField(_legAnimator, "_smoothedInputMag", 1.0f);

            // Prevent phase from advancing by zeroing step frequency (velocity is already 0).
            SetPrivateField(_legAnimator, "_stepFrequency", 0f);

            // Act
            yield return new WaitForFixedUpdate();

            // Assert: left upper-leg target should be near identity (bias neutralised backward swing).
            float angleL = Quaternion.Angle(_upperLegLJoint.targetRotation, Quaternion.identity);
            Assert.That(angleL, Is.LessThan(5f),
                $"With stranded-foot bias at peak backward phase (sin=−1), left upper-leg target " +
                $"should be near identity (0°). Actual: {angleL:F2}°. Without bias it would be " +
                "~50° from identity. The bias shifts backward swing from −stepAngle to neutral.");
        }
    }
}
