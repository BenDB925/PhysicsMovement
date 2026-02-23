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
    /// PlayMode tests for <see cref="ArmAnimator"/> raise-hands feature.
    /// Verifies that holding raise input drives arm targets away from identity,
    /// releasing returns them, wall-grabbing arm is skipped, and dynamic-grabbing
    /// arm is still driven.
    /// </summary>
    public class RaiseHandsTests
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
        /// Builds a minimal rig with Hips + UpperArm_L/R + LowerArm_L/R (ConfigurableJoints),
        /// ArmAnimator, and LegAnimator (required dependency). Returns the ArmAnimator.
        /// </summary>
        private ArmAnimator CreateRaiseRig(out GameObject hipsGo,
            out ConfigurableJoint upperArmL, out ConfigurableJoint upperArmR,
            out ConfigurableJoint lowerArmL, out ConfigurableJoint lowerArmR)
        {
            hipsGo = Track(new GameObject("Hips"));
            hipsGo.transform.position = new Vector3(0f, 50f, 0f);
            Rigidbody hipsRb = hipsGo.AddComponent<Rigidbody>();
            hipsRb.isKinematic = true;
            hipsGo.AddComponent<BoxCollider>();

            // UpperArm_L
            upperArmL = CreateArmJoint("UpperArm_L", hipsGo);
            // LowerArm_L
            lowerArmL = CreateArmJoint("LowerArm_L", upperArmL.gameObject);
            // UpperArm_R
            upperArmR = CreateArmJoint("UpperArm_R", hipsGo);
            // LowerArm_R
            lowerArmR = CreateArmJoint("LowerArm_R", upperArmR.gameObject);

            // LegAnimator is a required sibling for ArmAnimator.
            hipsGo.AddComponent<LegAnimator>();

            // ArmAnimator.
            ArmAnimator animator = hipsGo.AddComponent<ArmAnimator>();

            return animator;
        }

        /// <summary>
        /// Extends the raise rig with GrabController + HandGrabZones for wall-grab tests.
        /// </summary>
        private ArmAnimator CreateRaiseRigWithGrab(out GameObject hipsGo,
            out ConfigurableJoint upperArmL, out ConfigurableJoint upperArmR,
            out GrabController gc, out HandGrabZone zoneL, out HandGrabZone zoneR)
        {
            hipsGo = Track(new GameObject("Hips"));
            hipsGo.transform.position = new Vector3(0f, 50f, 0f);
            Rigidbody hipsRb = hipsGo.AddComponent<Rigidbody>();
            hipsRb.isKinematic = true;
            hipsGo.AddComponent<BoxCollider>();

            // Left arm chain: UpperArm_L -> LowerArm_L -> Hand_L
            GameObject upperArmLGo = CreateArmSegmentGo("UpperArm_L", hipsGo);
            upperArmL = upperArmLGo.GetComponent<ConfigurableJoint>();
            GameObject lowerArmLGo = CreateArmSegmentGo("LowerArm_L", upperArmLGo);
            GameObject handLGo = CreateHandSegmentGo("Hand_L", lowerArmLGo);

            // Right arm chain: UpperArm_R -> LowerArm_R -> Hand_R
            GameObject upperArmRGo = CreateArmSegmentGo("UpperArm_R", hipsGo);
            upperArmR = upperArmRGo.GetComponent<ConfigurableJoint>();
            GameObject lowerArmRGo = CreateArmSegmentGo("LowerArm_R", upperArmRGo);
            GameObject handRGo = CreateHandSegmentGo("Hand_R", lowerArmRGo);

            // Self-body exclusion.
            Rigidbody[] allBodies = hipsGo.GetComponentsInChildren<Rigidbody>(true);
            var selfBodies = new HashSet<Rigidbody>(allBodies);

            zoneL = handLGo.GetComponent<HandGrabZone>();
            zoneR = handRGo.GetComponent<HandGrabZone>();
            zoneL.SetSelfBodiesForTest(selfBodies);
            zoneR.SetSelfBodiesForTest(selfBodies);

            // GrabController must be added before ArmAnimator so Awake ordering works.
            gc = hipsGo.AddComponent<GrabController>();

            // LegAnimator (required by ArmAnimator).
            hipsGo.AddComponent<LegAnimator>();

            // ArmAnimator.
            ArmAnimator animator = hipsGo.AddComponent<ArmAnimator>();

            return animator;
        }

        private ConfigurableJoint CreateArmJoint(string name, GameObject parent)
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

            return joint;
        }

        private GameObject CreateArmSegmentGo(string name, GameObject parent)
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

        private GameObject CreateHandSegmentGo(string name, GameObject parent)
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

        private GameObject CreateExternalTarget(Vector3 position, string name = "Target")
        {
            GameObject go = Track(new GameObject(name));
            go.transform.position = position;
            Rigidbody rb = go.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            go.AddComponent<SphereCollider>();
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

        /// <summary>
        /// Returns the angle in degrees between a quaternion and identity.
        /// </summary>
        private static float AngleFromIdentity(Quaternion q)
        {
            return Quaternion.Angle(Quaternion.identity, q);
        }

        // ─── Tests ──────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator RaiseInput_DrivesArmsAwayFromIdentity()
        {
            ArmAnimator animator = CreateRaiseRig(out _, out ConfigurableJoint upperArmL,
                out ConfigurableJoint upperArmR, out _, out _);

            yield return null; // Let Awake complete.

            // Set arms to identity baseline.
            upperArmL.targetRotation = Quaternion.identity;
            upperArmR.targetRotation = Quaternion.identity;

            // Speed up the transition for test purposes.
            SetPrivateField(animator, "_raiseTransitionTime", 0.05f);

            // Hold raise input.
            animator.SetRaiseInputForTest(true);

            // Run enough frames for the transition to complete.
            for (int i = 0; i < 20; i++)
                yield return new WaitForFixedUpdate();

            float leftAngle = AngleFromIdentity(upperArmL.targetRotation);
            float rightAngle = AngleFromIdentity(upperArmR.targetRotation);

            Assert.That(leftAngle, Is.GreaterThan(10f),
                $"Left upper arm should be driven away from identity. Angle={leftAngle:F1}.");
            Assert.That(rightAngle, Is.GreaterThan(10f),
                $"Right upper arm should be driven away from identity. Angle={rightAngle:F1}.");
        }

        [UnityTest]
        public IEnumerator RaiseInput_Released_ArmsReturnTowardIdentity()
        {
            ArmAnimator animator = CreateRaiseRig(out _, out ConfigurableJoint upperArmL,
                out ConfigurableJoint upperArmR, out _, out _);

            yield return null;

            SetPrivateField(animator, "_raiseTransitionTime", 0.05f);

            // Raise arms fully.
            animator.SetRaiseInputForTest(true);
            for (int i = 0; i < 20; i++)
                yield return new WaitForFixedUpdate();

            float raisedAngle = AngleFromIdentity(upperArmL.targetRotation);
            Assert.That(raisedAngle, Is.GreaterThan(10f),
                $"Precondition: arms should be raised. Angle={raisedAngle:F1}.");

            // Release raise input.
            animator.SetRaiseInputForTest(false);
            for (int i = 0; i < 20; i++)
                yield return new WaitForFixedUpdate();

            float returnedAngleL = AngleFromIdentity(upperArmL.targetRotation);
            float returnedAngleR = AngleFromIdentity(upperArmR.targetRotation);

            Assert.That(returnedAngleL, Is.LessThan(5f),
                $"Left arm should return toward identity after release. Angle={returnedAngleL:F1}.");
            Assert.That(returnedAngleR, Is.LessThan(5f),
                $"Right arm should return toward identity after release. Angle={returnedAngleR:F1}.");
        }

        [UnityTest]
        public IEnumerator RaisePose_SkipsWallGrabbingArm()
        {
            ArmAnimator animator = CreateRaiseRigWithGrab(out _, out ConfigurableJoint upperArmL,
                out ConfigurableJoint upperArmR, out GrabController gc,
                out HandGrabZone zoneL, out _);

            // Place a wall near Hand_L only.
            CreateStaticWall(
                zoneL.transform.position + new Vector3(0.05f, 0f, 0f),
                new Vector3(0.1f, 0.5f, 0.5f));

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Grab wall with left hand only.
            gc.SetGrabInputForTest(leftHeld: true, rightHeld: false);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsWallGrabbingLeft, Is.True,
                "Precondition: left hand should be wall-grabbing.");

            SetPrivateField(animator, "_raiseTransitionTime", 0.05f);

            // Record left arm baseline after wall grab.
            upperArmL.targetRotation = Quaternion.identity;
            upperArmR.targetRotation = Quaternion.identity;

            // Hold raise input.
            animator.SetRaiseInputForTest(true);
            for (int i = 0; i < 20; i++)
                yield return new WaitForFixedUpdate();

            float leftAngle = AngleFromIdentity(upperArmL.targetRotation);
            float rightAngle = AngleFromIdentity(upperArmR.targetRotation);

            // Wall-grabbing arm (left) should NOT be overridden.
            Assert.That(leftAngle, Is.LessThan(5f),
                $"Wall-grabbing left arm should NOT be driven to raised pose. Angle={leftAngle:F1}.");
            // Free arm (right) SHOULD be raised.
            Assert.That(rightAngle, Is.GreaterThan(10f),
                $"Free right arm should be driven to raised pose. Angle={rightAngle:F1}.");
        }

        [UnityTest]
        public IEnumerator RaisePose_StillDrivesArmGrabbingDynamicObject()
        {
            ArmAnimator animator = CreateRaiseRigWithGrab(out _, out ConfigurableJoint upperArmL,
                out ConfigurableJoint upperArmR, out GrabController gc,
                out HandGrabZone zoneL, out _);

            // Place a dynamic target near Hand_L (not a wall).
            CreateExternalTarget(
                zoneL.transform.position + new Vector3(0.05f, 0f, 0f), "DynamicProp");

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Grab the dynamic object with left hand only.
            gc.SetGrabInputForTest(leftHeld: true, rightHeld: false);
            yield return new WaitForFixedUpdate();

            Assert.That(gc.IsGrabbingLeft, Is.True,
                "Precondition: left hand should be grabbing.");
            Assert.That(gc.IsWallGrabbingLeft, Is.False,
                "Precondition: left hand should NOT be wall-grabbing (dynamic target).");

            SetPrivateField(animator, "_raiseTransitionTime", 0.05f);

            // Hold raise input.
            animator.SetRaiseInputForTest(true);
            for (int i = 0; i < 20; i++)
                yield return new WaitForFixedUpdate();

            float leftAngle = AngleFromIdentity(upperArmL.targetRotation);
            float rightAngle = AngleFromIdentity(upperArmR.targetRotation);

            // Both arms should be driven to raised pose — dynamic grab does NOT skip.
            Assert.That(leftAngle, Is.GreaterThan(10f),
                $"Arm grabbing dynamic object should still be driven to raised pose. Angle={leftAngle:F1}.");
            Assert.That(rightAngle, Is.GreaterThan(10f),
                $"Free right arm should be driven to raised pose. Angle={rightAngle:F1}.");
        }
    }
}
