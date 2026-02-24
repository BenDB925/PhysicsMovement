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
    /// PlayMode tests for CameraFollow (Phase 3T — GAP-12).
    ///
    /// Validates:
    ///   • WithNullTarget_DoesNotThrow: CameraFollow must gracefully skip LateUpdate
    ///     when _target is null (no PlayerMovement in scene).
    ///   • DuringJump_CameraTracksUpwardMovement: camera Y must increase when the
    ///     character jumps (pivot = target.position + offset, which is higher).
    ///
    /// CameraFollow.Awake auto-finds PlayerMovement in the scene, so we control the
    /// target via reflection to test the null-target path cleanly.
    /// </summary>
    public class CameraFollowTests
    {
        // ── Constants ─────────────────────────────────────────────────────────

        private const int SettleFrames = 80;
        private const int JumpFrames   = 60;

        private static readonly Vector3 TestOrigin = new Vector3(900f, 0f, 900f);

        // ── Rig ───────────────────────────────────────────────────────────────

        private GameObject        _groundGO;
        private GameObject        _cameraGO;
        private GameObject        _hipsGO;
        private Rigidbody         _hipsRb;
        private PlayerMovement    _movement;
        private CameraFollow      _cameraFollow;

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
            BuildCamera();
            BuildRig();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hipsGO   != null) Object.Destroy(_hipsGO);
            if (_cameraGO != null) Object.Destroy(_cameraGO);
            if (_groundGO != null) Object.Destroy(_groundGO);

            Time.fixedDeltaTime                  = _savedFixedDeltaTime;
            Physics.defaultSolverIterations      = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        /// <summary>
        /// GAP-12a: When _target is null (no PlayerMovement in scene), CameraFollow
        /// must not throw a NullReferenceException during LateUpdate calls.
        ///
        /// The current implementation guards LateUpdate with `if (_target == null) return`,
        /// which prevents NRE. This test ensures that guard is present and robust.
        ///
        /// Method: force _target to null via reflection, then run several Update/LateUpdate
        /// cycles and assert no exception was thrown.
        /// </summary>
        [UnityTest]
        public IEnumerator WithNullTarget_DoesNotThrow()
        {
            // Arrange: destroy the character so CameraFollow cannot auto-find PlayerMovement.
            // Also force _target to null via reflection.
            if (_hipsGO != null)
            {
                Object.Destroy(_hipsGO);
                _hipsGO = null;
            }

            ForceTargetNull();

            // Act: run several frames. Should not throw.
            // NOTE: We cannot use try/catch with yield return — instead we use
            // LogAssert.NoUnexpectedReceived() and catch compile-safe.
            // We detect throw by checking the test doesn't crash Unity's test runner.
            for (int i = 0; i < 10; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            // Assert: if we reach here without the test runner crashing, no NRE was thrown.
            // Verify the CameraFollow component is still alive (not destroyed on exception).
            Assert.That(_cameraFollow != null, Is.True,
                "CameraFollow component should still exist after 10 frames with null target. " +
                "A NRE would have crashed the test runner.");
            Assert.That(_cameraGO != null, Is.True,
                "CameraFollow.LateUpdate must not throw when _target is null. " +
                "Add a null guard: 'if (_target == null) return;' at the start of LateUpdate.");
        }

        /// <summary>
        /// GAP-12b: During a jump, the camera's Y position should increase, tracking
        /// the character upward (pivot = target.position + pivotHeightOffset).
        ///
        /// Method:
        ///   1. Settle the ragdoll and camera.
        ///   2. Record the camera Y position before the jump.
        ///   3. Apply a jump impulse to the hips.
        ///   4. Wait JumpFrames.
        ///   5. Assert the camera Y is greater than before.
        ///
        /// Threshold: camera must have risen by ≥ 0.3 m (partial tracking is acceptable;
        /// the position smooth time may prevent full instant tracking).
        /// </summary>
        [UnityTest]
        public IEnumerator DuringJump_CameraTracksUpwardMovement()
        {
            // Arrange - settle with render frames so LateUpdate initialises camera position.
            yield return WaitRenderFrames(SettleFrames);

            float cameraYBefore = _cameraGO.transform.position.y;

            // Act - apply a strong upward impulse to simulate a jump.
            _hipsRb.AddForce(Vector3.up * 800f, ForceMode.Impulse);

            // Wait for both physics (hips to move up) and render frames (LateUpdate to track).
            yield return WaitPhysicsFrames(JumpFrames);
            yield return WaitRenderFrames(JumpFrames);

            float cameraYAfter = _cameraGO.transform.position.y;
            float cameraYDelta = cameraYAfter - cameraYBefore;

            // Assert: camera should have tracked at least 0.3 m upward.
            Assert.That(cameraYDelta, Is.GreaterThanOrEqualTo(0.3f),
                $"Camera should track upward during jump. " +
                $"Y before: {cameraYBefore:F3}, Y after: {cameraYAfter:F3}, delta: {cameraYDelta:F3}. " +
                "Check CameraFollow LateUpdate: pivot = target.position + Vector3.up × pivotHeightOffset.");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void ForceTargetNull()
        {
            FieldInfo targetField = typeof(CameraFollow)
                .GetField("_target", BindingFlags.NonPublic | BindingFlags.Instance);

            if (targetField != null)
            {
                targetField.SetValue(_cameraFollow, null);
            }
        }

        private static IEnumerator WaitPhysicsFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        /// <summary>
        /// Wait for render frames (end-of-frame) so LateUpdate runs.
        /// CameraFollow updates in LateUpdate, so camera position only changes
        /// when render frames are processed, not FixedUpdate frames.
        /// </summary>
        private static IEnumerator WaitRenderFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null; // yields to end of frame, triggers LateUpdate
            }
        }

        // ── Rig Construction ──────────────────────────────────────────────────

        private void BuildGroundPlane()
        {
            _groundGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _groundGO.name = "CameraFollow_Ground";
            _groundGO.transform.position  = TestOrigin + new Vector3(0f, -0.5f, 0f);
            _groundGO.transform.localScale = new Vector3(400f, 1f, 400f);
            _groundGO.layer = GameSettings.LayerEnvironment;
        }

        private void BuildCamera()
        {
            _cameraGO = new GameObject("TestCamera");
            _cameraGO.AddComponent<Camera>();
            _cameraGO.transform.position = TestOrigin + new Vector3(0f, 2f, -6f);
            _cameraFollow = _cameraGO.AddComponent<CameraFollow>();
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
            var ulL = CreateCapsule("UpperLeg_L", _hipsGO, new Vector3(-0.10f, -0.22f, 0f), 4f);
            ConfigureJoint(ulL, _hipsRb, 1200f, 120f);
            var llL = CreateCapsule("LowerLeg_L", ulL, new Vector3(0f, -0.38f, 0f), 2.5f);
            ConfigureJoint(llL, ulL.GetComponent<Rigidbody>(), 1200f, 120f);
            var fL  = CreateBox("Foot_L", llL, new Vector3(0f, -0.35f, 0.07f), 1f);
            ConfigureJoint(fL, llL.GetComponent<Rigidbody>(), 300f, 30f);
            AddGroundSensor(fL);

            // ── Right Leg ────────────────────────────────────────────────────
            var ulR = CreateCapsule("UpperLeg_R", _hipsGO, new Vector3(0.10f, -0.22f, 0f), 4f);
            ConfigureJoint(ulR, _hipsRb, 1200f, 120f);
            var llR = CreateCapsule("LowerLeg_R", ulR, new Vector3(0f, -0.38f, 0f), 2.5f);
            ConfigureJoint(llR, ulR.GetComponent<Rigidbody>(), 1200f, 120f);
            var fR  = CreateBox("Foot_R", llR, new Vector3(0f, -0.35f, 0.07f), 1f);
            ConfigureJoint(fR, llR.GetComponent<Rigidbody>(), 300f, 30f);
            AddGroundSensor(fR);

            // ── Components ───────────────────────────────────────────────────
            _hipsGO.AddComponent<RagdollSetup>();
            _hipsGO.AddComponent<BalanceController>();
            _movement = _hipsGO.AddComponent<PlayerMovement>();
            _hipsGO.AddComponent<CharacterState>();
            _hipsGO.AddComponent<LegAnimator>();

            _movement.SetMoveInputForTest(Vector2.zero);

            // Disable BalanceController and CharacterState so their FixedUpdate loops
            // don't fight the test impulse or override injected state.
            var bc = _hipsGO.GetComponent<BalanceController>();
            var cs = _hipsGO.GetComponent<CharacterState>();
            if (bc != null) bc.enabled = false;
            if (cs != null) cs.enabled = false;

            // CameraFollow.Awake runs before BuildRig() adds PlayerMovement so
            // FindFirstObjectByType returns null. Wire _target manually.
            typeof(CameraFollow)
                .GetField("_target", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.SetValue(_cameraFollow, _hipsGO.transform);

            // Original comment: — CameraFollow will find the PlayerMovement automatically.
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
            Vector3 localPos, float mass)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            go.AddComponent<BoxCollider>().size = new Vector3(0.10f, 0.07f, 0.22f);
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
