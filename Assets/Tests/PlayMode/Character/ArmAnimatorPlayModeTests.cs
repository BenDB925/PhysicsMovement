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
    /// PlayMode integration tests for ArmAnimator (Phase 3T — GAP-7/GAP-13).
    ///
    /// Validates the runtime behaviour of arm swing during locomotion:
    ///   • WhileWalking_RightArmSwingsOppositeToLeftLeg: checks that left arm and right arm
    ///     rotate in opposite directions relative to each other, matching natural counter-swing.
    ///   • AtIdle_ArmsReturnToNearRestPosition: after walking stops, arms must return to
    ///     near-identity within a few seconds (SmoothedInputMag decays to zero).
    ///
    /// These PlayMode tests complement the EditMode unit tests by verifying that the
    /// arm swing runs correctly under live FixedUpdate scheduling with real physics.
    /// </summary>
    public class ArmAnimatorPlayModeTests
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const int SettleFrames    = 100;
        private const int WalkFrames      = 120;
        private const int IdleDecayFrames = 150;

        private static readonly Vector3 TestOrigin = new Vector3(800f, 0f, 800f);

        // ── Rig ───────────────────────────────────────────────────────────────

        private GameObject        _groundGO;
        private GameObject        _hipsGO;
        private Rigidbody         _hipsRb;
        private PlayerMovement    _movement;
        private BalanceController _balance;
        private CharacterState    _characterState;

        private ConfigurableJoint _upperArmLJoint;
        private ConfigurableJoint _upperArmRJoint;

        private float _savedFixedDeltaTime;
        private int   _savedSolverIterations;
        private int   _savedSolverVelocityIterations;

        [SetUp]
        public void SetUp()
        {
            _savedFixedDeltaTime           = Time.fixedDeltaTime;
            _savedSolverIterations         = Physics.defaultSolverIterations;
            _savedSolverVelocityIterations = Physics.defaultSolverVelocityIterations;

            Time.fixedDeltaTime                  = 0.01f;
            Physics.defaultSolverIterations      = 12;
            Physics.defaultSolverVelocityIterations = 4;

            BuildGroundPlane();
            BuildRig();

            // ── Inject stable grounded state and disable controllers so their
            //    FixedUpdate loops do not override injected state each frame.
            _balance        = _hipsGO.GetComponent<BalanceController>();
            _characterState = _hipsGO.GetComponent<CharacterState>();

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _balance.enabled        = false;
            _characterState.enabled = false;

            // Freeze hips rotation so the physics rig never accumulates yaw angular
            // velocity during the settle phase. Without this, collision bounces on the
            // dynamic Rigidbody can push angVelY above the spin-gate threshold
            // (threshold × 0.5 = 4 rad/s), repeatedly resetting the hysteresis counter
            // inside LegAnimator and preventing gait from ever engaging during the tests.
            _hipsRb.constraints = RigidbodyConstraints.FreezeRotation;
        }

        [TearDown]
        public void TearDown()
        {
            if (_hipsGO   != null) Object.Destroy(_hipsGO);
            if (_groundGO != null) Object.Destroy(_groundGO);

            Time.fixedDeltaTime                  = _savedFixedDeltaTime;
            Physics.defaultSolverIterations      = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        /// <summary>
        /// GAP-7 PlayMode: While walking, the right arm must swing in the OPPOSITE direction
        /// to the left arm at any given frame (counter-swing gait coordination).
        ///
        /// We sample both arm targetRotation Z-euler angles during the walk phase and
        /// confirm they are not both positive or both negative on the same frame.
        /// A sampling window of 40–80 frames (mid-walk) is used to avoid the ramp-up period.
        ///
        /// Threshold: across the sample window, the fraction of frames where arms swing
        /// in opposite directions must be ≥ 60%. Allows for cross-zero frames where
        /// both angles are near zero simultaneously.
        /// </summary>
        [UnityTest]
        public IEnumerator WhileWalking_RightArmSwingsOppositeToLeftLeg()
        {
            // Arrange — settle.
            yield return WaitPhysicsFrames(SettleFrames);

            // Inject Moving state so LegAnimator advances the gait phase.
            _characterState.SetStateForTest(CharacterStateType.Moving);

            // Begin walking.
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            int oppositeFrames = 0;
            int sampleFrames   = 0;

            for (int frame = 0; frame < WalkFrames; frame++)
            {
                yield return new WaitForFixedUpdate();

                // Only sample the middle portion (frames 40–80) to skip ramp-up.
                if (frame < 40 || frame >= 80) continue;

                sampleFrames++;

                // Read arm target rotations.
                Quaternion leftArmTarget  = _upperArmLJoint != null
                    ? _upperArmLJoint.targetRotation
                    : Quaternion.identity;
                Quaternion rightArmTarget = _upperArmRJoint != null
                    ? _upperArmRJoint.targetRotation
                    : Quaternion.identity;

                // Extract signed Z-euler angles.
                float leftZ  = Mathf.DeltaAngle(0f, leftArmTarget.eulerAngles.z);
                float rightZ = Mathf.DeltaAngle(0f, rightArmTarget.eulerAngles.z);

                // Opposite direction = signs differ (one positive, one negative).
                // Allow a deadzone of ±1° to avoid counting frames where both are near zero.
                bool leftActive  = Mathf.Abs(leftZ)  > 1f;
                bool rightActive = Mathf.Abs(rightZ) > 1f;

                if (leftActive && rightActive && Mathf.Sign(leftZ) != Mathf.Sign(rightZ))
                {
                    oppositeFrames++;
                }
            }

            // Assert: ≥ 60% of active sample frames have opposite swing directions.
            float oppositeRatio = sampleFrames > 0 ? (float)oppositeFrames / sampleFrames : 0f;
            Assert.That(sampleFrames, Is.GreaterThan(0),
                "No sample frames were collected. Check WalkFrames and sample window constants.");
            Assert.That(oppositeRatio, Is.GreaterThanOrEqualTo(0.6f),
                $"Arms should swing in opposite directions ≥ 60% of walk frames. " +
                $"Got {oppositeFrames}/{sampleFrames} = {oppositeRatio * 100f:F0}%. " +
                "Check ArmAnimator phase offset (left arm should use phase+π, right arm phase).");
        }

        /// <summary>
        /// GAP-13 PlayMode: After walking stops (input set to zero), both arm joints
        /// must return to near-identity (within 2° of Quaternion.identity) within
        /// IdleDecayFrames (1.5 s at 100 Hz).
        ///
        /// This validates that the ArmAnimator idle branch correctly sets targetRotation
        /// to identity when SmoothedInputMag decays to zero.
        /// </summary>
        [UnityTest]
        public IEnumerator AtIdle_ArmsReturnToNearRestPosition()
        {
            // Arrange — settle, then walk for WalkFrames to prime the arm swing.
            _characterState.SetStateForTest(CharacterStateType.Standing);
            yield return WaitPhysicsFrames(SettleFrames);

            _characterState.SetStateForTest(CharacterStateType.Moving);
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            // DEBUG: yield mid-walk to sample SmoothedInputMag
            LegAnimator legAnim = _hipsGO.GetComponent<LegAnimator>();
            float midMag = 0f;
            for (int dbgFrame = 0; dbgFrame < WalkFrames; dbgFrame++)
            {
                yield return new WaitForFixedUpdate();
                if (dbgFrame == 30) midMag = legAnim != null ? legAnim.SmoothedInputMag : -1f;
            }
            UnityEngine.Debug.Log($"[AtIdle_DEBUG] SmoothedInputMag@frame30={midMag:F4}  LegPhase={legAnim?.Phase:F4}  CharState={_characterState.CurrentState}");

            // Verify arms are not at identity (i.e., swing is active before stopping).
            float leftAngleBefore  = _upperArmLJoint != null
                ? Quaternion.Angle(_upperArmLJoint.targetRotation, Quaternion.identity)
                : 0f;
            float rightAngleBefore = _upperArmRJoint != null
                ? Quaternion.Angle(_upperArmRJoint.targetRotation, Quaternion.identity)
                : 0f;

            // Precondition: at least one arm should be non-identity after walking.
            float maxAngleBefore = Mathf.Max(leftAngleBefore, rightAngleBefore);
            Assert.That(maxAngleBefore, Is.GreaterThan(1f),
                $"Pre-condition: at least one arm should be non-identity after walking. " +
                $"Max angle from identity = {maxAngleBefore:F2}°. " +
                "ArmAnimator may not be applying swing — check LegAnimator gait is running.");

            // Act — stop input.
            _movement.SetMoveInputForTest(Vector2.zero);
            _characterState.SetStateForTest(CharacterStateType.Standing);

            // Wait up to IdleDecayFrames for arms to return to identity.
            bool recovered = false;
            const float IdleThreshold = 2f; // degrees

            for (int frame = 0; frame < IdleDecayFrames; frame++)
            {
                yield return new WaitForFixedUpdate();

                float leftAngle  = _upperArmLJoint != null
                    ? Quaternion.Angle(_upperArmLJoint.targetRotation, Quaternion.identity)
                    : 0f;
                float rightAngle = _upperArmRJoint != null
                    ? Quaternion.Angle(_upperArmRJoint.targetRotation, Quaternion.identity)
                    : 0f;

                if (leftAngle <= IdleThreshold && rightAngle <= IdleThreshold)
                {
                    recovered = true;
                    break;
                }
            }

            float finalLeftAngle  = _upperArmLJoint != null
                ? Quaternion.Angle(_upperArmLJoint.targetRotation, Quaternion.identity)
                : 0f;
            float finalRightAngle = _upperArmRJoint != null
                ? Quaternion.Angle(_upperArmRJoint.targetRotation, Quaternion.identity)
                : 0f;

            Assert.That(recovered, Is.True,
                $"Arms must return to near-identity (≤ {IdleThreshold}°) within {IdleDecayFrames} frames " +
                $"({IdleDecayFrames * Time.fixedDeltaTime:F1} s) after input stops. " +
                $"Final angles: left={finalLeftAngle:F2}°, right={finalRightAngle:F2}°. " +
                "Check ArmAnimator idle branch (SmoothedInputMag < 0.01 → SetAllArmTargetsToIdentity).");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static IEnumerator WaitPhysicsFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        // ── Rig Construction ──────────────────────────────────────────────────

        private void BuildGroundPlane()
        {
            _groundGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _groundGO.name = "ArmPlayMode_Ground";
            _groundGO.transform.position  = TestOrigin + new Vector3(0f, -0.5f, 0f);
            _groundGO.transform.localScale = new Vector3(400f, 1f, 400f);
            _groundGO.layer = GameSettings.LayerEnvironment;
        }

        private void BuildRig()
        {
            _hipsGO = new GameObject("Hips");
            _hipsGO.transform.position = TestOrigin + new Vector3(0f, 1.2f, 0f);

            _hipsRb                        = _hipsGO.AddComponent<Rigidbody>();
            _hipsRb.mass                   = 10f;
            _hipsRb.interpolation          = RigidbodyInterpolation.Interpolate;
            _hipsRb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            _hipsGO.AddComponent<BoxCollider>().size = new Vector3(0.26f, 0.20f, 0.15f);

            // ── Left Leg ─────────────────────────────────────────────────────
            var upperLegL = CreateCapsule("UpperLeg_L", _hipsGO, new Vector3(-0.10f, -0.22f, 0f), 4f);
            ConfigureJoint(upperLegL, _hipsRb, 1200f, 120f);
            var lowerLegL = CreateCapsule("LowerLeg_L", upperLegL, new Vector3(0f, -0.38f, 0f), 2.5f);
            ConfigureJoint(lowerLegL, upperLegL.GetComponent<Rigidbody>(), 1200f, 120f);
            var footL = CreateBox("Foot_L", lowerLegL, new Vector3(0f, -0.35f, 0.07f), 1f, new Vector3(0.10f, 0.07f, 0.22f));
            ConfigureJoint(footL, lowerLegL.GetComponent<Rigidbody>(), 300f, 30f);
            AddGroundSensor(footL);

            // ── Right Leg ────────────────────────────────────────────────────
            var upperLegR = CreateCapsule("UpperLeg_R", _hipsGO, new Vector3(0.10f, -0.22f, 0f), 4f);
            ConfigureJoint(upperLegR, _hipsRb, 1200f, 120f);
            var lowerLegR = CreateCapsule("LowerLeg_R", upperLegR, new Vector3(0f, -0.38f, 0f), 2.5f);
            ConfigureJoint(lowerLegR, upperLegR.GetComponent<Rigidbody>(), 1200f, 120f);
            var footR = CreateBox("Foot_R", lowerLegR, new Vector3(0f, -0.35f, 0.07f), 1f, new Vector3(0.10f, 0.07f, 0.22f));
            ConfigureJoint(footR, lowerLegR.GetComponent<Rigidbody>(), 300f, 30f);
            AddGroundSensor(footR);

            // ── Left Arm ─────────────────────────────────────────────────────
            var upperArmLGO = CreateCapsule("UpperArm_L", _hipsGO, new Vector3(-0.18f, 0.22f, 0f), 2f);
            _upperArmLJoint = ConfigureJointReturn(upperArmLGO, _hipsRb, 800f, 80f);
            var lowerArmLGO = CreateCapsule("LowerArm_L", upperArmLGO, new Vector3(0f, -0.28f, 0f), 1f);
            ConfigureJoint(lowerArmLGO, upperArmLGO.GetComponent<Rigidbody>(), 300f, 30f);

            // ── Right Arm ────────────────────────────────────────────────────
            var upperArmRGO = CreateCapsule("UpperArm_R", _hipsGO, new Vector3(0.18f, 0.22f, 0f), 2f);
            _upperArmRJoint = ConfigureJointReturn(upperArmRGO, _hipsRb, 800f, 80f);
            var lowerArmRGO = CreateCapsule("LowerArm_R", upperArmRGO, new Vector3(0f, -0.28f, 0f), 1f);
            ConfigureJoint(lowerArmRGO, upperArmRGO.GetComponent<Rigidbody>(), 300f, 30f);

            // ── Components ───────────────────────────────────────────────────
            _hipsGO.AddComponent<RagdollSetup>();
            _hipsGO.AddComponent<BalanceController>();
            _hipsGO.AddComponent<CharacterState>();
            _movement = _hipsGO.AddComponent<PlayerMovement>();
            _hipsGO.AddComponent<LegAnimator>();
            _hipsGO.AddComponent<ArmAnimator>();

            _movement.SetMoveInputForTest(Vector2.zero);
        }

        private static GameObject CreateCapsule(string name, GameObject parent,
            Vector3 localPos, float mass)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            var col = go.AddComponent<CapsuleCollider>();
            col.radius = 0.07f; col.height = 0.28f; col.direction = 1;
            return go;
        }

        private static GameObject CreateBox(string name, GameObject parent,
            Vector3 localPos, float mass, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        private static void ConfigureJoint(GameObject child, Rigidbody parentRb,
            float spring, float damper)
        {
            ConfigureJointReturn(child, parentRb, spring, damper);
        }

        private static ConfigurableJoint ConfigureJointReturn(GameObject child, Rigidbody parentRb,
            float spring, float damper)
        {
            var joint = child.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parentRb;
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;
            joint.lowAngularXLimit  = new SoftJointLimit { limit = -70f };
            joint.highAngularXLimit = new SoftJointLimit { limit = 70f };
            joint.angularYLimit     = new SoftJointLimit { limit = 40f };
            joint.angularZLimit     = new SoftJointLimit { limit = 40f };
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = damper,
                maximumForce   = float.MaxValue,
            };
            joint.targetRotation  = Quaternion.identity;
            joint.enableCollision = false;
            return joint;
        }

        private static void AddGroundSensor(GameObject footGO)
        {
            var sensor = footGO.AddComponent<GroundSensor>();
            var field  = typeof(GroundSensor).GetField("_groundLayers",
                BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(sensor, (LayerMask)(1 << GameSettings.LayerEnvironment));
        }
    }
}
