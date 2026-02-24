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
    /// Focused regression tests for the tight 180Â° hairpin corner â€” the section where
    /// the character consistently gets stuck when arriving at top speed.
    ///
    /// These tests isolate the hairpin (gates 4â€“8 of the lap course) so we don't have
    /// to run a full 40s lap to get a signal. The character spawns at full running
    /// approach speed (via a short run-up straight), then drives through the hairpin.
    ///
    /// Gate layout (mirrored from LapCourseTests):
    ///   Gate 0  ( 0, 0,  10)  run-up start
    ///   Gate 1  ( 0, 0,  15)  hairpin entry (top of straight)
    ///   Gate 2  ( 4, 0,  15)  hairpin approach
    ///   Gate 3  ( 8, 0,  15)  hairpin apex
    ///   Gate 4  (12, 0,  15)  hairpin exit
    ///   Gate 5  (16, 0,  15)  post-hairpin wide
    ///   Gate 6  (16, 0,  10)  post-hairpin turn  â† the stuck gate
    ///
    /// The character must pass all 7 gates with no falls and no stuck-leg loops.
    /// </summary>
    public class HairpinCornerTests
    {
        // â”€â”€ Course geometry â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static readonly Vector3 TestOriginOffset = new Vector3(0f, 0f, 4000f);

        // Short run-up into the full hairpin sequence.
        private static readonly Vector3[] HairpinWaypoints = new Vector3[]
        {
            new Vector3( 0f, 0f,  0f),   // 0: spawn / run-up start
            new Vector3( 0f, 0f,  5f),   // 1: mid run-up
            new Vector3( 0f, 0f, 10f),   // 2: hairpin entry
            new Vector3( 0f, 0f, 15f),   // 3: top of straight
            new Vector3( 4f, 0f, 15f),   // 4: hairpin approach
            new Vector3( 8f, 0f, 15f),   // 5: apex
            new Vector3(12f, 0f, 15f),   // 6: exit
            new Vector3(16f, 0f, 15f),   // 7: post-hairpin wide
            new Vector3(16f, 0f, 10f),   // 8: POST-HAIRPIN TURN â€” the stuck gate
            new Vector3(16f, 0f,  5f),   // 9: confirm he's moving away cleanly
        };

        private const float TightGateRadius = 2.5f;
        private const float OpenGateRadius  = 3.5f;

        private static float GetGateRadius(int i)
        {
            if (i <= 2)  return OpenGateRadius;   // run-up
            if (i <= 8)  return TightGateRadius;  // hairpin + post-hairpin turn
            return OpenGateRadius;
        }

        // â”€â”€ Timing â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private const int SettleFrames          = 200;   // 2 s @ 100 Hz
        private const int MaxFrames             = 3000;  // 30 s budget â€” plenty for this section
        private const int GateMissedTimeout     = 400;   // 4 s per gate before counting miss
        private const int BudgetFrames          = 2000;  // 20 s pass/fail assertion

        // â”€â”€ Rig constants â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private const float HipsSpawnHeight    = 1.0f;
        private const int   LayerEnvironment   = GameSettings.LayerEnvironment;
        private const int   LayerPlayer        = GameSettings.LayerPlayer1Parts;
        private const int   LayerLowerLegParts = GameSettings.LayerLowerLegParts;

        // â”€â”€ Shared state â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private GameObject    _groundGO;
        private GameObject    _hipsGO;
        private Rigidbody     _hipsRb;
        private BalanceController _bc;
        private CharacterState _cs;
        private PlayerMovement _pm;

        private float _savedFixedDeltaTime;
        private int   _savedSolverIterations;
        private int   _savedSolverVelocityIterations;

        // â”€â”€ Setup / Teardown â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        [SetUp]
        public void SetUp()
        {
            _savedFixedDeltaTime           = Time.fixedDeltaTime;
            _savedSolverIterations         = Physics.defaultSolverIterations;
            _savedSolverVelocityIterations = Physics.defaultSolverVelocityIterations;

            Time.fixedDeltaTime                     = 0.01f;
            Physics.defaultSolverIterations         = 12;
            Physics.defaultSolverVelocityIterations = 4;

            Physics.IgnoreLayerCollision(LayerPlayer,        LayerEnvironment, false);
            Physics.IgnoreLayerCollision(LayerLowerLegParts, LayerEnvironment, true);

            CreateFlatGround();
            CreateCharacterRig();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hipsGO   != null) Object.Destroy(_hipsGO);
            if (_groundGO != null) Object.Destroy(_groundGO);

            Time.fixedDeltaTime                     = _savedFixedDeltaTime;
            Physics.defaultSolverIterations         = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
        }

        // â”€â”€ Tests â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        /// <summary>
        /// Regression: character must clear the full hairpin sequence including the
        /// post-hairpin turn (gate 8) with no falls and no missed gates.
        ///
        /// This is the direct replacement for manual lap testing at the stuck corner.
        /// Red = stuck loop still happening. Green = corner is clean.
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Hairpin_ClearsAllGates_NoFalls()
        {
            yield return SettleCharacter();
            var result = new HairpinResult();
            yield return RunGhostDriver(result);

            string summary = BuildSummary(result);
            Debug.Log($"[HairpinCorner] Hairpin_ClearsAllGates_NoFalls: {summary}");

            Assert.AreEqual(0, result.GatesMissed,
                $"Expected 0 gates missed but got {result.GatesMissed}. {summary}");

            Assert.AreEqual(0, result.FallCount,
                $"Expected 0 falls but got {result.FallCount}. {summary}");

            Assert.That(result.FrameCount, Is.LessThanOrEqualTo(BudgetFrames),
                $"Hairpin sequence exceeded {BudgetFrames / 100f:F1}s budget. {summary}");
        }

        /// <summary>
        /// Diagnostic: always passes, logs timing + gate details. Run this when
        /// debugging â€” it tells you exactly which gate failed and at what frame.
        /// </summary>
        [UnityTest]
        [Timeout(60000)]
        public IEnumerator Hairpin_Diagnostic()
        {
            yield return SettleCharacter();
            var result = new HairpinResult();
            yield return RunGhostDriver(result);

            float secs = result.FrameCount / 100f;
            Debug.Log($"[HairpinCorner] â”€â”€ DIAGNOSTIC â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€");
            Debug.Log($"[HairpinCorner] Time:   {secs:F2}s ({result.FrameCount} frames)");
            Debug.Log($"[HairpinCorner] Falls:  {result.FallCount}");
            Debug.Log($"[HairpinCorner] Gates:  {result.GatesHit}/{HairpinWaypoints.Length} hit, {result.GatesMissed} missed");
            Debug.Log($"[HairpinCorner] Last gate reached: {result.LastGateReached}");
            Debug.Log($"[HairpinCorner] Stuck events detected: {result.StuckEventCount}");
            Debug.Log($"[HairpinCorner] â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€");

            Assert.Pass(BuildSummary(result));
        }

        // â”€â”€ Ghost driver â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private IEnumerator RunGhostDriver(HairpinResult result)
        {
            int totalGates     = HairpinWaypoints.Length;
            int currentGate    = 1;
            int frame          = 0;
            int framesSinceHit = 0;
            bool done          = false;
            bool prevFallen    = false;
            bool prevStuck     = false;

            LegAnimator legAnimator = _hipsGO.GetComponent<LegAnimator>();

            while (!done && frame < MaxFrames)
            {
                frame++;
                framesSinceHit++;

                // Fall detection (edge-triggered).
                bool fallen = (_cs != null && _cs.CurrentState == CharacterStateType.Fallen)
                           || _bc.IsFallen;
                if (fallen && !prevFallen) result.FallCount++;
                prevFallen = fallen;

                // Stuck-recovery detection (edge-triggered via public IsRecovering property).
                bool recovering = legAnimator != null && legAnimator.IsRecovering;
                if (recovering && !prevStuck) result.StuckEventCount++;
                prevStuck = recovering;

                // Ghost driver: point at current waypoint.
                Vector3 targetWorld = HairpinWaypoints[currentGate] + TestOriginOffset;
                Vector3 hipsXZ      = new Vector3(_hipsRb.position.x, 0f, _hipsRb.position.z);
                Vector3 targetXZ    = new Vector3(targetWorld.x, 0f, targetWorld.z);
                Vector3 toTarget    = targetXZ - hipsXZ;
                float   dist        = toTarget.magnitude;

                Vector2 input = dist > 0.01f
                    ? new Vector2(toTarget.x / dist, toTarget.z / dist)
                    : Vector2.zero;
                _pm.SetMoveInputForTest(input);

                // Gate hit.
                if (dist <= GetGateRadius(currentGate))
                {
                    result.GatesHit++;
                    result.LastGateReached = currentGate;
                    framesSinceHit = 0;
                    currentGate++;
                    if (currentGate >= totalGates) { done = true; break; }
                }

                // Gate timeout â†’ count as missed, advance.
                if (framesSinceHit >= GateMissedTimeout)
                {
                    Debug.Log($"[HairpinCorner] Gate {currentGate} MISSED at frame {frame} " +
                              $"(dist={dist:F2}m, falling={fallen}, recovering={recovering})");
                    result.GatesMissed++;
                    result.LastGateReached = currentGate;
                    framesSinceHit = 0;
                    currentGate++;
                    if (currentGate >= totalGates) { done = true; break; }
                }

                yield return new WaitForFixedUpdate();
            }

            if (!done)
            {
                int remaining = totalGates - currentGate;
                result.GatesMissed += Mathf.Max(0, remaining);
            }

            result.FrameCount = frame;
            _pm.SetMoveInputForTest(Vector2.zero);
        }

        // â”€â”€ World construction â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private void CreateFlatGround()
        {
            _groundGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _groundGO.name = "HairpinGround";
            _groundGO.transform.position   = new Vector3(8f, -0.5f, 8f) + TestOriginOffset;
            _groundGO.transform.localScale  = new Vector3(60f, 1f, 60f);
            _groundGO.layer = LayerEnvironment;
            Object.Destroy(_groundGO.GetComponent<Renderer>());
        }

        private void CreateCharacterRig()
        {
            Vector3 spawnPos = new Vector3(
                HairpinWaypoints[0].x,
                HipsSpawnHeight,
                HairpinWaypoints[0].z) + TestOriginOffset;

            // â”€â”€ Hips root â”€â”€
            _hipsGO = new GameObject("Hips_Hairpin");
            _hipsGO.transform.position = spawnPos;
            _hipsGO.layer = LayerPlayer;

            _hipsRb = _hipsGO.AddComponent<Rigidbody>();
            _hipsRb.mass                   = 10f;
            _hipsRb.interpolation          = RigidbodyInterpolation.Interpolate;
            _hipsRb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            _hipsGO.AddComponent<BoxCollider>().size = new Vector3(0.26f, 0.20f, 0.15f);

            // â”€â”€ Torso â”€â”€
            GameObject torsoGO = CreateBoxSegment("Torso", _hipsGO, LayerPlayer,
                new Vector3(0f, 0.32f, 0f), 12f, new Vector3(0.28f, 0.32f, 0.14f));
            ConfigureJoint(torsoGO, _hipsRb, 300f, 30f, 1000f);

            // â”€â”€ Left leg â”€â”€
            GameObject upperLegL = CreateCapsuleSegment("UpperLeg_L", _hipsGO, LayerPlayer,
                new Vector3(-0.10f, -0.22f, 0f), 4f, 0.07f, 0.36f);
            ConfigureJoint(upperLegL, _hipsRb, 1200f, 120f, 5000f);
            GameObject lowerLegL = CreateCapsuleSegment("LowerLeg_L", upperLegL, LayerPlayer,
                new Vector3(0f, -0.38f, 0f), 2.5f, 0.055f, 0.33f);
            ConfigureJoint(lowerLegL, upperLegL.GetComponent<Rigidbody>(), 1200f, 120f, 5000f);
            AddGroundSensor(CreateSensorOnlySegment("Foot_L", lowerLegL, new Vector3(0f, -0.35f, 0.07f)));

            // â”€â”€ Right leg â”€â”€
            GameObject upperLegR = CreateCapsuleSegment("UpperLeg_R", _hipsGO, LayerPlayer,
                new Vector3(0.10f, -0.22f, 0f), 4f, 0.07f, 0.36f);
            ConfigureJoint(upperLegR, _hipsRb, 1200f, 120f, 5000f);
            GameObject lowerLegR = CreateCapsuleSegment("LowerLeg_R", upperLegR, LayerPlayer,
                new Vector3(0f, -0.38f, 0f), 2.5f, 0.055f, 0.33f);
            ConfigureJoint(lowerLegR, upperLegR.GetComponent<Rigidbody>(), 1200f, 120f, 5000f);
            AddGroundSensor(CreateSensorOnlySegment("Foot_R", lowerLegR, new Vector3(0f, -0.35f, 0.07f)));

            // â”€â”€ Arms â”€â”€
            GameObject upperArmL = CreateCapsuleSegment("UpperArm_L", torsoGO, LayerPlayer,
                new Vector3(-0.20f, 0.10f, 0f), 2f, 0.055f, 0.28f);
            ConfigureJoint(upperArmL, torsoGO.GetComponent<Rigidbody>(), 800f, 80f, 3000f);
            CreateCapsuleSegment("LowerArm_L", upperArmL, LayerPlayer,
                new Vector3(0f, -0.30f, 0f), 1.5f, 0.045f, 0.25f);

            GameObject upperArmR = CreateCapsuleSegment("UpperArm_R", torsoGO, LayerPlayer,
                new Vector3(0.20f, 0.10f, 0f), 2f, 0.055f, 0.28f);
            ConfigureJoint(upperArmR, torsoGO.GetComponent<Rigidbody>(), 800f, 80f, 3000f);
            CreateCapsuleSegment("LowerArm_R", upperArmR, LayerPlayer,
                new Vector3(0f, -0.30f, 0f), 1.5f, 0.045f, 0.25f);

            // â”€â”€ Character components â€” RagdollSetup first â”€â”€
            _hipsGO.AddComponent<RagdollSetup>();
            _bc = _hipsGO.AddComponent<BalanceController>();
            _pm = _hipsGO.AddComponent<PlayerMovement>();
            _cs = _hipsGO.AddComponent<CharacterState>();
            _hipsGO.AddComponent<LegAnimator>();
            _hipsGO.AddComponent<ArmAnimator>();
        }

        // â”€â”€ Settle â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private IEnumerator SettleCharacter()
        {
            for (int i = 0; i < SettleFrames; i++)
                yield return new WaitForFixedUpdate();
        }

        // â”€â”€ Result container â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private class HairpinResult
        {
            public int GatesHit;
            public int GatesMissed;
            public int FallCount;
            public int FrameCount;
            public int LastGateReached;
            public int StuckEventCount;
        }

        private static string BuildSummary(HairpinResult r)
        {
            float secs = r.FrameCount / 100f;
            return $"Time={secs:F2}s ({r.FrameCount} frames) | Falls={r.FallCount} | " +
                   $"Gates={r.GatesHit}/{HairpinWaypoints.Length} | Missed={r.GatesMissed} | " +
                   $"LastGate={r.LastGateReached} | StuckEvents={r.StuckEventCount}";
        }

        // â”€â”€ Build helpers (identical to LapCourseTests) â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€

        private static GameObject CreateBoxSegment(string name, GameObject parent, int layer,
            Vector3 localPos, float mass, Vector3 boxSize)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.layer = layer;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation          = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            go.AddComponent<BoxCollider>().size = boxSize;
            return go;
        }

        private static GameObject CreateCapsuleSegment(string name, GameObject parent, int layer,
            Vector3 localPos, float mass, float radius, float height)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
            go.layer = layer;
            var rb = go.AddComponent<Rigidbody>();
            rb.mass = mass;
            rb.interpolation          = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            var col = go.AddComponent<CapsuleCollider>();
            col.radius    = radius;
            col.height    = height;
            col.direction = 1;
            return go;
        }

        private static GameObject CreateSensorOnlySegment(string name, GameObject parent, Vector3 localPos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            go.transform.localPosition = localPos;
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
            var sensor = footGO.AddComponent<GroundSensor>();
            var fi = typeof(GroundSensor).GetField("_groundLayers",
                BindingFlags.NonPublic | BindingFlags.Instance);
            fi?.SetValue(sensor, (LayerMask)(1 << LayerEnvironment));
        }
    }
}