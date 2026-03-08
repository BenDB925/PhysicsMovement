using System.Collections;
using System.IO;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
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
    ///   1. CompleteLap_WithinTimeLimit_NoFalls — regression gate: must clear all gates, no falls, within budget.
    ///   2. CompleteLap_RecordsLapTime         — diagnostic: always passes, logs a prominent [LAP TIME] line.
    ///
    /// Rig construction notes:
    ///   - Ground box is on layer 12 (Environment). Physics.IgnoreLayerCollision(8,12,false)
    ///     ensures Hips (layer 8) collides with the ground.
    ///   - ALL child body segments are placed on layer 8 (LayerPlayer1Parts) to mirror the
    ///     production prefab hierarchy. RagdollSetup.Awake will move LowerLeg_L / LowerLeg_R
    ///     to layer 13 (LowerLegParts) and call Physics.IgnoreLayerCollision(13,12,true) so
    ///     lower legs pass through the floor — exactly the production setup.
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
        // ── Course geometry ────────────────────────────────────────────────────────

        // DESIGN: The course fits within a 35×35 m flat area.
        // Waypoints are visited in order; the character must pass within gateRadius of each.
        // The full lap:
        //   (1) Start at origin
        //   (2) Long straight ~15 m north
        //   (3) Tight 180° hairpin
        //   (4) Chicane: left-right-left, 3 gates ~4 m apart
        //   (5) Slalom: 5 alternating gates ~5 m apart
        //   (6) S-bend: two smooth arcs
        //   (7) Return to start

        /// <summary>
        /// Pre-designed lap course. Each Vector3 is an XZ waypoint (Y is ignored during
        /// distance checks — checked on the flat XZ plane only). Y values are 0.
        /// Course is offset by TestOriginOffset to avoid conflicts with other test objects
        /// that may occupy the world origin area.
        /// </summary>
        private static readonly Vector3 TestOriginOffset = new Vector3(0f, 0f, 3000f);

        private static readonly Vector3[] CourseWaypoints = new Vector3[]
        {
            // (1) Start
            new Vector3( 0f,  0f,  0f),

            // (2) Long straight — 15 m north
            new Vector3( 0f,  0f,  5f),
            new Vector3( 0f,  0f, 10f),
            new Vector3( 0f,  0f, 15f),

            // (3) Tight 180° hairpin — loop around a point at (8, 0, 15)
            new Vector3( 4f,  0f, 15f),
            new Vector3( 8f,  0f, 15f),   // hairpin apex
            new Vector3(12f,  0f, 15f),
            new Vector3(16f,  0f, 15f),
            new Vector3(16f,  0f, 10f),   // post-hairpin straighten

            // (4) Chicane — left-right-left, gates ~4 m apart
            new Vector3(12f,  0f,  8f),   // chicane gate 1 (left)
            new Vector3(16f,  0f,  4f),   // chicane gate 2 (right)
            new Vector3(12f,  0f,  0f),   // chicane gate 3 (left)

            // (5) Slalom — 5 alternating gates ~5 m apart
            new Vector3( 8f,  0f, -4f),   // slalom gate 1 (right)
            new Vector3( 4f,  0f, -8f),   // slalom gate 2 (left)
            new Vector3( 8f,  0f,-12f),   // slalom gate 3 (right)
            new Vector3( 4f,  0f,-16f),   // slalom gate 4 (left)
            new Vector3( 8f,  0f,-20f),   // slalom gate 5 (right)

            // (6) S-bend — two arcs
            new Vector3( 4f,  0f,-24f),   // arc 1 apex
            new Vector3( 0f,  0f,-20f),   // inflection
            new Vector3(-4f,  0f,-16f),   // arc 2 apex
            new Vector3( 0f,  0f,-12f),   // back to centre

            // (7) Return to start
            new Vector3( 0f,  0f, -8f),
            new Vector3( 0f,  0f, -4f),
            new Vector3( 0f,  0f,  0f),   // finish line
        };

        // ── Gate radii ────────────────────────────────────────────────────────────

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

        // ── Timing ────────────────────────────────────────────────────────────────

        /// <summary>Frames to let the character settle before driving (2 s @ 100 Hz).</summary>
        private const int SettleFrames = 200;

        /// <summary>Maximum frames for a complete lap (60 s @ 100 Hz).</summary>
        private const int MaxLapFrames = 6000;

        /// <summary>Frames without a gate hit before the current gate is marked missed.</summary>
        private const int GateMissedTimeoutFrames = 600;

        /// <summary>Frame budget for the pass/fail assertion (40 s @ 100 Hz).</summary>
        private const int LapBudgetFrames = 4000;

        // ── Spawn constants ────────────────────────────────────────────────────────

        private const float HipsSpawnHeight = 1.0f;

        // ── Shared state ──────────────────────────────────────────────────────────

        private PlayerPrefabTestRig _rig;
        private Rigidbody      _hipsRb;
        private BalanceController _bc;
        private CharacterState _cs;
        private PlayerMovement _pm;

        // ── Setup / Teardown ──────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOriginOffset,
                SpawnOffset = new Vector3(CourseWaypoints[0].x, HipsSpawnHeight, CourseWaypoints[0].z),
                GroundName = "LapGround",
                GroundScale = new Vector3(60f, 1f, 60f),
            });

            _hipsRb = _rig.HipsBody;
            _bc = _rig.BalanceController;
            _cs = _rig.CharacterState;
            _pm = _rig.PlayerMovement;
        }

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
        }

        // ── Tests ─────────────────────────────────────────────────────────────────

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

            // ── Personal best tracking ────────────────────────────────────────────
            SavePBIfFaster(lapSeconds, result.FallCount, result.GatesHit, result.TotalGates);

            Assert.Pass(headline);
        }

        // ── PB persistence ────────────────────────────────────────────────────────

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

            Debug.Log($"[LapCourse] 🏆 NEW PB! {newPB}");
        }

        // ── Ghost driver coroutine ────────────────────────────────────────────────

        /// <summary>
        /// Drives the character through the waypoint course. Each FixedUpdate frame,
        /// the ghost computes the XZ direction to the current target waypoint and injects
        /// it via SetMoveInputForTest. Advances to the next waypoint when within gateRadius.
        /// </summary>
        private IEnumerator RunGhostDriver(LapResult result)
        {
            int totalGates      = CourseWaypoints.Length;
            result.TotalGates   = totalGates;
            int currentGate     = 1;   // gate 0 = spawn — already there
            int lapFrame        = 0;
            int framesSinceHit  = 0;
            bool lapComplete    = false;
            bool prevFallen     = false;

            while (!lapComplete && lapFrame < MaxLapFrames)
            {
                lapFrame++;
                framesSinceHit++;

                // Fallen edge detection (counts transitions, not sustained states).
                bool fallen = (_cs != null && _cs.CurrentState == CharacterStateType.Fallen)
                           || _bc.IsFallen;
                if (fallen && !prevFallen)
                {
                    result.FallCount++;
                }
                prevFallen = fallen;

                // Compute XZ direction to current target waypoint.
                // Use world position offset by TestOriginOffset.
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

        // ── Settle helper ─────────────────────────────────────────────────────────

        private IEnumerator SettleCharacter()
        {
            yield return _rig.WarmUp(SettleFrames);
        }

        // ── Result container ──────────────────────────────────────────────────────

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

    }
}
