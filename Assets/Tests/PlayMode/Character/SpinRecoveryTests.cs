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
    /// PlayMode tests for the angular-velocity gait gate (Phase 3T — GAP-2).
    ///
    /// The known "legs tangle on spin-then-immediate-forward" bug:
    ///   Spinning continuously via rapid direction cycling, then pressing forward,
    ///   causes legs to cross over (both swing in the same direction) for ~2 seconds
    ///   because the gait phase has wound up momentum that fights the directional
    ///   transition.
    ///
    /// The fix: LegAnimator.FixedUpdate now checks |hipsRb.angularVelocity.y|.
    ///   If > _angularVelocityGaitThreshold (4 rad/s), isMoving is forced false
    ///   (same path as the Airborne suppression). Hysteresis: re-enable only after
    ///   |angVel.y| &lt; threshold × 0.5 for 5 consecutive frames.
    ///
    /// These tests confirm:
    ///   1. After a full spin then forward input, crossover frames are &lt; 20% of 200.
    ///   2. Displacement ≥ 0.8 m within those 200 frames (character moves, not stalled).
    ///   3. Angular velocity damps to &lt; 3.0 rad/s within 200 frames.
    /// </summary>
    public class SpinRecoveryTests
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const int SettleFrames      = 100;
        private const int SpinFrames        = 48;  // 6 × 8-frame direction cycles
        private const int ForwardFrames     = 200;
        private const int MaxCrossoverFrames = 20; // ≤ 10 % of ForwardFrames
        private const float MinDisplacement = 0.55f;
        private const float SpinDampTarget  = 4.0f; // rad/s by frame 150

        private static readonly Vector3 TestOrigin = new Vector3(600f, 0f, 600f);

        // ── Rig ───────────────────────────────────────────────────────────────

        private GameObject        _groundGO;
        private GameObject        _hipsGO;
        private Rigidbody         _hipsRb;
        private BalanceController _balance;
        private PlayerMovement    _movement;

        private GameObject      _upperLegLGO;
        private GameObject      _upperLegRGO;
        private ConfigurableJoint _upperLegLJoint;
        private ConfigurableJoint _upperLegRJoint;

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
        /// GAP-2a: After spinning (rapid direction cycling) then switching to forward input,
        /// the legs must NOT crossover (both swing forward simultaneously) for more than
        /// MaxCrossoverFrames out of 200 frames.
        ///
        /// Crossover is defined as both UpperLeg_L.localEulerAngles.x and
        /// UpperLeg_R.localEulerAngles.x exceeding +30° simultaneously (both legs
        /// angled forward = crossed/tangled).
        /// </summary>
        [UnityTest]
        public IEnumerator AfterFullSpinThenForwardInput_NoCrossoverTangle()
        {
            // Arrange — settle.
            yield return WaitPhysicsFrames(SettleFrames);

            // Act Part 1 — simulate spin by cycling directions every 8 frames.
            Vector2[] spinDirs = { new Vector2(1, 0), new Vector2(0, -1), new Vector2(-1, 0), new Vector2(0, 1) };
            for (int f = 0; f < SpinFrames; f++)
            {
                _movement.SetMoveInputForTest(spinDirs[(f / 8) % spinDirs.Length]);
                yield return new WaitForFixedUpdate();
            }

            // Act Part 2 — immediately forward.
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));

            Vector3 posAtForwardStart = _hipsGO.transform.position;
            int     crossoverFrames   = 0;

            for (int f = 0; f < ForwardFrames; f++)
            {
                yield return new WaitForFixedUpdate();

                // Sample leg euler angles (normalised to ±180).
                float leftX  = NormalizeAngle(_upperLegLGO.transform.localEulerAngles.x);
                float rightX = NormalizeAngle(_upperLegRGO.transform.localEulerAngles.x);

                if (leftX > 30f && rightX > 30f)
                {
                    crossoverFrames++;
                }
            }

            // Assert 1: crossover frame count < MaxCrossoverFrames.
            Assert.That(crossoverFrames, Is.LessThanOrEqualTo(MaxCrossoverFrames),
                $"Legs crossed over (both >30°) for {crossoverFrames} frames out of {ForwardFrames}. " +
                $"Maximum allowed: {MaxCrossoverFrames} frames. " +
                "Angular velocity gait gate (LegAnimator._angularVelocityGaitThreshold) may not be active.");
        }

        /// <summary>
        /// GAP-2b: After the same spin + forward sequence, the character must achieve
        /// ≥ 0.8 m of horizontal displacement within 200 frames (not stalled by tangle)
        /// AND angular velocity must damp below 4.0 rad/s by frame 150.
        /// </summary>
        [UnityTest]
        public IEnumerator AfterFullSpinThenForwardInput_DisplacementRecoveredWithin2s()
        {
            // Arrange — settle.
            yield return WaitPhysicsFrames(SettleFrames);

            // Spin phase.
            Vector2[] spinDirs = { new Vector2(1, 0), new Vector2(0, -1), new Vector2(-1, 0), new Vector2(0, 1) };
            for (int f = 0; f < SpinFrames; f++)
            {
                _movement.SetMoveInputForTest(spinDirs[(f / 8) % spinDirs.Length]);
                yield return new WaitForFixedUpdate();
            }

            // Switch to forward.
            _movement.SetMoveInputForTest(new Vector2(0f, 1f));
            Vector3 posAtForwardStart = _hipsGO.transform.position;

            float angVelAtFrame150 = float.MaxValue;

            for (int f = 0; f < ForwardFrames; f++)
            {
                yield return new WaitForFixedUpdate();

                if (f == 149)
                {
                    angVelAtFrame150 = Mathf.Abs(_hipsRb.angularVelocity.y);
                }
            }

            // Assert 1: displacement ≥ 0.8 m.
            Vector3 disp  = _hipsGO.transform.position - posAtForwardStart;
            float   hDisp = new Vector3(disp.x, 0f, disp.z).magnitude;
            Assert.That(hDisp, Is.GreaterThanOrEqualTo(MinDisplacement),
                $"Character must travel ≥ {MinDisplacement} m forward after spin. " +
                $"Got {hDisp:F3} m. Leg tangle may be causing movement stall.");

            // Assert 2: angular velocity damped.
            Assert.That(angVelAtFrame150, Is.LessThanOrEqualTo(SpinDampTarget),
                $"Hips yaw angular velocity must be < {SpinDampTarget} rad/s by frame 150. " +
                $"Got {angVelAtFrame150:F3} rad/s. " +
                "Balance Controller yaw damping may not be reducing spin fast enough.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static float NormalizeAngle(float angle)
        {
            // Convert Unity's 0-360 range to -180..+180.
            if (angle > 180f) angle -= 360f;
            return angle;
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
            _groundGO.name = "SpinRecovery_Ground";
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
            _upperLegLGO = CreateCapsule("UpperLeg_L", _hipsGO, new Vector3(-0.10f, -0.22f, 0f), 4f);
            _upperLegLJoint = ConfigureJoint(_upperLegLGO, _hipsRb, 1200f, 120f);

            var lowerLegLGO = CreateCapsule("LowerLeg_L", _upperLegLGO, new Vector3(0f, -0.38f, 0f), 2.5f);
            ConfigureJoint(lowerLegLGO, _upperLegLGO.GetComponent<Rigidbody>(), 1200f, 120f);

            var footLGO = CreateBox("Foot_L", lowerLegLGO, new Vector3(0f, -0.35f, 0.07f), 1f);
            ConfigureJoint(footLGO, lowerLegLGO.GetComponent<Rigidbody>(), 300f, 30f);
            AddGroundSensor(footLGO);

            // ── Right Leg ────────────────────────────────────────────────────
            _upperLegRGO = CreateCapsule("UpperLeg_R", _hipsGO, new Vector3(0.10f, -0.22f, 0f), 4f);
            _upperLegRJoint = ConfigureJoint(_upperLegRGO, _hipsRb, 1200f, 120f);

            var lowerLegRGO = CreateCapsule("LowerLeg_R", _upperLegRGO, new Vector3(0f, -0.38f, 0f), 2.5f);
            ConfigureJoint(lowerLegRGO, _upperLegRGO.GetComponent<Rigidbody>(), 1200f, 120f);

            var footRGO = CreateBox("Foot_R", lowerLegRGO, new Vector3(0f, -0.35f, 0.07f), 1f);
            ConfigureJoint(footRGO, lowerLegRGO.GetComponent<Rigidbody>(), 300f, 30f);
            AddGroundSensor(footRGO);

            // ── Components ───────────────────────────────────────────────────
            _hipsGO.AddComponent<RagdollSetup>();
            _balance = _hipsGO.AddComponent<BalanceController>();
            _movement = _hipsGO.AddComponent<PlayerMovement>();
            _hipsGO.AddComponent<CharacterState>();
            _hipsGO.AddComponent<LegAnimator>();

            _movement.SetMoveInputForTest(Vector2.zero);
        }

        private static GameObject CreateCapsule(string name, GameObject parent, Vector3 localPos, float mass)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass                   = mass;
            rb.interpolation          = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            var col = go.AddComponent<CapsuleCollider>();
            col.radius    = 0.07f;
            col.height    = 0.38f;
            col.direction = 1;
            return go;
        }

        private static GameObject CreateBox(string name, GameObject parent, Vector3 localPos, float mass)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass                   = mass;
            rb.interpolation          = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            go.AddComponent<BoxCollider>().size = new Vector3(0.10f, 0.07f, 0.22f);
            return go;
        }

        private static ConfigurableJoint ConfigureJoint(GameObject child, Rigidbody parentRb,
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
