using System.Collections;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Lap-course regression tests for the full character stack.
    ///
    /// A ghost driver feeds directional input via SetMoveInputForTest each physics frame,
    /// advancing through a pre-designed waypoint circuit inspired by Top Gear's test track.
    /// The course covers: straight, hairpin, chicane, slalom, S-bend, and return to start.
    ///
    /// Two tests:
    ///   1. CompleteLap_WithinTimeLimit_NoFalls â€" regression gate: must clear all gates, no falls, within budget.
    ///   2. CompleteLap_RecordsLapTime         â€" diagnostic: always passes, logs a prominent [LAP TIME] line.
    ///
    /// Rig construction notes:
    ///   - Ground box is on layer 12 (Environment). Physics.IgnoreLayerCollision(8,12,false)
    ///     ensures Hips (layer 8) collides with the ground.
    ///   - ALL child body segments are placed on layer 8 (LayerPlayer1Parts) to mirror the
    ///     production prefab hierarchy. RagdollSetup.Awake will move LowerLeg_L / LowerLeg_R
    ///     to layer 13 (LowerLegParts) and call Physics.IgnoreLayerCollision(13,12,true) so
    ///     lower legs pass through the floor â€" exactly the production setup.
    ///   - Foot child GOs carry GroundSensor only (no Collider), so the sensor casts while
    ///     the foot itself adds zero friction to the ground surface.
    ///   - RagdollSetup is added BEFORE BalanceController/CharacterState/PlayerMovement so
    ///     its Awake() configures joints and layer ignores before the other components start.
    ///
    /// Collaborators: <see cref="BalanceController"/>, <see cref="CharacterState"/>,
    /// <see cref="PlayerMovement"/>, <see cref="LegAnimator"/>, <see cref="ArmAnimator"/>,
    /// <see cref="GroundSensor"/>, <see cref="RagdollSetup"/>.
    /// </summary>
    public class LapCourseTests
    {
        // â"€â"€ Course geometry â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

        // DESIGN: The course fits within a 35Ã-35 m flat area.
        // Waypoints are visited in order; the character must pass within gateRadius of each.
        // The full lap:
        //   (1) Start at origin
        //   (2) Long straight ~15 m north
        //   (3) Tight 180Â° hairpin
        //   (4) Chicane: left-right-left, 3 gates ~4 m apart
        //   (5) Slalom: 5 alternating gates ~5 m apart
        //   (6) S-bend: two smooth arcs
        //   (7) Return to start

        /// <summary>
        /// Pre-designed lap course. Each Vector3 is an XZ waypoint (Y is ignored during
        /// distance checks â€" checked on the flat XZ plane only). Y values are 0.
        /// Course is offset by TestOriginOffset to avoid conflicts with other test objects
        /// that may occupy the world origin area.
        /// </summary>
        private static readonly Vector3 TestOriginOffset = new Vector3(0f, 0f, 3000f);

        private static readonly Vector3[] CourseWaypoints = new Vector3[]
        {
            // (1) Start
            new Vector3( 0f,  0f,  0f),

            // (2) Long straight â€" 15 m north
            new Vector3( 0f,  0f,  5f),
            new Vector3( 0f,  0f, 10f),
            new Vector3( 0f,  0f, 15f),

            // (3) Tight 180Â° hairpin â€" loop around a point at (8, 0, 15)
            new Vector3( 4f,  0f, 15f),
            new Vector3( 8f,  0f, 15f),   // hairpin apex
            new Vector3(12f,  0f, 15f),
            new Vector3(16f,  0f, 15f),
            new Vector3(16f,  0f, 10f),   // post-hairpin straighten

            // (4) Chicane â€" left-right-left, gates ~4 m apart
            new Vector3(12f,  0f,  8f),   // chicane gate 1 (left)
            new Vector3(16f,  0f,  4f),   // chicane gate 2 (right)
            new Vector3(12f,  0f,  0f),   // chicane gate 3 (left)

            // (5) Slalom â€" 5 alternating gates ~5 m apart
            new Vector3( 8f,  0f, -4f),   // slalom gate 1 (right)
            new Vector3( 4f,  0f, -8f),   // slalom gate 2 (left)
            new Vector3( 8f,  0f,-12f),   // slalom gate 3 (right)
            new Vector3( 4f,  0f,-16f),   // slalom gate 4 (left)
            new Vector3( 8f,  0f,-20f),   // slalom gate 5 (right)

            // (6) S-bend â€" two arcs
            new Vector3( 4f,  0f,-24f),   // arc 1 apex
            new Vector3( 0f,  0f,-20f),   // inflection
            new Vector3(-4f,  0f,-16f),   // arc 2 apex
            new Vector3( 0f,  0f,-12f),   // back to centre

            // (7) Return to start
            new Vector3( 0f,  0f, -8f),
            new Vector3( 0f,  0f, -4f),
            new Vector3( 0f,  0f,  0f),   // finish line
        };

        // â"€â"€ Gate radii â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

        /// <summary>Radius for tight corners (hairpin, chicane, slalom, S-bend).</summary>
        private const float TightGateRadius = 2.5f;

        /// <summary>Radius for wide-open waypoints (straights, return leg).</summary>
        private const float OpenGateRadius = 3.5f;

        // DESIGN: tight gates apply to corners; open gates apply to straights and return.
        // Index thresholds: waypoints 0-3 are straight (open), 4-20 are corners (tight),
        // 21-23 are return (open).
        private static float GetGateRadius(int waypointIndex)
        {
            if (waypointIndex <= 3)  { return OpenGateRadius; }   // start + straight
            if (waypointIndex <= 20) { return TightGateRadius; }  // corners + slalom + S-bend
            return OpenGateRadius;                                  // return leg
        }

        // â"€â"€ Timing â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

        /// <summary>Frames to let the character settle before driving (2 s @ 100 Hz).</summary>
        private const int SettleFrames = 200;

        /// <summary>Maximum frames for a complete lap (60 s @ 100 Hz).</summary>
        private const int MaxLapFrames = 6000;

        /// <summary>Frames without a gate hit before the current gate is marked missed.</summary>
        private const int GateMissedTimeoutFrames = 600;

        /// <summary>Frame budget for the pass/fail assertion (40 s @ 100 Hz).</summary>
        private const int LapBudgetFrames = 4000;

        // â"€â"€ Spawn constants â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

        private const float HipsSpawnHeight = 1.0f;
        private const int   LayerEnvironment   = GameSettings.LayerEnvironment;     // 12
        private const int   LayerPlayer        = GameSettings.LayerPlayer1Parts;    // 8
        private const int   LayerLowerLegParts = GameSettings.LayerLowerLegParts;  // 13

        // â"€â"€ Shared state â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

        private GameObject     _groundGO;
        private GameObject     _hipsGO;
        private Rigidbody      _hipsRb;
        private BalanceController _bc;
        private CharacterState _cs;
        private PlayerMovement _pm;

        private float _savedFixedDeltaTime;
        private int   _savedSolverIterations;
        private int   _savedSolverVelocityIterations;

        // â"€â"€ Setup / Teardown â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

        [SetUp]
        public void SetUp()
        {
            _savedFixedDeltaTime              = Time.fixedDeltaTime;
            _savedSolverIterations            = Physics.defaultSolverIterations;
            _savedSolverVelocityIterations    = Physics.defaultSolverVelocityIterations;

            Time.fixedDeltaTime               = 0.01f;    // 100 Hz
            Physics.defaultSolverIterations   = 12;
            Physics.defaultSolverVelocityIterations = 4;

            // Enable playerâ†'environment collision (layer 8 â†" layer 12).
            // Also ensure LowerLegParts (13) does NOT collide with environment â€" will be
            // called by RagdollSetup.Awake too, but setting here as a safety net.
            Physics.IgnoreLayerCollision(LayerPlayer,        LayerEnvironment, false);
            Physics.IgnoreLayerCollision(LayerLowerLegParts, LayerEnvironment, true);

            CreateFlatGround();
            CreateCharacterRig();
        }

        [TearDown]
        public void TearDown()
        {
            if (_hipsGO   != null) { Object.Destroy(_hipsGO); }
            if (_groundGO != null) { Object.Destroy(_groundGO); }

            Time.fixedDeltaTime                     = _savedFixedDeltaTime;
            Physics.defaultSolverIterations         = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
        }

        // â"€â"€ Tests â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

        /// <summary>
        /// Regression gate: the character must complete the lap circuit with no falls
        /// and no missed gates within 40 seconds (4000 frames @ 100 Hz).
        ///
        /// Failure message includes lap time, fall count, and gate hit/miss counts.
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator CompleteLap_WithinTimeLimit_NoFalls()
        {
            yield return SettleCharacter();
            var result = new LapResult();
            yield return RunGhostDriver(result);

            string summary = BuildSummary(result);
            Debug.Log($"[LapCourse] CompleteLap_WithinTimeLimit_NoFalls: {summary}");

            Assert.AreEqual(0, result.GatesMissed,
                $"Expected 0 gates missed but got {result.GatesMissed}. {summary}");

            Assert.AreEqual(0, result.FallCount,
                $"Expected 0 falls but got {result.FallCount}. {summary}");

            Assert.That(result.LapFrames, Is.LessThanOrEqualTo(LapBudgetFrames),
                $"Lap time exceeded {LapBudgetFrames} frames ({LapBudgetFrames / 100f:F1}s). {summary}");
        }

        /// <summary>
        /// Diagnostic test: always passes. Runs a full lap and logs a prominent
        /// [LAP TIME] line for baseline tracking.
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator CompleteLap_RecordsLapTime()
        {
            yield return SettleCharacter();
            var result = new LapResult();
            yield return RunGhostDriver(result);

            float  lapSeconds = result.LapFrames / 100f;
            string headline   = $"[LAP TIME] {lapSeconds:F2}s | Falls: {result.FallCount} | " +
                                $"Gates: {result.GatesHit}/{result.TotalGates}";
            Debug.Log(headline);
            Debug.Log($"[LapCourse] Full summary: {BuildSummary(result)}");

            // â"€â"€ Personal best tracking â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€
            SavePBIfFaster(lapSeconds, result.FallCount, result.GatesHit, result.TotalGates);

            Assert.Pass(headline);
        }

        // â"€â"€ PB persistence â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

        private static string PBFilePath =>
            Path.Combine(Application.dataPath, "..", "Logs", "lap-pb.txt");

        private static void SavePBIfFaster(float lapTime, int falls, int gates, int total)
        {
            // Only record a PB on a clean lap (no falls, all gates hit).
            if (falls > 0 || gates < total) return;

            string pbPath = PBFilePath;
            float  existingPB = float.MaxValue;

            if (File.Exists(pbPath))
            {
                string[] lines = File.ReadAllLines(pbPath);
                if (lines.Length > 0)
                {
                    // Format: "PB: 23.65s | ..."
                    string[] parts = lines[0].Split('|');
                    if (parts.Length > 0)
                    {
                        string t = parts[0].Replace("PB:", "").Replace("s", "").Trim();
                        float.TryParse(t, System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out existingPB);
                    }
                }
            }

            if (lapTime >= existingPB) return;  // not faster

            string date    = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm");
            string newPB   = $"PB: {lapTime:F2}s | Falls: {falls} | Gates: {gates}/{total} | Date: {date}";
            string prevLine = File.Exists(pbPath)
                ? File.ReadAllLines(pbPath)[0]
                : "Previous: --";

            Directory.CreateDirectory(Path.GetDirectoryName(pbPath)!);
            File.WriteAllText(pbPath, $"{newPB}\n{prevLine}\n");

            Debug.Log($"[LapCourse] ðŸ† NEW PB! {newPB}");
        }

        // â"€â"€ Ghost driver coroutine â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

        /// <summary>
        /// Drives the character through the waypoint course. Each FixedUpdate frame,
        /// the ghost computes the XZ direction to the current target waypoint and injects
        /// it via SetMoveInputForTest. Advances to the next waypoint when within gateRadius.
        /// </summary>
        private IEnumerator RunGhostDriver(LapResult result)
        {
            const int   StableFramesRequired = 10;
            const float StableTiltDeg        = 15f;

            int totalGates      = CourseWaypoints.Length;
            result.TotalGates   = totalGates;
            int currentGate     = 1;   // gate 0 = spawn — already there
            int lapFrame        = 0;
            int framesSinceHit  = 0;
            int stableFrames    = 0;
            bool navSuspended   = false;
            bool lapComplete    = false;
            bool prevFallen     = false;

            while (!lapComplete && lapFrame < MaxLapFrames)
            {
                lapFrame++;

                // Fallen edge detection (counts transitions, not sustained states).
                bool fallen    = (_cs != null && _cs.CurrentState == CharacterStateType.Fallen)
                               || _bc.IsFallen;
                bool gettingUp = _cs != null && _cs.CurrentState == CharacterStateType.GettingUp;
                if (fallen && !prevFallen) result.FallCount++;
                prevFallen = fallen;

                // Navigation suspension: enter on Fallen/GettingUp, exit on confirmed stable state.
                if (fallen || gettingUp)
                {
                    navSuspended = true;
                    stableFrames = 0;
                }

                if (navSuspended)
                {
                    bool standing = _cs != null && _cs.CurrentState == CharacterStateType.Standing;
                    float tilt    = _bc != null ? _bc.TiltAngleDeg : 90f;
                    bool stable   = standing && tilt < StableTiltDeg;

                    if (stable) stableFrames++;
                    else        stableFrames = 0;

                    if (stableFrames >= StableFramesRequired)
                    {
                        navSuspended = false;
                        stableFrames = 0;
                        framesSinceHit = 0; // fresh gate window after recovery
                    }
                    else
                    {
                        _pm.SetMoveInputForTest(Vector2.zero);
                        yield return new WaitForFixedUpdate();
                        continue;
                    }
                }

                framesSinceHit++;

                // Compute XZ direction to current target waypoint.
                Vector3 targetWorld = CourseWaypoints[currentGate] + TestOriginOffset;
                Vector3 hipsXZ      = new Vector3(_hipsRb.position.x, 0f, _hipsRb.position.z);
                Vector3 targetXZ    = new Vector3(targetWorld.x,      0f, targetWorld.z);
                Vector3 toTarget    = targetXZ - hipsXZ;
                float   dist        = toTarget.magnitude;

                Vector2 moveInput = (dist > 0.01f)
                    ? new Vector2(toTarget.x / dist, toTarget.z / dist)
                    : Vector2.zero;

                _pm.SetMoveInputForTest(moveInput);

                // Gate hit check.
                float gateRadius = GetGateRadius(currentGate);
                if (dist <= gateRadius)
                {
                    result.GatesHit++;
                    framesSinceHit = 0;
                    currentGate++;

                    if (currentGate >= totalGates)
                    {
                        lapComplete = true;
                        break;
                    }
                }

                // Gate missed timeout.
                if (framesSinceHit >= GateMissedTimeoutFrames)
                {
                    result.GatesMissed++;
                    framesSinceHit = 0;
                    currentGate++;

                    if (currentGate >= totalGates)
                    {
                        lapComplete = true;
                        break;
                    }
                }

                yield return new WaitForFixedUpdate();
            }

            if (!lapComplete)
            {
                int remaining = totalGates - currentGate;
                result.GatesMissed += Mathf.Max(0, remaining);
            }

            result.LapFrames  = lapFrame;
            result.LapComplete = lapComplete;

            _pm.SetMoveInputForTest(Vector2.zero);
        }

        // â"€â"€ World construction â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

        /// <summary>
        /// Creates a 40Ã-40 m flat ground box on Layer 12 (Environment) at the test area.
        /// </summary>
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

        /// <summary>
        /// Builds a minimal but production-faithful character rig:
        ///
        /// All body segment GOs are on layer 8 (LayerPlayer1Parts), mirroring the real prefab.
        /// RagdollSetup.Awake will move LowerLeg_L and LowerLeg_R to layer 13 (LowerLegParts)
        /// and disable their ground collision, preventing the lower legs from catching on the
        /// floor and creating friction that blocks forward movement.
        ///
        /// Foot GOs carry only a GroundSensor (NO Collider). This is intentional: the feet are
        /// purely sensor attachment points, and having no collider means they add zero friction
        /// at the ground surface. The GroundSensor uses transform.position as cast origin when
        /// no Collider is present (safe fallback documented in GroundSensor.GetCastOrigin).
        ///
        /// Component add order: RagdollSetup â†' BalanceController â†' CharacterState â†'
        ///   PlayerMovement â†' LegAnimator â†' ArmAnimator.
        /// RagdollSetup must run before BC so joints and layer ignores are configured when BC
        /// calls GetComponentsInChildren to locate GroundSensors.
        /// </summary>
        private void CreateCharacterRig()
        {
            Vector3 spawnPos = new Vector3(
                CourseWaypoints[0].x,
                HipsSpawnHeight,
                CourseWaypoints[0].z) + TestOriginOffset;

            // â"€â"€ Hips root â"€â"€
            _hipsGO = new GameObject("Hips_Lap");
            _hipsGO.transform.position = spawnPos;
            _hipsGO.layer = LayerPlayer;

            _hipsRb = _hipsGO.AddComponent<Rigidbody>();
            _hipsRb.mass                    = 10f;
            _hipsRb.interpolation           = RigidbodyInterpolation.Interpolate;
            _hipsRb.collisionDetectionMode  = CollisionDetectionMode.Continuous;

            BoxCollider hipsCol = _hipsGO.AddComponent<BoxCollider>();
            hipsCol.size = new Vector3(0.26f, 0.20f, 0.15f);

            // â"€â"€ Torso â"€â"€
            GameObject torsoGO = CreateBoxSegment("Torso", _hipsGO, LayerPlayer,
                new Vector3(0f, 0.32f, 0f), 12f, new Vector3(0.28f, 0.32f, 0.14f));
            ConfigureJoint(torsoGO, _hipsRb, 300f, 30f, 1000f);

            // â"€â"€ Left leg â"€â"€
            GameObject upperLegL = CreateCapsuleSegment("UpperLeg_L", _hipsGO, LayerPlayer,
                new Vector3(-0.10f, -0.22f, 0f), 4f, 0.07f, 0.36f);
            ConfigureJoint(upperLegL, _hipsRb, 1200f, 120f, 5000f);

            GameObject lowerLegL = CreateCapsuleSegment("LowerLeg_L", upperLegL, LayerPlayer,
                new Vector3(0f, -0.38f, 0f), 2.5f, 0.055f, 0.33f);
            ConfigureJoint(lowerLegL, upperLegL.GetComponent<Rigidbody>(), 1200f, 120f, 5000f);

            // Foot: GroundSensor attachment only â€" NO Collider so feet add zero ground friction.
            GameObject footL = CreateSensorOnlySegment("Foot_L", lowerLegL,
                new Vector3(0f, -0.35f, 0.07f));
            AddGroundSensor(footL);

            // â"€â"€ Right leg â"€â"€
            GameObject upperLegR = CreateCapsuleSegment("UpperLeg_R", _hipsGO, LayerPlayer,
                new Vector3(0.10f, -0.22f, 0f), 4f, 0.07f, 0.36f);
            ConfigureJoint(upperLegR, _hipsRb, 1200f, 120f, 5000f);

            GameObject lowerLegR = CreateCapsuleSegment("LowerLeg_R", upperLegR, LayerPlayer,
                new Vector3(0f, -0.38f, 0f), 2.5f, 0.055f, 0.33f);
            ConfigureJoint(lowerLegR, upperLegR.GetComponent<Rigidbody>(), 1200f, 120f, 5000f);

            GameObject footR = CreateSensorOnlySegment("Foot_R", lowerLegR,
                new Vector3(0f, -0.35f, 0.07f));
            AddGroundSensor(footR);

            // â"€â"€ Arms â"€â"€
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

            // â"€â"€ Components â€" RagdollSetup FIRST so joints/layers are configured before BC â"€â"€
            _hipsGO.AddComponent<RagdollSetup>();
            _bc = _hipsGO.AddComponent<BalanceController>();
            _pm = _hipsGO.AddComponent<PlayerMovement>();
            _cs = _hipsGO.AddComponent<CharacterState>();
            _hipsGO.AddComponent<LegAnimator>();
            _hipsGO.AddComponent<ArmAnimator>();
        }

        // â"€â"€ Settle helper â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

        private IEnumerator SettleCharacter()
        {
            // Wait one frame for Start() to run (PlayerMovement.Start caches Camera.main).
            yield return new WaitForFixedUpdate();

            // Null the camera so ghost driver input is treated as raw world-space XZ.
            // A stale Camera.main from CameraFollowTests would rotate input by a random yaw.
            _pm?.SetCameraForTest(null);

            for (int i = 1; i < SettleFrames; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        // â"€â"€ Result container â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

        private class LapResult
        {
            public int  TotalGates;
            public int  GatesHit;
            public int  GatesMissed;
            public int  FallCount;
            public int  LapFrames;
            public bool LapComplete;
        }

        private static string BuildSummary(LapResult r)
        {
            float secs = r.LapFrames / 100f;
            return $"LapTime={secs:F2}s ({r.LapFrames} frames) | " +
                   $"Falls={r.FallCount} | " +
                   $"Gates={r.GatesHit}/{r.TotalGates} | " +
                   $"Missed={r.GatesMissed} | " +
                   $"Complete={r.LapComplete}";
        }

        // â"€â"€ Build helpers â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€â"€

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
            col.direction = 1; // Y axis
            return go;
        }

        /// <summary>
        /// Creates a collider-free child GO used only as a sensor attachment point.
        /// No Rigidbody, no Collider â€" purely a transform anchor.
        /// </summary>
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

            // Linear motions: Locked â€" child position stays relative to parent.
            // This is correct: RagdollSetup.DisableNeighboringCollisions uses joint.connectedBody
            // to find pairs; and Locked keeps the limb attached at the anchor.
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

        /// <summary>
        /// Adds a GroundSensor to the given sensor-only GO and sets its _groundLayers mask
        /// to LayerEnvironment (12) via reflection (avoids UnityEditor dependency).
        /// </summary>
        private static void AddGroundSensor(GameObject footGO)
        {
            GroundSensor sensor = footGO.AddComponent<GroundSensor>();
            FieldInfo fi = typeof(GroundSensor).GetField(
                "_groundLayers",
                BindingFlags.NonPublic | BindingFlags.Instance);
            fi?.SetValue(sensor, (LayerMask)(1 << LayerEnvironment));
        }
    }
}