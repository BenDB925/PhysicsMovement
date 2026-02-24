using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Lap parameter optimizer â€” runs a 27-combination grid sweep across moveForce,
    /// maxSpeed, and kPYaw, scores each run on the lap circuit, and writes a ranked
    /// report to Logs/lap-optimizer-results.txt.
    ///
    /// ALL tests in this file are [Ignore] and excluded from standard CI. Run manually.
    ///
    /// Scoring: (gatesHit Ã— 100) - (fallCount Ã— 500) - (lapFrames / 10).
    /// Higher = better. Gates hit is the primary signal; falls are heavily penalised.
    ///
    /// Parameters swept (3 Ã— 3 Ã— 3 = 27 combinations):
    ///   moveForce : {200, 280, 360}
    ///   maxSpeed  : {4, 5, 6}
    ///   kPYaw     : {80, 120, 160}
    ///
    /// Rig construction mirrors LapCourseTests exactly â€” see that class for detailed
    /// rationale on foot-collider-free design and layer configuration.
    ///
    /// Collaborators: <see cref="BalanceController"/>, <see cref="PlayerMovement"/>,
    /// <see cref="LegAnimator"/>, <see cref="ArmAnimator"/>, <see cref="CharacterState"/>,
    /// <see cref="GroundSensor"/>, <see cref="RagdollSetup"/>.
    /// </summary>
    public class LapOptimizerTests
    {
        // â”€â”€ Course (identical to LapCourseTests â€” copied for isolation) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static readonly Vector3 TestOriginOffset = new Vector3(0f, 0f, 4000f);

        private static readonly Vector3[] CourseWaypoints = new Vector3[]
        {
            // (1) Start
            new Vector3( 0f,  0f,  0f),

            // (2) Long straight â€” 15 m north
            new Vector3( 0f,  0f,  5f),
            new Vector3( 0f,  0f, 10f),
            new Vector3( 0f,  0f, 15f),

            // (3) Tight 180Â° hairpin
            new Vector3( 4f,  0f, 15f),
            new Vector3( 8f,  0f, 15f),
            new Vector3(12f,  0f, 15f),
            new Vector3(16f,  0f, 15f),
            new Vector3(16f,  0f, 10f),

            // (4) Chicane â€” left-right-left
            new Vector3(12f,  0f,  8f),
            new Vector3(16f,  0f,  4f),
            new Vector3(12f,  0f,  0f),

            // (5) Slalom â€” 5 alternating gates
            new Vector3( 8f,  0f, -4f),
            new Vector3( 4f,  0f, -8f),
            new Vector3( 8f,  0f,-12f),
            new Vector3( 4f,  0f,-16f),
            new Vector3( 8f,  0f,-20f),

            // (6) S-bend
            new Vector3( 4f,  0f,-24f),
            new Vector3( 0f,  0f,-20f),
            new Vector3(-4f,  0f,-16f),
            new Vector3( 0f,  0f,-12f),

            // (7) Return to start
            new Vector3( 0f,  0f, -8f),
            new Vector3( 0f,  0f, -4f),
            new Vector3( 0f,  0f,  0f),
        };

        private const float TightGateRadius = 2.5f;
        private const float OpenGateRadius  = 3.5f;

        private static float GetGateRadius(int index)
        {
            if (index <= 3)  { return OpenGateRadius; }
            if (index <= 20) { return TightGateRadius; }
            return OpenGateRadius;
        }

        // â”€â”€ Timing â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private const int SettleFrames            = 200;
        private const int MaxLapFrames            = 6000;
        private const int GateMissedTimeoutFrames = 600;
        private const string ReportPath           = "Logs/lap-optimizer-results.txt";

        // â”€â”€ Layer constants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private const int LayerEnvironment   = GameSettings.LayerEnvironment;     // 12
        private const int LayerPlayer        = GameSettings.LayerPlayer1Parts;    // 8
        private const int LayerLowerLegParts = GameSettings.LayerLowerLegParts;  // 13

        // â”€â”€ Parameter grids â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static readonly float[] MoveForceSweep = { 200f, 280f, 360f };
        private static readonly float[] MaxSpeedSweep  = { 4f,   5f,   6f   };
        private static readonly float[] KPYawSweep     = { 80f,  120f, 160f };

        // â”€â”€ Shared state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private GameObject     _groundGO;
        private GameObject     _hipsGO;
        private Rigidbody      _hipsRb;
        private BalanceController _bc;
        private CharacterState _cs;
        private PlayerMovement _pm;

        private float _savedFixedDeltaTime;
        private int   _savedSolverIterations;
        private int   _savedSolverVelocityIterations;

        // â”€â”€ Setup / Teardown â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [SetUp]
        public void SetUp()
        {
            _savedFixedDeltaTime           = Time.fixedDeltaTime;
            _savedSolverIterations         = Physics.defaultSolverIterations;
            _savedSolverVelocityIterations = Physics.defaultSolverVelocityIterations;

            Time.fixedDeltaTime               = 0.01f;
            Physics.defaultSolverIterations   = 12;
            Physics.defaultSolverVelocityIterations = 4;

            Physics.IgnoreLayerCollision(LayerPlayer,        LayerEnvironment, false);
            Physics.IgnoreLayerCollision(LayerLowerLegParts, LayerEnvironment, true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_hipsGO   != null) { Object.Destroy(_hipsGO);   _hipsGO   = null; }
            if (_groundGO != null) { Object.Destroy(_groundGO); _groundGO = null; }

            Time.fixedDeltaTime                     = _savedFixedDeltaTime;
            Physics.defaultSolverIterations         = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
        }

        // â”€â”€ Optimizer test â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [UnityTest]
        [Timeout(3600000)]
        [Ignore("On-demand optimizer â€” excluded from standard CI run. " +
                "Remove [Ignore] or run via Unity Test Runner when tuning locomotion parameters.")]
        public IEnumerator LapOptimizer_SweepLocomotionParameters_WritesRankedReport()
        {
            var results = new List<TrialResult>();
            int total   = MoveForceSweep.Length * MaxSpeedSweep.Length * KPYawSweep.Length;
            int current = 0;

            foreach (float moveForce in MoveForceSweep)
            foreach (float maxSpeed  in MaxSpeedSweep)
            foreach (float kPYaw    in KPYawSweep)
            {
                current++;
                Debug.Log($"[LapOptimizer] Trial {current}/{total}: " +
                          $"moveForce={moveForce}  maxSpeed={maxSpeed}  kPYaw={kPYaw}");

                // Build fresh world each trial.
                CreateFlatGround();
                CreateCharacterRig();

                // Apply parameters via reflection (private serialized fields).
                SetFloatField(_pm,  "_moveForce", moveForce);
                SetFloatField(_pm,  "_maxSpeed",  maxSpeed);
                SetFloatField(_bc,  "_kPYaw",     kPYaw);

                // Settle.
                for (int i = 0; i < SettleFrames; i++)
                {
                    yield return new WaitForFixedUpdate();
                }

                // Run lap.
                var trial = new TrialResult
                {
                    MoveForce  = moveForce,
                    MaxSpeed   = maxSpeed,
                    KPYaw      = kPYaw,
                    TotalGates = CourseWaypoints.Length,
                };
                yield return RunGhostDriver(trial);

                results.Add(trial);

                // Clean up between trials.
                if (_hipsGO   != null) { Object.Destroy(_hipsGO);   _hipsGO   = null; }
                if (_groundGO != null) { Object.Destroy(_groundGO); _groundGO = null; }
            }

            // Sort by score descending.
            results.Sort((a, b) => b.Score.CompareTo(a.Score));

            // Write ranked report.
            var sb = new StringBuilder();
            sb.AppendLine("=== LAP OPTIMIZER RESULTS ===");
            sb.AppendLine($"Total trials : {results.Count}");
            sb.AppendLine($"Generated    : {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("Scoring: (gatesHitÃ—100) - (fallCountÃ—500) - (lapFrames/10)");
            sb.AppendLine("â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€");
            sb.AppendLine("RANKED RESULTS (all 27):");
            sb.AppendLine();

            for (int i = 0; i < results.Count; i++)
            {
                sb.AppendLine(results[i].ToReportLine(i + 1));
            }

            string dir = Path.GetDirectoryName(ReportPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(ReportPath, sb.ToString());

            Debug.Log($"[LapOptimizer] Done. Report written to {ReportPath}");
            Debug.Log($"[LapOptimizer] Best: {results[0].ToReportLine(1)}");

            Assert.Pass($"Optimizer complete. Best score: {results[0].Score}. See {ReportPath}");
        }

        // â”€â”€ Ghost driver â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private IEnumerator RunGhostDriver(TrialResult trial)
        {
            int totalGates     = CourseWaypoints.Length;
            int currentGate    = 1;
            int lapFrame       = 0;
            int framesSinceHit = 0;
            bool prevFallen    = false;

            while (lapFrame < MaxLapFrames)
            {
                lapFrame++;
                framesSinceHit++;

                bool fallen = (_cs != null && _cs.CurrentState == CharacterStateType.Fallen)
                           || _bc.IsFallen;
                if (fallen && !prevFallen)
                {
                    trial.FallCount++;
                }
                prevFallen = fallen;

                Vector3 targetWorld = CourseWaypoints[currentGate] + TestOriginOffset;
                Vector3 hipsXZ      = new Vector3(_hipsRb.position.x, 0f, _hipsRb.position.z);
                Vector3 targetXZ    = new Vector3(targetWorld.x,      0f, targetWorld.z);
                Vector3 toTarget    = targetXZ - hipsXZ;
                float   dist        = toTarget.magnitude;

                Vector2 moveInput = (dist > 0.01f)
                    ? new Vector2(toTarget.x / dist, toTarget.z / dist)
                    : Vector2.zero;

                _pm.SetMoveInputForTest(moveInput);

                if (dist <= GetGateRadius(currentGate))
                {
                    trial.GatesHit++;
                    framesSinceHit = 0;
                    currentGate++;

                    if (currentGate >= totalGates) { break; }
                }

                if (framesSinceHit >= GateMissedTimeoutFrames)
                {
                    trial.GatesMissed++;
                    framesSinceHit = 0;
                    currentGate++;

                    if (currentGate >= totalGates) { break; }
                }

                yield return new WaitForFixedUpdate();
            }

            trial.LapFrames = lapFrame;
            _pm.SetMoveInputForTest(Vector2.zero);
        }

        // â”€â”€ World construction â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void CreateFlatGround()
        {
            _groundGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _groundGO.name = "LapGround";
            _groundGO.transform.position   = new Vector3(0f, -0.5f, 0f) + TestOriginOffset;
            _groundGO.transform.localScale = new Vector3(60f, 1f, 60f);
            _groundGO.layer = LayerEnvironment;

            Renderer rend = _groundGO.GetComponent<Renderer>();
            if (rend != null) { Object.Destroy(rend); }
        }

        private void CreateCharacterRig()
        {
            Vector3 spawnPos = new Vector3(
                CourseWaypoints[0].x,
                1.0f,
                CourseWaypoints[0].z) + TestOriginOffset;

            _hipsGO = new GameObject("Hips_Lap");
            _hipsGO.transform.position = spawnPos;
            _hipsGO.layer = LayerPlayer;

            _hipsRb = _hipsGO.AddComponent<Rigidbody>();
            _hipsRb.mass                    = 10f;
            _hipsRb.interpolation           = RigidbodyInterpolation.Interpolate;
            _hipsRb.collisionDetectionMode  = CollisionDetectionMode.Continuous;

            BoxCollider hipsCol = _hipsGO.AddComponent<BoxCollider>();
            hipsCol.size = new Vector3(0.26f, 0.20f, 0.15f);

            // Torso
            GameObject torsoGO = CreateBoxSegment("Torso", _hipsGO, LayerPlayer,
                new Vector3(0f, 0.32f, 0f), 12f, new Vector3(0.28f, 0.32f, 0.14f));
            ConfigureJoint(torsoGO, _hipsRb, 300f, 30f, 1000f);

            // Left leg
            GameObject upperLegL = CreateCapsuleSegment("UpperLeg_L", _hipsGO, LayerPlayer,
                new Vector3(-0.10f, -0.22f, 0f), 4f, 0.07f, 0.36f);
            ConfigureJoint(upperLegL, _hipsRb, 1200f, 120f, 5000f);

            GameObject lowerLegL = CreateCapsuleSegment("LowerLeg_L", upperLegL, LayerPlayer,
                new Vector3(0f, -0.38f, 0f), 2.5f, 0.055f, 0.33f);
            ConfigureJoint(lowerLegL, upperLegL.GetComponent<Rigidbody>(), 1200f, 120f, 5000f);

            GameObject footL = CreateSensorOnlySegment("Foot_L", lowerLegL,
                new Vector3(0f, -0.35f, 0.07f));
            AddGroundSensor(footL);

            // Right leg
            GameObject upperLegR = CreateCapsuleSegment("UpperLeg_R", _hipsGO, LayerPlayer,
                new Vector3(0.10f, -0.22f, 0f), 4f, 0.07f, 0.36f);
            ConfigureJoint(upperLegR, _hipsRb, 1200f, 120f, 5000f);

            GameObject lowerLegR = CreateCapsuleSegment("LowerLeg_R", upperLegR, LayerPlayer,
                new Vector3(0f, -0.38f, 0f), 2.5f, 0.055f, 0.33f);
            ConfigureJoint(lowerLegR, upperLegR.GetComponent<Rigidbody>(), 1200f, 120f, 5000f);

            GameObject footR = CreateSensorOnlySegment("Foot_R", lowerLegR,
                new Vector3(0f, -0.35f, 0.07f));
            AddGroundSensor(footR);

            // Arms
            GameObject upperArmL = CreateCapsuleSegment("UpperArm_L", torsoGO, LayerPlayer,
                new Vector3(-0.20f, 0.10f, 0f), 2f, 0.055f, 0.28f);
            ConfigureJoint(upperArmL, torsoGO.GetComponent<Rigidbody>(), 800f, 80f, 3000f);

            GameObject lowerArmL = CreateCapsuleSegment("LowerArm_L", upperArmL, LayerPlayer,
                new Vector3(0f, -0.30f, 0f), 1.5f, 0.045f, 0.25f);
            ConfigureJoint(lowerArmL, upperArmL.GetComponent<Rigidbody>(), 100f, 10f, 400f);

            GameObject upperArmR = CreateCapsuleSegment("UpperArm_R", torsoGO, LayerPlayer,
                new Vector3(0.20f, 0.10f, 0f), 2f, 0.055f, 0.28f);
            ConfigureJoint(upperArmR, torsoGO.GetComponent<Rigidbody>(), 800f, 80f, 3000f);

            GameObject lowerArmR = CreateCapsuleSegment("LowerArm_R", upperArmR, LayerPlayer,
                new Vector3(0f, -0.30f, 0f), 1.5f, 0.045f, 0.25f);
            ConfigureJoint(lowerArmR, upperArmR.GetComponent<Rigidbody>(), 100f, 10f, 400f);

            // Components â€” RagdollSetup FIRST
            _hipsGO.AddComponent<RagdollSetup>();
            _bc = _hipsGO.AddComponent<BalanceController>();
            _pm = _hipsGO.AddComponent<PlayerMovement>();
            _cs = _hipsGO.AddComponent<CharacterState>();
            _hipsGO.AddComponent<LegAnimator>();
            _hipsGO.AddComponent<ArmAnimator>();
        }

        // â”€â”€ Build helpers â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static GameObject CreateBoxSegment(string name, GameObject parent, int layer,
            Vector3 localPos, float mass, Vector3 boxSize)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.layer = layer;

            Rigidbody rb  = go.AddComponent<Rigidbody>();
            rb.mass       = mass;
            rb.interpolation          = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            BoxCollider col = go.AddComponent<BoxCollider>();
            col.size = boxSize;
            return go;
        }

        private static GameObject CreateCapsuleSegment(string name, GameObject parent, int layer,
            Vector3 localPos, float mass, float radius, float height)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            go.layer = layer;

            Rigidbody rb  = go.AddComponent<Rigidbody>();
            rb.mass       = mass;
            rb.interpolation          = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

            CapsuleCollider col = go.AddComponent<CapsuleCollider>();
            col.radius    = radius;
            col.height    = height;
            col.direction = 1;
            return go;
        }

        private static GameObject CreateSensorOnlySegment(string name, GameObject parent,
            Vector3 localPos)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent.transform, worldPositionStays: false);
            go.transform.localPosition = localPos;
            return go;
        }

        private static void ConfigureJoint(GameObject child, Rigidbody parentRb,
            float spring, float damper, float maxForce)
        {
            ConfigurableJoint joint = child.AddComponent<ConfigurableJoint>();
            joint.connectedBody = parentRb;

            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;

            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;

            joint.lowAngularXLimit  = new SoftJointLimit { limit = -60f };
            joint.highAngularXLimit = new SoftJointLimit { limit =  60f };
            joint.angularYLimit     = new SoftJointLimit { limit =  30f };
            joint.angularZLimit     = new SoftJointLimit { limit =  30f };

            joint.anchor                       = Vector3.zero;
            joint.autoConfigureConnectedAnchor = true;
            joint.enableCollision              = false;
            joint.enablePreprocessing          = true;

            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = damper,
                maximumForce   = maxForce,
            };
            joint.targetRotation = Quaternion.identity;
        }

        private static void AddGroundSensor(GameObject footGO)
        {
            GroundSensor sensor = footGO.AddComponent<GroundSensor>();
            FieldInfo fi = typeof(GroundSensor).GetField(
                "_groundLayers",
                BindingFlags.NonPublic | BindingFlags.Instance);
            fi?.SetValue(sensor, (LayerMask)(1 << LayerEnvironment));
        }

        private static void SetFloatField(MonoBehaviour target, string fieldName, float value)
        {
            FieldInfo fi = target.GetType().GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(fi, Is.Not.Null,
                $"Field '{fieldName}' not found on {target.GetType().Name}. " +
                "Update LapOptimizerTests if the field was renamed.");
            fi.SetValue(target, value);
        }

        // â”€â”€ Trial result â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private class TrialResult
        {
            public float MoveForce;
            public float MaxSpeed;
            public float KPYaw;
            public int   GatesHit;
            public int   GatesMissed;
            public int   FallCount;
            public int   LapFrames;
            public int   TotalGates;

            /// <summary>
            /// Score = (gatesHit Ã— 100) âˆ’ (fallCount Ã— 500) âˆ’ (lapFrames / 10).
            /// Higher is better.
            /// </summary>
            public float Score =>
                (GatesHit * 100f) - (FallCount * 500f) - (LapFrames / 10f);

            public string ToReportLine(int rank)
            {
                float secs = LapFrames / 100f;
                return $"#{rank,3}  score={Score:F1}  gates={GatesHit}/{TotalGates}  " +
                       $"missed={GatesMissed}  falls={FallCount}  " +
                       $"lapTime={secs:F2}s ({LapFrames}fr)  |  " +
                       $"moveForce={MoveForce}  maxSpeed={MaxSpeed}  kPYaw={KPYaw}";
            }
        }
    }
}