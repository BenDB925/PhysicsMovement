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
    /// PlayMode full-stack sanity tests (Phase 3T — GAP-10).
    ///
    /// Validates that no Rigidbody in the character hierarchy develops NaN or Inf
    /// values in position, linearVelocity, or angularVelocity during a varied
    /// input sequence that exercises all locomotion subsystems simultaneously.
    ///
    /// The NaN-propagation risk:
    ///   - Camera pitch near vertical → ProjectOnPlane(camera.forward) ≈ zero →
    ///     Normalize() on near-zero vector → NaN velocity injected into PhysX.
    ///   - A NaN in angularVelocity propagates silently through all subsequent
    ///     physics steps, permanently locking the character in place.
    ///   - This test catches any such regression before it reaches the player.
    ///
    /// Design: builds a full character rig (all components), applies a mixed
    /// input cycle (forward → strafe → jump → stop → spin → forward) over
    /// 300 fixed frames, and asserts all Rigidbody physics values are finite.
    /// The test origin is placed far from (0,0,0) to avoid interaction with any
    /// geometry that might exist at the default scene origin.
    /// </summary>
    public class FullStackSanityTests
    {
        // ── Constants ────────────────────────────────────────────────────────

        private const int SettleFrames   = 100;
        private const int TestFrameCount = 300;

        /// <summary>Spawn origin — far from (0,0,0) to avoid scene geometry collisions.</summary>
        private static readonly Vector3 TestOrigin = new Vector3(500f, 0f, 500f);

        // ── Input cycle (segment lengths in frames) ───────────────────────────

        // 0–49:  forward
        // 50–99: strafe right
        // 100:   jump
        // 101–149: stop
        // 150–199: spin (cycle directions)
        // 200–299: forward

        // ── Shared Rig ────────────────────────────────────────────────────────

        private GameObject         _groundGO;
        private GameObject         _hipsGO;
        private Rigidbody          _hipsRb;
        private BalanceController  _balance;
        private PlayerMovement     _movement;
        private CharacterState     _characterState;
        private List<Rigidbody>    _allRigidbodies;

        private float _savedFixedDeltaTime;
        private int   _savedSolverIterations;
        private int   _savedSolverVelocityIterations;

        [SetUp]
        public void SetUp()
        {
            _savedFixedDeltaTime             = Time.fixedDeltaTime;
            _savedSolverIterations           = Physics.defaultSolverIterations;
            _savedSolverVelocityIterations   = Physics.defaultSolverVelocityIterations;

            Time.fixedDeltaTime                  = 0.01f;
            Physics.defaultSolverIterations      = 12;
            Physics.defaultSolverVelocityIterations = 4;

            BuildGroundPlane();
            BuildCharacterRig();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hipsGO  != null) Object.Destroy(_hipsGO);
            if (_groundGO != null) Object.Destroy(_groundGO);

            Time.fixedDeltaTime                  = _savedFixedDeltaTime;
            Physics.defaultSolverIterations      = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        /// <summary>
        /// GAP-10: After 300 frames of mixed input (forward, strafe, jump, stop, spin),
        /// no Rigidbody in the character hierarchy should contain NaN or Inf in its
        /// position, linearVelocity, or angularVelocity fields.
        /// </summary>
        [UnityTest]
        public IEnumerator FullStack_MixedInputLocomotion_NoNaNOrInfInAnyRigidbody()
        {
            // Arrange — let the rig settle before starting the test sequence.
            yield return WaitPhysicsFrames(SettleFrames);

            // Act — run the mixed input sequence.
            Vector2[] spinDirs =
            {
                new Vector2(1f, 0f),
                new Vector2(0f, -1f),
                new Vector2(-1f, 0f),
                new Vector2(0f, 1f),
            };

            for (int frame = 0; frame < TestFrameCount; frame++)
            {
                // Set input based on which segment we're in.
                if (frame < 50)
                {
                    _movement.SetMoveInputForTest(Vector2.up);           // forward
                }
                else if (frame < 100)
                {
                    _movement.SetMoveInputForTest(new Vector2(1f, 0f));  // strafe right
                }
                else if (frame == 100)
                {
                    _movement.SetMoveInputForTest(Vector2.zero);
                    _movement.SetJumpInputForTest(true);                  // single jump
                }
                else if (frame < 150)
                {
                    _movement.SetMoveInputForTest(Vector2.zero);         // stop
                }
                else if (frame < 200)
                {
                    // Spin: cycle directions every 12 frames.
                    _movement.SetMoveInputForTest(spinDirs[(frame / 12) % spinDirs.Length]);
                }
                else
                {
                    _movement.SetMoveInputForTest(Vector2.up);           // forward again
                }

                yield return new WaitForFixedUpdate();

                // Assert: check all rigidbodies for NaN/Inf every frame.
                AssertNoNaNOrInf(frame);
            }
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private void AssertNoNaNOrInf(int frame)
        {
            foreach (Rigidbody rb in _allRigidbodies)
            {
                if (rb == null) continue;

                string tag = $"[{rb.gameObject.name}] frame {frame}";

                Assert.That(float.IsNaN(rb.position.x) || float.IsInfinity(rb.position.x), Is.False,
                    $"{tag}: position.x is NaN/Inf ({rb.position.x:F6}). " +
                    "Likely cause: near-zero ProjectOnPlane result was Normalized, producing NaN.");

                Assert.That(float.IsNaN(rb.position.y) || float.IsInfinity(rb.position.y), Is.False,
                    $"{tag}: position.y is NaN/Inf ({rb.position.y:F6}).");

                Assert.That(float.IsNaN(rb.position.z) || float.IsInfinity(rb.position.z), Is.False,
                    $"{tag}: position.z is NaN/Inf ({rb.position.z:F6}).");

                Assert.That(float.IsNaN(rb.linearVelocity.x) || float.IsInfinity(rb.linearVelocity.x), Is.False,
                    $"{tag}: linearVelocity.x is NaN/Inf ({rb.linearVelocity.x:F6}).");

                Assert.That(float.IsNaN(rb.linearVelocity.y) || float.IsInfinity(rb.linearVelocity.y), Is.False,
                    $"{tag}: linearVelocity.y is NaN/Inf ({rb.linearVelocity.y:F6}).");

                Assert.That(float.IsNaN(rb.linearVelocity.z) || float.IsInfinity(rb.linearVelocity.z), Is.False,
                    $"{tag}: linearVelocity.z is NaN/Inf ({rb.linearVelocity.z:F6}).");

                Assert.That(float.IsNaN(rb.angularVelocity.x) || float.IsInfinity(rb.angularVelocity.x), Is.False,
                    $"{tag}: angularVelocity.x is NaN/Inf ({rb.angularVelocity.x:F6}).");

                Assert.That(float.IsNaN(rb.angularVelocity.y) || float.IsInfinity(rb.angularVelocity.y), Is.False,
                    $"{tag}: angularVelocity.y is NaN/Inf ({rb.angularVelocity.y:F6}).");

                Assert.That(float.IsNaN(rb.angularVelocity.z) || float.IsInfinity(rb.angularVelocity.z), Is.False,
                    $"{tag}: angularVelocity.z is NaN/Inf ({rb.angularVelocity.z:F6}).");
            }
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
            _groundGO.name = "FullStack_Ground";
            _groundGO.transform.position   = TestOrigin + new Vector3(0f, -0.5f, 0f);
            _groundGO.transform.localScale  = new Vector3(400f, 1f, 400f);
            _groundGO.layer = GameSettings.LayerEnvironment;
        }

        private void BuildCharacterRig()
        {
            _allRigidbodies = new List<Rigidbody>();

            // ── Hips root ────────────────────────────────────────────────────
            _hipsGO = new GameObject("Hips");
            _hipsGO.transform.position = TestOrigin + new Vector3(0f, 1.2f, 0f);

            _hipsRb                        = _hipsGO.AddComponent<Rigidbody>();
            _hipsRb.mass                   = 10f;
            _hipsRb.interpolation          = RigidbodyInterpolation.Interpolate;
            _hipsRb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            _allRigidbodies.Add(_hipsRb);

            var hipsCol = _hipsGO.AddComponent<BoxCollider>();
            hipsCol.size = new Vector3(0.26f, 0.20f, 0.15f);

            // ── Left Leg ─────────────────────────────────────────────────────
            var upperLegL = CreateSegment("UpperLeg_L", _hipsGO,
                new Vector3(-0.10f, -0.22f, 0f), 4f, 0.07f, 0.36f);
            ConfigureJoint(upperLegL, _hipsRb, 1200f, 120f, float.MaxValue);

            var lowerLegL = CreateSegment("LowerLeg_L", upperLegL,
                new Vector3(0f, -0.38f, 0f), 2.5f, 0.055f, 0.33f);
            ConfigureJoint(lowerLegL, upperLegL.GetComponent<Rigidbody>(), 1200f, 120f, float.MaxValue);

            var footL = CreateBoxSegment("Foot_L", lowerLegL,
                new Vector3(0f, -0.35f, 0.07f), 1f, new Vector3(0.10f, 0.07f, 0.22f));
            ConfigureJoint(footL, lowerLegL.GetComponent<Rigidbody>(), 300f, 30f, float.MaxValue);
            AddGroundSensor(footL);

            // ── Right Leg ────────────────────────────────────────────────────
            var upperLegR = CreateSegment("UpperLeg_R", _hipsGO,
                new Vector3(0.10f, -0.22f, 0f), 4f, 0.07f, 0.36f);
            ConfigureJoint(upperLegR, _hipsRb, 1200f, 120f, float.MaxValue);

            var lowerLegR = CreateSegment("LowerLeg_R", upperLegR,
                new Vector3(0f, -0.38f, 0f), 2.5f, 0.055f, 0.33f);
            ConfigureJoint(lowerLegR, upperLegR.GetComponent<Rigidbody>(), 1200f, 120f, float.MaxValue);

            var footR = CreateBoxSegment("Foot_R", lowerLegR,
                new Vector3(0f, -0.35f, 0.07f), 1f, new Vector3(0.10f, 0.07f, 0.22f));
            ConfigureJoint(footR, lowerLegR.GetComponent<Rigidbody>(), 300f, 30f, float.MaxValue);
            AddGroundSensor(footR);

            // Track all child rigidbodies.
            foreach (var rb in _hipsGO.GetComponentsInChildren<Rigidbody>(true))
            {
                if (!_allRigidbodies.Contains(rb))
                {
                    _allRigidbodies.Add(rb);
                }
            }

            // ── Components ───────────────────────────────────────────────────
            _hipsGO.AddComponent<RagdollSetup>();
            _balance        = _hipsGO.AddComponent<BalanceController>();
            _movement       = _hipsGO.AddComponent<PlayerMovement>();
            _characterState = _hipsGO.AddComponent<CharacterState>();
            _hipsGO.AddComponent<LegAnimator>();

            // Inject move override so tests aren't polluted by live input.
            _movement.SetMoveInputForTest(Vector2.zero);
        }

        private static GameObject CreateSegment(string name, GameObject parent,
            Vector3 localPos, float mass, float radius, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass                   = mass;
            rb.interpolation          = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            var col = go.AddComponent<CapsuleCollider>();
            col.radius    = radius;
            col.height    = height;
            col.direction = 1; // Y
            return go;
        }

        private static GameObject CreateBoxSegment(string name, GameObject parent,
            Vector3 localPos, float mass, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;

            var rb = go.AddComponent<Rigidbody>();
            rb.mass                   = mass;
            rb.interpolation          = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            var col = go.AddComponent<BoxCollider>();
            col.size = size;
            return go;
        }

        private static void ConfigureJoint(GameObject child, Rigidbody parentRb,
            float spring, float damper, float maxForce)
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
                maximumForce   = maxForce,
            };
            joint.targetRotation = Quaternion.identity;
            joint.enableCollision = false;
        }

        private static void AddGroundSensor(GameObject footGO)
        {
            var sensor = footGO.AddComponent<GroundSensor>();
            var field  = typeof(GroundSensor).GetField("_groundLayers",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            field?.SetValue(sensor, (LayerMask)(1 << GameSettings.LayerEnvironment));
        }
    }
}
