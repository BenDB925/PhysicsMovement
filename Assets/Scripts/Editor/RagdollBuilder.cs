using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;

namespace PhysicsDrivenMovement.Editor
{
    /// <summary>
    /// Editor utility that procedurally builds the PlayerRagdoll prefab from scratch.
    /// Creates the 15-body humanoid hierarchy, adds Rigidbodies with correct masses,
    /// shaped Colliders, ConfigurableJoints with anatomical angle limits, a
    /// shared PhysicsMaterial, and primitive mesh renderers for each body part.
    /// Each segment gets a child "Visual" GameObject with a MeshFilter + MeshRenderer
    /// using sphere/capsule/cube primitives sized to match the physics collider.
    /// Attach <see cref="RagdollSetup"/> at root.
    /// Access via: Tools → PhysicsDrivenMovement → Build Player Ragdoll.
    /// Collaborators: <see cref="RagdollSetup"/>.
    /// </summary>
    public static class RagdollBuilder
    {
        private const int DefaultPlayerPartsLayer = GameSettings.LayerPlayer1Parts;

        // ─── Body Segment Definition ─────────────────────────────────────────

        private enum ColliderShape { Box, Capsule, Sphere }

        /// <summary>Drive spring/damper profile applied to a joint's SLERP drive.</summary>
        private readonly struct JointDriveProfile
        {
            public readonly float Spring;
            public readonly float Damper;
            public readonly float MaxForce;

            public JointDriveProfile(float spring, float damper, float maxForce)
            {
                Spring = spring;
                Damper = damper;
                MaxForce = maxForce;
            }
        }

        /// <summary>Data bag describing one body segment.</summary>
        private readonly struct SegmentDef
        {
            public readonly string  Name;
            public readonly string  ParentName;
            public readonly float   Mass;
            public readonly Vector3 LocalPosition;

            // Collider
            public readonly ColliderShape Shape;
            public readonly Vector3       BoxSize;     // for Box
            public readonly float         CapsuleRadius;
            public readonly float         CapsuleHeight;
            public readonly int           CapsuleDir;  // 0=X, 1=Y, 2=Z
            public readonly float         SphereRadius;

            // Joint limits (degrees) — only used when ParentName != null.
            // Angular X is the primary hinge / most limited axis.
            public readonly float LowAngX;
            public readonly float HighAngX;
            public readonly float AngY;
            public readonly float AngZ;

            // Joint axis (primary) in child local space.
            public readonly Vector3 JointAxis;
            public readonly Vector3 JointSecondaryAxis;
            /// <summary>SLERP drive profile — spring, damper, and max force for this joint.</summary>
            public readonly JointDriveProfile DriveProfile;

            public SegmentDef(
                string name, string parent, float mass, Vector3 localPos,
                ColliderShape shape,
                Vector3 boxSize = default, float capsuleRadius = 0f,
                float capsuleHeight = 0f, int capsuleDir = 1, float sphereRadius = 0f,
                float lowAngX = -40f, float highAngX = 40f,
                float angY = 30f, float angZ = 30f,
                Vector3 jointAxis = default, Vector3 jointSecondaryAxis = default,
                JointDriveProfile driveProfile = default)
            {
                Name = name;
                ParentName = parent;
                Mass = mass;
                LocalPosition = localPos;
                Shape = shape;
                BoxSize = boxSize;
                CapsuleRadius = capsuleRadius;
                CapsuleHeight = capsuleHeight;
                CapsuleDir = capsuleDir;
                SphereRadius = sphereRadius;
                LowAngX = lowAngX;
                HighAngX = highAngX;
                AngY = angY;
                AngZ = angZ;
                JointAxis = jointAxis == default ? Vector3.right : jointAxis;
                JointSecondaryAxis = jointSecondaryAxis == default ? Vector3.up : jointSecondaryAxis;
                DriveProfile = driveProfile;
            }
        }

        // ─── Segment Table ─────────────────────────────────────────────────────
        // DESIGN: Centre-positioning convention — each GameObject sits at the centre
        //         of its body part. joint.anchor = zero; autoConfigureConnectedAnchor
        //         = true so Unity resolves the connected anchor in parent space.
        // Masses total ~49 kg; adjust Torso to reach desired total.

        private static readonly SegmentDef[] Segments = new[]
        {
            // ── Core ────────────────────────────────────────────────────────────
            new SegmentDef(
                name: "Hips", parent: null, mass: 10f,
                localPos: Vector3.zero,
                shape: ColliderShape.Box,
                boxSize: new Vector3(0.26f, 0.20f, 0.15f)),

            new SegmentDef(
                name: "Torso", parent: "Hips", mass: 12f,
                localPos: new Vector3(0f, 0.32f, 0f),
                shape: ColliderShape.Box,
                boxSize: new Vector3(0.28f, 0.32f, 0.14f),
                lowAngX: -40f, highAngX: 40f, angY: 30f, angZ: 25f,
                jointAxis: Vector3.right, jointSecondaryAxis: Vector3.up,
                driveProfile: new JointDriveProfile(650f, 65f, 2500f)),

            new SegmentDef(
                name: "Head", parent: "Torso", mass: 4f,
                localPos: new Vector3(0f, 0.27f, 0f),
                shape: ColliderShape.Sphere,
                sphereRadius: 0.11f,
                lowAngX: -40f, highAngX: 40f, angY: 35f, angZ: 20f,
                jointAxis: Vector3.right, jointSecondaryAxis: Vector3.up,
                driveProfile: new JointDriveProfile(150f, 15f, 500f)),

            // ── Left Arm ────────────────────────────────────────────────────────
            new SegmentDef(
                name: "UpperArm_L", parent: "Torso", mass: 2f,
                localPos: new Vector3(-0.23f, 0.08f, 0f),
                shape: ColliderShape.Capsule,
                capsuleRadius: 0.05f, capsuleHeight: 0.26f, capsuleDir: 1,
                lowAngX: -70f, highAngX: 70f, angY: 60f, angZ: 40f,
                jointAxis: Vector3.forward, jointSecondaryAxis: Vector3.up,
                driveProfile: new JointDriveProfile(100f, 10f, 400f)),

            new SegmentDef(
                name: "LowerArm_L", parent: "UpperArm_L", mass: 1.5f,
                localPos: new Vector3(0f, -0.27f, 0f),
                shape: ColliderShape.Capsule,
                capsuleRadius: 0.04f, capsuleHeight: 0.24f, capsuleDir: 1,
                lowAngX: 0f, highAngX: 140f, angY: 10f, angZ: 5f,
                jointAxis: Vector3.right, jointSecondaryAxis: Vector3.forward,
                driveProfile: new JointDriveProfile(80f, 8f, 300f)),

            new SegmentDef(
                name: "Hand_L", parent: "LowerArm_L", mass: 0.5f,
                localPos: new Vector3(0f, -0.22f, 0f),
                shape: ColliderShape.Box,
                boxSize: new Vector3(0.08f, 0.10f, 0.04f),
                lowAngX: -30f, highAngX: 30f, angY: 20f, angZ: 10f,
                jointAxis: Vector3.right, jointSecondaryAxis: Vector3.forward,
                driveProfile: new JointDriveProfile(50f, 5f, 200f)),

            // ── Right Arm ───────────────────────────────────────────────────────
            new SegmentDef(
                name: "UpperArm_R", parent: "Torso", mass: 2f,
                localPos: new Vector3(0.23f, 0.08f, 0f),
                shape: ColliderShape.Capsule,
                capsuleRadius: 0.05f, capsuleHeight: 0.26f, capsuleDir: 1,
                lowAngX: -70f, highAngX: 70f, angY: 60f, angZ: 40f,
                jointAxis: Vector3.forward, jointSecondaryAxis: Vector3.up,
                driveProfile: new JointDriveProfile(100f, 10f, 400f)),

            new SegmentDef(
                name: "LowerArm_R", parent: "UpperArm_R", mass: 1.5f,
                localPos: new Vector3(0f, -0.27f, 0f),
                shape: ColliderShape.Capsule,
                capsuleRadius: 0.04f, capsuleHeight: 0.24f, capsuleDir: 1,
                lowAngX: 0f, highAngX: 140f, angY: 10f, angZ: 5f,
                jointAxis: Vector3.right, jointSecondaryAxis: Vector3.forward,
                driveProfile: new JointDriveProfile(80f, 8f, 300f)),

            new SegmentDef(
                name: "Hand_R", parent: "LowerArm_R", mass: 0.5f,
                localPos: new Vector3(0f, -0.22f, 0f),
                shape: ColliderShape.Box,
                boxSize: new Vector3(0.08f, 0.10f, 0.04f),
                lowAngX: -30f, highAngX: 30f, angY: 20f, angZ: 10f,
                jointAxis: Vector3.right, jointSecondaryAxis: Vector3.forward,
                driveProfile: new JointDriveProfile(50f, 5f, 200f)),

            // ── Left Leg ────────────────────────────────────────────────────────
            new SegmentDef(
                name: "UpperLeg_L", parent: "Hips", mass: 4f,
                localPos: new Vector3(-0.10f, -0.22f, 0f),
                shape: ColliderShape.Capsule,
                capsuleRadius: 0.07f, capsuleHeight: 0.36f, capsuleDir: 1,
                lowAngX: -60f, highAngX: 60f, angY: 30f, angZ: 30f,
                jointAxis: Vector3.right, jointSecondaryAxis: Vector3.forward,
                driveProfile: new JointDriveProfile(650f, 65f, 2500f)),

            new SegmentDef(
                name: "LowerLeg_L", parent: "UpperLeg_L", mass: 2.5f,
                localPos: new Vector3(0f, -0.38f, 0f),
                shape: ColliderShape.Capsule,
                capsuleRadius: 0.055f, capsuleHeight: 0.33f, capsuleDir: 1,
                lowAngX: -120f, highAngX: 0f, angY: 5f, angZ: 5f,
                jointAxis: Vector3.right, jointSecondaryAxis: Vector3.forward,
                driveProfile: new JointDriveProfile(550f, 55f, 2200f)),

            new SegmentDef(
                name: "Foot_L", parent: "LowerLeg_L", mass: 1f,
                localPos: new Vector3(0f, -0.35f, 0.07f),
                shape: ColliderShape.Box,
                boxSize: new Vector3(0.10f, 0.07f, 0.22f),
                lowAngX: -30f, highAngX: 30f, angY: 15f, angZ: 10f,
                jointAxis: Vector3.right, jointSecondaryAxis: Vector3.forward,
                driveProfile: new JointDriveProfile(350f, 35f, 1200f)),

            // ── Right Leg ───────────────────────────────────────────────────────
            new SegmentDef(
                name: "UpperLeg_R", parent: "Hips", mass: 4f,
                localPos: new Vector3(0.10f, -0.22f, 0f),
                shape: ColliderShape.Capsule,
                capsuleRadius: 0.07f, capsuleHeight: 0.36f, capsuleDir: 1,
                lowAngX: -60f, highAngX: 60f, angY: 30f, angZ: 30f,
                jointAxis: Vector3.right, jointSecondaryAxis: Vector3.forward,
                driveProfile: new JointDriveProfile(650f, 65f, 2500f)),

            new SegmentDef(
                name: "LowerLeg_R", parent: "UpperLeg_R", mass: 2.5f,
                localPos: new Vector3(0f, -0.38f, 0f),
                shape: ColliderShape.Capsule,
                capsuleRadius: 0.055f, capsuleHeight: 0.33f, capsuleDir: 1,
                lowAngX: -120f, highAngX: 0f, angY: 5f, angZ: 5f,
                jointAxis: Vector3.right, jointSecondaryAxis: Vector3.forward,
                driveProfile: new JointDriveProfile(550f, 55f, 2200f)),

            new SegmentDef(
                name: "Foot_R", parent: "LowerLeg_R", mass: 1f,
                localPos: new Vector3(0f, -0.35f, 0.07f),
                shape: ColliderShape.Box,
                boxSize: new Vector3(0.10f, 0.07f, 0.22f),
                lowAngX: -30f, highAngX: 30f, angY: 15f, angZ: 10f,
                jointAxis: Vector3.right, jointSecondaryAxis: Vector3.forward,
                driveProfile: new JointDriveProfile(350f, 35f, 1200f)),
        };

        // ─── Menu Entry ────────────────────────────────────────────────────────

        [MenuItem("Tools/PhysicsDrivenMovement/Build Player Ragdoll")]
        public static void BuildRagdollPrefab()
        {
            const string prefabPath   = "Assets/Prefabs/PlayerRagdoll.prefab";
            const string materialPath = "Assets/PhysicsMaterials/Ragdoll.asset";
            const string bodyMatPath  = "Assets/Materials/RagdollBody.mat";

            // STEP 1: Create (or refresh) the physics material asset.
            PhysicsMaterial physicsMat = CreateOrLoadPhysicsMaterial(materialPath);

            // STEP 1b: Create (or refresh) the visual body material.
            Material bodyMat = CreateOrLoadBodyMaterial(bodyMatPath);

            // STEP 2: Build the GameObject hierarchy in memory.
            var goMap = new Dictionary<string, GameObject>(Segments.Length);

            GameObject rootGO = null;

            foreach (SegmentDef seg in Segments)
            {
                GameObject go = new GameObject(seg.Name);
                go.layer = DefaultPlayerPartsLayer;

                // STEP 2a: Add Rigidbody with correct mass and interpolation.
                Rigidbody rb = go.AddComponent<Rigidbody>();
                rb.mass             = seg.Mass;
                rb.interpolation    = RigidbodyInterpolation.Interpolate;
                rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

                // STEP 2b: Add collider shaped to the body part.
                AddCollider(go, seg, physicsMat);

                // STEP 2c: Add a child Visual mesh so there is something to see.
                AddMeshRenderer(go, seg, bodyMat);

                // STEP 2d: Parent under correct segment.
                if (seg.ParentName != null && goMap.TryGetValue(seg.ParentName, out GameObject parentGO))
                {
                    go.transform.SetParent(parentGO.transform, worldPositionStays: false);
                }

                go.transform.localPosition = seg.LocalPosition;
                go.transform.localRotation = Quaternion.identity;

                goMap[seg.Name] = go;

                if (seg.ParentName == null)
                {
                    rootGO = go;
                }
            }

            // STEP 3: Add ConfigurableJoints after all objects exist (connectedBody references need GOs).
            foreach (SegmentDef seg in Segments)
            {
                if (seg.ParentName == null)
                {
                    continue;
                }

                ConfigureJoint(goMap[seg.Name], goMap[seg.ParentName].GetComponent<Rigidbody>(), seg);
            }

            // STEP 4: Add runtime components to root (Hips).
            Debug.Assert(rootGO != null, "Root GO (Hips) was not created.");
            AttachRagdollSetup(rootGO);
            AttachBalanceController(rootGO);
            AttachPlayerMovement(rootGO);
            AttachCharacterState(rootGO);
            AttachLegAnimator(rootGO);
            AttachArmAnimator(rootGO);
            AttachDebugPushForce(rootGO);

            // STEP 4b: Add GroundSensor to both feet.
            AttachGroundSensor(goMap["Foot_L"]);
            AttachGroundSensor(goMap["Foot_R"]);

            // STEP 4c (Phase 4): Add HandGrabZone trigger spheres to both hands.
            AttachHandGrabZone(goMap["Hand_L"]);
            AttachHandGrabZone(goMap["Hand_R"]);

            // STEP 4d (Phase 4): Add GrabController and PunchController to Hips.
            AttachGrabController(rootGO);
            AttachPunchController(rootGO);

            // STEP 4e (Phase 4): Add HitReceiver to Head.
            AttachHitReceiver(goMap["Head"]);

            // STEP 5: Save as a prefab asset.
            string directory = System.IO.Path.GetDirectoryName(prefabPath);
            if (!AssetDatabase.IsValidFolder(directory))
            {
                AssetDatabase.CreateFolder(
                    System.IO.Path.GetDirectoryName(directory),
                    System.IO.Path.GetFileName(directory));
            }

            PrefabUtility.SaveAsPrefabAsset(rootGO, prefabPath);
            Object.DestroyImmediate(rootGO);

            AssetDatabase.Refresh();
            Debug.Log($"[RagdollBuilder] PlayerRagdoll prefab saved to '{prefabPath}'.");
        }

        // ─── Private Helpers ──────────────────────────────────────────────────

        // ─── Component Attach Helpers ───────────────────────────────────────
        // Each method adds a component and uses SerializedObject to write private
        // [SerializeField] values that match the tuned PlayerRagdoll prefab.

        private static void AttachRagdollSetup(GameObject go)
        {
            var comp = go.AddComponent<RagdollSetup>();
            using var so = new SerializedObject(comp);
            so.FindProperty("_debugSetup").boolValue = false;
            so.FindProperty("_upperLegSpring").floatValue = 1200f;
            so.FindProperty("_upperLegDamper").floatValue = 120f;
            so.FindProperty("_upperLegMaxForce").floatValue = 5000f;
            so.FindProperty("_lowerLegSpring").floatValue = 1200f;
            so.FindProperty("_lowerLegDamper").floatValue = 120f;
            so.FindProperty("_lowerLegMaxForce").floatValue = 5000f;
            so.FindProperty("_lowerLegLowAngularX").floatValue = -90f;
            so.FindProperty("_lowerLegHighAngularX").floatValue = 90f;
            so.FindProperty("_disableLowerLegGroundCollision").boolValue = true;
            so.FindProperty("_upperArmSpring").floatValue = 800f;
            so.FindProperty("_upperArmDamper").floatValue = 80f;
            so.FindProperty("_upperArmMaxForce").floatValue = 3000f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AttachBalanceController(GameObject go)
        {
            var comp = go.AddComponent<BalanceController>();
            using var so = new SerializedObject(comp);
            so.FindProperty("_kP").floatValue = 2000f;
            so.FindProperty("_kD").floatValue = 350f;
            so.FindProperty("_kPYaw").floatValue = 160f;
            so.FindProperty("_kDYaw").floatValue = 40f;
            so.FindProperty("_yawDeadZoneDeg").floatValue = 2f;
            so.FindProperty("_airborneMultiplier").floatValue = 0.2f;
            so.FindProperty("_fallenEnterAngleThreshold").floatValue = 65f;
            so.FindProperty("_fallenExitAngleThreshold").floatValue = 55f;
            so.FindProperty("_enableStartupStandAssist").boolValue = true;
            so.FindProperty("_startupStandAssistDuration").floatValue = 4f;
            so.FindProperty("_enablePersistentSeatedRecovery").boolValue = true;
            so.FindProperty("_persistentSeatedRecoveryAssistScale").floatValue = 0.35f;
            so.FindProperty("_persistentSeatedRecoveryMinAssistScale").floatValue = 0.55f;
            so.FindProperty("_startupAssistTargetHeight").floatValue = 0.9f;
            so.FindProperty("_startupStandAssistForce").floatValue = 1200f;
            so.FindProperty("_startupAssistHeightRange").floatValue = 0.35f;
            so.FindProperty("_startupAssistUseBodyUp").floatValue = 0.4f;
            so.FindProperty("_startupAssistMaxRiseSpeed").floatValue = 2f;
            so.FindProperty("_startupLegSpringMultiplier").floatValue = 2.25f;
            so.FindProperty("_startupLegDamperMultiplier").floatValue = 1.75f;
            so.FindProperty("_startupAssistLegForceFraction").floatValue = 0.8f;
            so.FindProperty("_deferLegJointsToAnimator").boolValue = true;
            so.FindProperty("_debugStateTransitions").boolValue = false;
            so.FindProperty("_debugRecoveryTelemetry").boolValue = false;
            so.FindProperty("_debugRecoveryTelemetryInterval").floatValue = 0.25f;
            so.FindProperty("_debugSeatedHeightThreshold").floatValue = 0.75f;
            so.FindProperty("_enableComStabilization").boolValue = true;
            so.FindProperty("_comStabilizationStrength").floatValue = 200f;
            so.FindProperty("_comStabilizationDamping").floatValue = 40f;
            so.FindProperty("_enableHeightMaintenance").boolValue = true;
            so.FindProperty("_standingHipsHeight").floatValue = 0.95f;
            so.FindProperty("_heightMaintenanceStrength").floatValue = 1500f;
            so.FindProperty("_heightMaintenanceDamping").floatValue = 160f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AttachPlayerMovement(GameObject go)
        {
            var comp = go.AddComponent<PlayerMovement>();
            using var so = new SerializedObject(comp);
            so.FindProperty("_moveForce").floatValue = 150f;
            so.FindProperty("_maxSpeed").floatValue = 5f;
            so.FindProperty("_jumpForce").floatValue = 100f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AttachCharacterState(GameObject go)
        {
            var comp = go.AddComponent<CharacterState>();
            using var so = new SerializedObject(comp);
            so.FindProperty("_moveEnterThreshold").floatValue = 0.1f;
            so.FindProperty("_moveExitThreshold").floatValue = 0.05f;
            so.FindProperty("_getUpDelay").floatValue = 0.5f;
            so.FindProperty("_knockoutDuration").floatValue = 1.5f;
            so.FindProperty("_getUpForce").floatValue = 250f;
            so.FindProperty("_getUpTimeout").floatValue = 3f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AttachLegAnimator(GameObject go)
        {
            var comp = go.AddComponent<LegAnimator>();
            using var so = new SerializedObject(comp);
            so.FindProperty("_stepAngle").floatValue = 60f;
            so.FindProperty("_stepFrequencyScale").floatValue = 0.1f;
            so.FindProperty("_stepFrequency").floatValue = 1.25f;
            so.FindProperty("_kneeAngle").floatValue = 65f;
            so.FindProperty("_upperLegLiftBoost").floatValue = 45f;
            so.FindProperty("_idleBlendSpeed").floatValue = 5f;
            so.FindProperty("_yawAlignThresholdDeg").floatValue = 90f;
            so.FindProperty("_swingAxis").vector3Value = Vector3.right;
            so.FindProperty("_kneeAxis").vector3Value = Vector3.right;
            so.FindProperty("_useWorldSpaceSwing").boolValue = false;
            so.FindProperty("_debugLog").boolValue = false;
            so.FindProperty("_airborneSpringMultiplier").floatValue = 0.15f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AttachArmAnimator(GameObject go)
        {
            var comp = go.AddComponent<ArmAnimator>();
            using var so = new SerializedObject(comp);
            so.FindProperty("_armSwingAngle").floatValue = 39.8f;
            so.FindProperty("_elbowBendAngle").floatValue = 21.2f;
            so.FindProperty("_armSwingScale").floatValue = 0.785f;
            so.FindProperty("_armSwingAxis").vector3Value = Vector3.forward;
            so.FindProperty("_elbowAxis").vector3Value = Vector3.right;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AttachDebugPushForce(GameObject go)
        {
            var comp = go.AddComponent<DebugPushForce>();
            using var so = new SerializedObject(comp);
            so.FindProperty("_enableDebugKeys").boolValue = true;
            so.FindProperty("_smallPushForce").floatValue = 200f;
            so.FindProperty("_largePushForce").floatValue = 800f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AttachGroundSensor(GameObject footGO)
        {
            var sensor = footGO.AddComponent<GroundSensor>();
            using var so = new SerializedObject(sensor);
            so.FindProperty("_groundLayers").intValue = 1 << GameSettings.LayerEnvironment;
            so.FindProperty("_castRadius").floatValue = 0.08f;
            so.FindProperty("_castDistance").floatValue = 0.25f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AttachHandGrabZone(GameObject handGO)
        {
            SphereCollider trigger = handGO.AddComponent<SphereCollider>();
            trigger.isTrigger = true;
            trigger.radius = 0.15f;

            var zone = handGO.AddComponent<HandGrabZone>();
            using var so = new SerializedObject(zone);
            so.FindProperty("_detectionRadius").floatValue = 0.15f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AttachGrabController(GameObject go)
        {
            var comp = go.AddComponent<GrabController>();
            using var so = new SerializedObject(comp);
            so.FindProperty("_grabBreakForce").floatValue = 2000f;
            so.FindProperty("_grabBreakTorque").floatValue = 2000f;
            so.FindProperty("_grabArmSpringMultiplier").floatValue = 3f;
            so.FindProperty("_grabArmDamperMultiplier").floatValue = 2f;
            so.FindProperty("_throwForceMultiplier").floatValue = 10f;
            so.FindProperty("_throwMinSpeed").floatValue = 1f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AttachPunchController(GameObject go)
        {
            var comp = go.AddComponent<PunchController>();
            using var so = new SerializedObject(comp);
            so.FindProperty("_punchImpulse").floatValue = 80f;
            so.FindProperty("_punchDuration").floatValue = 0.3f;
            so.FindProperty("_punchCooldown").floatValue = 0.5f;
            so.FindProperty("_punchArmSpringMultiplier").floatValue = 4f;
            so.FindProperty("_punchArmDamperMultiplier").floatValue = 2f;
            so.FindProperty("_punchTargetAngle").floatValue = 60f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void AttachHitReceiver(GameObject go)
        {
            var comp = go.AddComponent<HitReceiver>();
            using var so = new SerializedObject(comp);
            so.FindProperty("_knockoutVelocityThreshold").floatValue = 8f;
            so.FindProperty("_knockoutDuration").floatValue = 3f;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ─── Primitive Mesh Cache ─────────────────────────────────────────────
        // Avoids creating many temporary primitives during a single build pass.
        private static readonly Dictionary<PrimitiveType, Mesh> s_meshCache = new();

        private static Mesh GetPrimitiveMesh(PrimitiveType type)
        {
            if (s_meshCache.TryGetValue(type, out Mesh cached))
                return cached;

            GameObject temp = GameObject.CreatePrimitive(type);
            Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
            Object.DestroyImmediate(temp);
            s_meshCache[type] = mesh;
            return mesh;
        }

        /// <summary>
        /// Adds a child "Visual" GameObject to <paramref name="go"/> carrying a
        /// MeshFilter and MeshRenderer sized to visually match the physics collider.
        /// A child is used so that its local scale does not affect sibling or child
        /// segment positions in the hierarchy.
        /// </summary>
        private static void AddMeshRenderer(GameObject go, in SegmentDef seg, Material material)
        {
            GameObject visual = new GameObject("Visual");
            visual.transform.SetParent(go.transform, worldPositionStays: false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.layer = go.layer;

            MeshFilter   mf = visual.AddComponent<MeshFilter>();
            MeshRenderer mr = visual.AddComponent<MeshRenderer>();
            mr.sharedMaterial = material;

            switch (seg.Shape)
            {
                case ColliderShape.Box:
                    // Unity cube primitive is 1×1×1 at scale 1.
                    mf.sharedMesh = GetPrimitiveMesh(PrimitiveType.Cube);
                    visual.transform.localScale = seg.BoxSize;
                    break;

                case ColliderShape.Capsule:
                    // Unity capsule primitive: total height = 2, radius = 0.5 at scale 1.
                    mf.sharedMesh = GetPrimitiveMesh(PrimitiveType.Capsule);
                    float xzScale = seg.CapsuleRadius * 2f;
                    float yScale  = seg.CapsuleHeight * 0.5f;
                    switch (seg.CapsuleDir)
                    {
                        case 0: // X-axis
                            visual.transform.localScale    = new Vector3(yScale, xzScale, xzScale);
                            visual.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                            break;
                        case 2: // Z-axis
                            visual.transform.localScale    = new Vector3(xzScale, xzScale, yScale);
                            visual.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                            break;
                        default: // Y-axis (dir == 1)
                            visual.transform.localScale = new Vector3(xzScale, yScale, xzScale);
                            break;
                    }
                    break;

                case ColliderShape.Sphere:
                    // Unity sphere primitive: radius = 0.5 at scale 1.
                    mf.sharedMesh = GetPrimitiveMesh(PrimitiveType.Sphere);
                    float diameter = seg.SphereRadius * 2f;
                    visual.transform.localScale = Vector3.one * diameter;
                    break;

                default:
                    Debug.LogError($"[RagdollBuilder] Unknown ColliderShape on '{seg.Name}' — no visual added.");
                    Object.DestroyImmediate(visual);
                    break;
            }
        }

        /// <summary>
        /// Creates or loads a simple lit material for the ragdoll body. Uses a warm
        /// skin-tone colour so the character reads clearly in the Scene view.
        /// </summary>
        private static Material CreateOrLoadBodyMaterial(string assetPath)
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
            if (mat != null)
                return mat;

            // URP Lit shader with a fallback to the Standard shader if URP is absent.
            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");

            mat = new Material(shader)
            {
                name  = "RagdollBody",
                color = new Color(0.85f, 0.69f, 0.55f), // warm skin tone
            };

            string directory = System.IO.Path.GetDirectoryName(assetPath);
            if (!AssetDatabase.IsValidFolder(directory))
            {
                AssetDatabase.CreateFolder(
                    System.IO.Path.GetDirectoryName(directory),
                    System.IO.Path.GetFileName(directory));
            }

            AssetDatabase.CreateAsset(mat, assetPath);
            return mat;
        }

        /// <summary>
        /// Creates a new Ragdoll physics material or loads the existing one.
        /// Static friction 0.6, dynamic friction 0.4, minimal bounciness.
        /// </summary>
        private static PhysicsMaterial CreateOrLoadPhysicsMaterial(string assetPath)
        {
            PhysicsMaterial mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(assetPath);
            if (mat != null)
            {
                return mat;
            }

            mat = new PhysicsMaterial("Ragdoll")
            {
                staticFriction    = 0.6f,
                dynamicFriction   = 0.4f,
                bounciness        = 0f,
                frictionCombine   = PhysicsMaterialCombine.Average,
                bounceCombine     = PhysicsMaterialCombine.Average
            };

            AssetDatabase.CreateAsset(mat, assetPath);
            return mat;
        }

        /// <summary>
        /// Adds the correct collider component to <paramref name="go"/> based on the
        /// segment definition, and assigns the shared <paramref name="physicsMat"/>.
        /// </summary>
        private static void AddCollider(GameObject go, in SegmentDef seg, PhysicsMaterial physicsMat)
        {
            Collider col;

            switch (seg.Shape)
            {
                case ColliderShape.Box:
                    BoxCollider box = go.AddComponent<BoxCollider>();
                    box.size = seg.BoxSize;
                    col = box;
                    break;

                case ColliderShape.Capsule:
                    CapsuleCollider capsule = go.AddComponent<CapsuleCollider>();
                    capsule.radius    = seg.CapsuleRadius;
                    capsule.height    = seg.CapsuleHeight;
                    capsule.direction = seg.CapsuleDir;
                    col = capsule;
                    break;

                case ColliderShape.Sphere:
                    SphereCollider sphere = go.AddComponent<SphereCollider>();
                    sphere.radius = seg.SphereRadius;
                    col = sphere;
                    break;

                default:
                    Debug.LogError($"[RagdollBuilder] Unknown ColliderShape on '{seg.Name}'.");
                    return;
            }

            col.material = physicsMat;
        }

        /// <summary>
        /// Adds and configures a ConfigurableJoint on <paramref name="childGO"/>, connecting
        /// it to <paramref name="parentRb"/>. Locks all linear axes, limits all angular axes
        /// using anatomically derived angle limits from <paramref name="seg"/>.
        /// </summary>
        private static void ConfigureJoint(
            GameObject childGO, Rigidbody parentRb, in SegmentDef seg)
        {
            ConfigurableJoint joint = childGO.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parentRb;

            // Lock translation — ragdoll segments do not translate relative to their parent.
            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;

            // Limit rotation on all three axes with anatomical ranges.
            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;

            // Primary axis (X) — asymmetric limits allow one-directional joints (e.g. knee).
            joint.lowAngularXLimit  = new SoftJointLimit { limit = seg.LowAngX };
            joint.highAngularXLimit = new SoftJointLimit { limit = seg.HighAngX };

            // Secondary axes — symmetric limits.
            joint.angularYLimit = new SoftJointLimit { limit = seg.AngY };
            joint.angularZLimit = new SoftJointLimit { limit = seg.AngZ };

            // Joint orientation axes.
            joint.axis          = seg.JointAxis;
            joint.secondaryAxis = seg.JointSecondaryAxis;

            // Auto-configure anchors so the joint pivot is at the child's origin
            // (centre of body part) within parent space.
            joint.anchor          = Vector3.zero;
            joint.autoConfigureConnectedAnchor = true;

            // DESIGN: enableCollision = false because RagdollSetup.DisableNeighboringCollisions
            //         handles collision ignoring explicitly. Enabling it here would conflict.
            joint.enableCollision = false;

            // Enable preprocessing to improve joint solver stability under large forces.
            joint.enablePreprocessing = true;

            // Projection: snap bodies back if they drift beyond threshold (safety net).
            joint.projectionMode          = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance      = 0.1f;
            joint.projectionAngle         = 5f;

            // Apply SLERP drive so joints resist displacement and can be controlled
            // by downstream runtime systems (BalanceController, LegAnimator, etc.).
            // targetRotation = Quaternion.identity means "hold the rest angle" — the
            // angle the joint was at when it was created (joint-local, NOT world space).
            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = new JointDrive
            {
                positionSpring = seg.DriveProfile.Spring,
                positionDamper = seg.DriveProfile.Damper,
                maximumForce = seg.DriveProfile.MaxForce,
            };
            joint.targetRotation = Quaternion.identity;
        }
    }
}
