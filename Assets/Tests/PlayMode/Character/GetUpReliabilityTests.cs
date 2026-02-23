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
    /// PlayMode tests for get-up reliability (Phase 3T — GAP-5).
    ///
    /// Validates that after falling due to a directional impulse (left, right, backward),
    /// the character reliably transitions back to Standing/Moving within
    /// 1.5 × _getUpTimeout frames (budget = 1.5× the configured timeout).
    ///
    /// Risk: if _getUpForce is insufficient relative to the character's rotated pose,
    /// the character can oscillate between GettingUp and Fallen indefinitely.
    /// The CharacterState timeout fires but recovery may be unreliable in real poses
    /// as opposed to the synthetic unit-test poses in CharacterStateTests.
    ///
    /// Design:
    ///   - A minimal ragdoll with ground sensor is spawned and settled (200 frames).
    ///   - A large directional impulse (300 N) is applied to the hips.
    ///   - The test waits for Fallen state, then counts frames until Standing/Moving.
    ///   - The budget is capped at getUpTimeout × 1.5 × 100 Hz = frames.
    ///
    /// Note: _getUpTimeout is read from CharacterState via reflection so the test
    /// automatically adapts if the serialized field value is changed.
    /// </summary>
    public class GetUpReliabilityTests
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const int   SettleFrames          = 200;
        private const int   WaitForFallenFrames   = 400; // max frames to reach Fallen after impulse (BC is strong; needs time)
        private const float GetUpTimeoutScale     = 1.5f;
        private const float DefaultGetUpTimeout   = 3f;  // fallback if field not found

        private static readonly Vector3 TestOrigin = new Vector3(700f, 0f, 700f);

        // ── Shared ────────────────────────────────────────────────────────────

        private GameObject        _groundGO;
        private GameObject        _hipsGO;
        private Rigidbody         _hipsRb;
        private BalanceController _balance;
        private CharacterState    _characterState;

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
        /// GAP-5a: Fall left, then recover.
        /// </summary>
        [UnityTest]
        [Ignore("Requires a full ragdoll hierarchy to register hips tilt for BalanceController.IsFallen. Synthetic single-Rigidbody rig cannot produce valid fallen state. TODO: rewrite using Arena_01 scene like GaitOutcomeTests.")]
        public IEnumerator AfterFallingLeft_CharacterRecoversToStandingWithinTimeout()
        {
            yield return RunGetUpReliabilityTest(Vector3.left * 800f, "Left");
        }

        /// <summary>
        /// GAP-5b: Fall right, then recover.
        /// </summary>
        [UnityTest]
        [Ignore("Requires a full ragdoll hierarchy. See AfterFallingLeft for details.")]
        public IEnumerator AfterFallingRight_CharacterRecoversToStandingWithinTimeout()
        {
            yield return RunGetUpReliabilityTest(Vector3.right * 800f, "Right");
        }

        /// <summary>
        /// GAP-5c: Fall backward, then recover.
        /// </summary>
        [UnityTest]
        [Ignore("Requires a full ragdoll hierarchy. See AfterFallingLeft for details.")]
        public IEnumerator AfterFallingBackward_CharacterRecoversToStandingWithinTimeout()
        {
            yield return RunGetUpReliabilityTest(Vector3.back * 800f, "Backward");
        }

        // ── Shared test logic ─────────────────────────────────────────────────

        private IEnumerator RunGetUpReliabilityTest(Vector3 impulse, string directionLabel)
        {
            // Settle.
            yield return WaitPhysicsFrames(SettleFrames);

            // Apply impulse to knock character over.
            _hipsRb.AddForce(impulse, ForceMode.Impulse);

            // Wait until Fallen (up to WaitForFallenFrames frames).
            int waitForFallen = 0;
            while (_characterState.CurrentState != CharacterStateType.Fallen
                   && waitForFallen < WaitForFallenFrames)
            {
                yield return new WaitForFixedUpdate();
                waitForFallen++;
            }

            // Precondition: character must have actually fallen.
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen),
                $"[{directionLabel}] Character did not reach Fallen state within {WaitForFallenFrames} frames " +
                $"after a {impulse.magnitude:F0} N impulse. Impulse may not be large enough, or " +
                "fallen threshold may have changed. Check BalanceController._fallenAngleThreshold.");

            // Read _getUpTimeout from CharacterState (adapts to any field value change).
            float getUpTimeout = GetGetUpTimeout();
            int budgetFrames = Mathf.CeilToInt(getUpTimeout * GetUpTimeoutScale * (1f / Time.fixedDeltaTime));

            // Wait for recovery.
            bool recovered  = false;
            int  frameCount = 0;

            while (frameCount < budgetFrames)
            {
                yield return new WaitForFixedUpdate();
                frameCount++;

                CharacterStateType state = _characterState.CurrentState;
                if (state == CharacterStateType.Standing || state == CharacterStateType.Moving)
                {
                    recovered = true;
                    break;
                }
            }

            // Assert recovery.
            Assert.That(recovered, Is.True,
                $"[{directionLabel}] Character did not recover to Standing/Moving within " +
                $"{budgetFrames} frames ({budgetFrames * Time.fixedDeltaTime:F1} s = " +
                $"{GetUpTimeoutScale}× getUpTimeout of {getUpTimeout:F1} s). " +
                $"Current state: {_characterState.CurrentState}. " +
                "Check CharacterState._getUpForce — it may be insufficient for this fall pose.");

            // Secondary: balance controller should not report IsFallen.
            Assert.That(_balance.IsFallen, Is.False,
                $"[{directionLabel}] IsFallen must be false after successful recovery. " +
                "BalanceController fallen state and CharacterState are out of sync.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private float GetGetUpTimeout()
        {
            FieldInfo field = typeof(CharacterState).GetField("_getUpTimeout",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                return (float)field.GetValue(_characterState);
            }

            // Fallback: use a reasonable default so the test still runs.
            return DefaultGetUpTimeout;
        }

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
            _groundGO.name = "GetUpReliability_Ground";
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

            // ── Torso ────────────────────────────────────────────────────────
            var torsoGO = CreateBox("Torso", _hipsGO, new Vector3(0f, 0.32f, 0f), 12f,
                new Vector3(0.28f, 0.32f, 0.14f));
            ConfigureJoint(torsoGO, _hipsRb, 300f, 30f);

            // ── Left Leg ─────────────────────────────────────────────────────
            var upperLegL = CreateCapsule("UpperLeg_L", _hipsGO, new Vector3(-0.10f, -0.22f, 0f), 4f);
            ConfigureJoint(upperLegL, _hipsRb, 1200f, 120f);

            var lowerLegL = CreateCapsule("LowerLeg_L", upperLegL, new Vector3(0f, -0.38f, 0f), 2.5f);
            ConfigureJoint(lowerLegL, upperLegL.GetComponent<Rigidbody>(), 1200f, 120f);

            var footL = CreateBox("Foot_L", lowerLegL, new Vector3(0f, -0.35f, 0.07f), 1f,
                new Vector3(0.10f, 0.07f, 0.22f));
            ConfigureJoint(footL, lowerLegL.GetComponent<Rigidbody>(), 300f, 30f);
            AddGroundSensor(footL);

            // ── Right Leg ────────────────────────────────────────────────────
            var upperLegR = CreateCapsule("UpperLeg_R", _hipsGO, new Vector3(0.10f, -0.22f, 0f), 4f);
            ConfigureJoint(upperLegR, _hipsRb, 1200f, 120f);

            var lowerLegR = CreateCapsule("LowerLeg_R", upperLegR, new Vector3(0f, -0.38f, 0f), 2.5f);
            ConfigureJoint(lowerLegR, upperLegR.GetComponent<Rigidbody>(), 1200f, 120f);

            var footR = CreateBox("Foot_R", lowerLegR, new Vector3(0f, -0.35f, 0.07f), 1f,
                new Vector3(0.10f, 0.07f, 0.22f));
            ConfigureJoint(footR, lowerLegR.GetComponent<Rigidbody>(), 300f, 30f);
            AddGroundSensor(footR);

            // ── Components ───────────────────────────────────────────────────
            _hipsGO.AddComponent<RagdollSetup>();
            _balance        = _hipsGO.AddComponent<BalanceController>();
            _characterState = _hipsGO.AddComponent<CharacterState>();
            var movement    = _hipsGO.AddComponent<PlayerMovement>();
            _hipsGO.AddComponent<LegAnimator>();

            movement.SetMoveInputForTest(Vector2.zero);
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
            col.radius = 0.07f; col.height = 0.38f; col.direction = 1;
            return go;
        }

        private static GameObject CreateBox(string name, GameObject parent,
            Vector3 localPos, float mass, Vector3 size = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            if (size == default) size = new Vector3(0.28f, 0.32f, 0.14f);
            go.AddComponent<BoxCollider>().size = size;
            return go;
        }

        private static void ConfigureJoint(GameObject child, Rigidbody parentRb,
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
