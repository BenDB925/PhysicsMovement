using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode tests for wall-climb force in <see cref="PlayerMovement"/>.
    /// Verifies climb force application when jump is held during a wall grab,
    /// and that the force is gated correctly.
    /// </summary>
    public class WallClimbTests
    {
        private readonly List<GameObject> _cleanup = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject go in _cleanup)
            {
                if (go != null) Object.Destroy(go);
            }
            _cleanup.Clear();
        }

        private GameObject Track(GameObject go)
        {
            _cleanup.Add(go);
            return go;
        }

        /// <summary>
        /// Builds a rig with PlayerMovement, BalanceController, GrabController, and
        /// Hand_L/Hand_R with HandGrabZones. Hips is non-kinematic with no gravity
        /// so climb force can be measured cleanly.
        /// </summary>
        private PlayerMovement CreateClimbRig(out GameObject hipsGo,
            out GrabController gc, out HandGrabZone zoneL,
            out Rigidbody hipsRb, out BalanceController balance)
        {
            hipsGo = Track(new GameObject("Hips"));
            hipsGo.transform.position = new Vector3(0f, 50f, 0f);
            hipsRb = hipsGo.AddComponent<Rigidbody>();
            hipsRb.useGravity = false;
            hipsGo.AddComponent<BoxCollider>();

            // BalanceController (disabled so it doesn't apply its own forces).
            balance = hipsGo.AddComponent<BalanceController>();
            balance.SetGroundStateForTest(isGrounded: false, isFallen: false);
            balance.enabled = false;

            // Left arm chain: UpperArm_L -> LowerArm_L -> Hand_L
            GameObject upperArmL = CreateArmSegment("UpperArm_L", hipsGo);
            GameObject lowerArmL = CreateArmSegment("LowerArm_L", upperArmL);
            GameObject handL = CreateHandSegment("Hand_L", lowerArmL);

            // Right arm chain: UpperArm_R -> LowerArm_R -> Hand_R
            GameObject upperArmR = CreateArmSegment("UpperArm_R", hipsGo);
            GameObject lowerArmR = CreateArmSegment("LowerArm_R", upperArmR);
            GameObject handR = CreateHandSegment("Hand_R", lowerArmR);

            // Self-body exclusion.
            Rigidbody[] allBodies = hipsGo.GetComponentsInChildren<Rigidbody>(true);
            var selfBodies = new HashSet<Rigidbody>(allBodies);
            foreach (HandGrabZone z in hipsGo.GetComponentsInChildren<HandGrabZone>())
            {
                z.SetSelfBodiesForTest(selfBodies);
            }

            // GrabController.
            gc = hipsGo.AddComponent<GrabController>();
            zoneL = gc.ZoneL;

            // PlayerMovement — add last, disable to control when FixedUpdate runs.
            PlayerMovement movement = hipsGo.AddComponent<PlayerMovement>();
            movement.SetMoveInputForTest(Vector2.zero);
            movement.enabled = false;

            // Inject RagdollSetup-like AllBodies via reflection so climb force
            // distributes across bodies (or falls back to hips-only).
            // PlayerMovement reads _ragdollSetup in FixedUpdate; without a real
            // RagdollSetup, _ragdollSetup is null and it falls back to applying
            // force to just the Hips Rigidbody, which is fine for testing.

            return movement;
        }

        private GameObject CreateArmSegment(string name, GameObject parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = new Vector3(0f, -0.2f, 0f);
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            go.AddComponent<CapsuleCollider>();

            ConfigurableJoint joint = go.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parent.GetComponent<Rigidbody>();
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = new JointDrive
            {
                positionSpring = 100f,
                positionDamper = 10f,
                maximumForce = 400f
            };

            return go;
        }

        private GameObject CreateHandSegment(string name, GameObject parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform);
            go.transform.localPosition = new Vector3(0f, -0.2f, 0f);
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            go.AddComponent<BoxCollider>();

            ConfigurableJoint joint = go.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parent.GetComponent<Rigidbody>();
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = new JointDrive
            {
                positionSpring = 50f,
                positionDamper = 5f,
                maximumForce = 200f
            };

            SphereCollider trigger = go.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.15f;

            go.AddComponent<HandGrabZone>();
            return go;
        }

        private GameObject CreateStaticWall(Vector3 position, Vector3 scale)
        {
            GameObject wall = Track(new GameObject("StaticWall"));
            wall.transform.position = position;
            wall.transform.localScale = scale;
            wall.AddComponent<BoxCollider>();
            return wall;
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null,
                $"Could not find private field '{fieldName}' on {instance.GetType().Name}.");

            field.SetValue(instance, value);
        }

        // ─── Tests ──────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator ClimbForce_Applied_WhenJumpHeldAndWallGrabbing()
        {
            PlayerMovement movement = CreateClimbRig(out _, out GrabController gc,
                out HandGrabZone zoneL, out Rigidbody hipsRb, out _);

            // Place wall near Hand_L.
            CreateStaticWall(
                zoneL.transform.position + new Vector3(0.05f, 0f, 0f),
                new Vector3(0.1f, 0.5f, 0.5f));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Establish wall grab.
            gc.SetGrabInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsWallGrabbing, Is.True, "Precondition: should be wall-grabbing.");

            // Set climb force high enough to produce measurable velocity.
            SetPrivateField(movement, "_climbForce", 5000f);

            // Inject jump-held and enable movement.
            hipsRb.linearVelocity = Vector3.zero;
            movement.SetJumpHeldForTest(true);
            movement.enabled = true;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            movement.enabled = false;

            float velocityY = hipsRb.linearVelocity.y;
            Assert.That(velocityY, Is.GreaterThan(0.1f),
                $"Hips should gain upward velocity from climb force. Got velocityY={velocityY:F4}.");
        }

        [UnityTest]
        public IEnumerator ClimbForce_NotApplied_WhenNotWallGrabbing()
        {
            PlayerMovement movement = CreateClimbRig(out _, out GrabController gc,
                out _, out Rigidbody hipsRb, out _);

            yield return new WaitForFixedUpdate();

            // No wall grab established — GrabController.IsWallGrabbing is false.
            Assert.That(gc.IsWallGrabbing, Is.False, "Precondition: should NOT be wall-grabbing.");

            SetPrivateField(movement, "_climbForce", 5000f);

            hipsRb.linearVelocity = Vector3.zero;
            movement.SetJumpHeldForTest(true);
            movement.enabled = true;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            movement.enabled = false;

            float velocityY = hipsRb.linearVelocity.y;
            Assert.That(velocityY, Is.LessThan(0.5f),
                $"No climb force should be applied when not wall-grabbing. Got velocityY={velocityY:F4}.");
        }

        [UnityTest]
        public IEnumerator ClimbForce_NotApplied_WhenJumpNotHeld()
        {
            PlayerMovement movement = CreateClimbRig(out _, out GrabController gc,
                out HandGrabZone zoneL, out Rigidbody hipsRb, out _);

            CreateStaticWall(
                zoneL.transform.position + new Vector3(0.05f, 0f, 0f),
                new Vector3(0.1f, 0.5f, 0.5f));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Establish wall grab.
            gc.SetGrabInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsWallGrabbing, Is.True, "Precondition: should be wall-grabbing.");

            SetPrivateField(movement, "_climbForce", 5000f);

            // Jump NOT held.
            hipsRb.linearVelocity = Vector3.zero;
            movement.SetJumpHeldForTest(false);
            movement.enabled = true;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            movement.enabled = false;

            float velocityY = hipsRb.linearVelocity.y;
            Assert.That(velocityY, Is.LessThan(0.5f),
                $"No climb force should be applied when jump is not held. Got velocityY={velocityY:F4}.");
        }

        [UnityTest]
        public IEnumerator ClimbForce_StopsWhenWallGrabReleased()
        {
            PlayerMovement movement = CreateClimbRig(out _, out GrabController gc,
                out HandGrabZone zoneL, out Rigidbody hipsRb, out _);

            CreateStaticWall(
                zoneL.transform.position + new Vector3(0.05f, 0f, 0f),
                new Vector3(0.1f, 0.5f, 0.5f));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Establish wall grab.
            gc.SetGrabInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsWallGrabbing, Is.True, "Precondition: should be wall-grabbing.");

            SetPrivateField(movement, "_climbForce", 5000f);

            // Start climbing.
            movement.SetJumpHeldForTest(true);
            movement.enabled = true;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            float velocityWhileClimbing = hipsRb.linearVelocity.y;
            Assert.That(velocityWhileClimbing, Is.GreaterThan(0.1f),
                $"Precondition: should have upward velocity while climbing. Got {velocityWhileClimbing:F4}.");

            // Release wall grab.
            gc.SetGrabInputForTest(false);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsWallGrabbing, Is.False, "Should no longer be wall-grabbing.");

            // Zero velocity and check that no more climb force is applied.
            hipsRb.linearVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            movement.enabled = false;

            float velocityAfterRelease = hipsRb.linearVelocity.y;
            Assert.That(velocityAfterRelease, Is.LessThan(0.5f),
                $"Climb force should stop after wall grab is released. Got velocityY={velocityAfterRelease:F4}.");
        }
    }
}
