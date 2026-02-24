using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Corner recovery parameter optimizer. Sweeps combinations of recovery/turn
    /// parameters and scores each on a hard-snap slalom course matching Arena_01.
    ///
    /// ALL tests are [Ignore] — excluded from CI. Run on-demand via Unity Test Runner.
    ///
    /// Uses the real PlayerRagdoll prefab (not the synthetic rig) with 1.0m gate radii
    /// matching LapDemoRunner. Results are therefore valid for Arena_01.
    ///
    /// Parameters swept:
    ///   recoveryFrames         : how long legs hold identity after stuck detection
    ///   directionChangeGrace   : grace period after input snap before stuck re-enables
    ///   kPYaw                  : BC yaw proportional gain (how fast body turns)
    ///   stepAngle              : leg swing amplitude
    ///
    /// Scoring: (gatesHit × 1000) - (lapFrames / 10) - (stuckEvents × 200)
    /// Higher = better. Gates are the primary signal.
    /// </summary>
    public class CornerOptimizerTests
    {
        private const string PrefabPath    = "Assets/Prefabs/PlayerRagdoll.prefab";
        private const string ReportPath    = "Logs/corner-optimizer-results.txt";

        private static readonly Vector3 TestOriginOffset = new Vector3(0f, 0f, 6000f);

        // ── Gate radii matching LapDemoRunner / Arena_01 ──────────────────────────
        private const float TightGateRadius = 1.0f;
        private const float OpenGateRadius  = 1.5f;

        private static float GetGateRadius(int i)
        {
            if (i <= 3)  return OpenGateRadius;
            if (i <= 20) return TightGateRadius;
            return OpenGateRadius;
        }

        // ── Course (identical to LapCourseTests / LapDemoRunner) ─────────────────
        private static readonly Vector3[] CourseWaypoints = new Vector3[]
        {
            new Vector3( 0f,  0f,  0f),
            new Vector3( 0f,  0f,  5f),
            new Vector3( 0f,  0f, 10f),
            new Vector3( 0f,  0f, 15f),
            new Vector3( 4f,  0f, 15f),
            new Vector3( 8f,  0f, 15f),
            new Vector3(12f,  0f, 15f),
            new Vector3(16f,  0f, 15f),
            new Vector3(16f,  0f, 10f),
            new Vector3(12f,  0f,  8f),
            new Vector3(16f,  0f,  4f),
            new Vector3(12f,  0f,  0f),
            new Vector3( 8f,  0f, -4f),
            new Vector3( 4f,  0f, -8f),
            new Vector3( 8f,  0f,-12f),
            new Vector3( 4f,  0f,-16f),
            new Vector3( 8f,  0f,-20f),
            new Vector3( 4f,  0f,-24f),
            new Vector3( 0f,  0f,-20f),
            new Vector3(-4f,  0f,-16f),
            new Vector3( 0f,  0f,-12f),
            new Vector3( 0f,  0f, -8f),
            new Vector3( 0f,  0f, -4f),
            new Vector3( 0f,  0f,  0f),
        };

        private const int SettleFrames          = 200;
        private const int GateMissedTimeoutFrames = 400;
        private const int MaxLapFrames           = 6000;
        private const float LookaheadRadius      = 6f;

        // ── Parameter grids ───────────────────────────────────────────────────────
        // Keep grids small — each trial is ~60s at 100Hz. 3×3×3 = 27 trials = ~30min.
        private static readonly int[]   RecoveryFramesSweep      = {  20,  35,  50 };
        private static readonly int[]   GraceFramesSweep         = {  40,  80, 120 };
        private static readonly float[] KPYawSweep               = { 120f, 200f, 280f };

        // ── Shared state ──────────────────────────────────────────────────────────
        private GameObject        _instance;
        private GameObject        _ground;
        private Rigidbody         _hipsRb;
        private PlayerMovement    _pm;
        private BalanceController _bc;
        private LegAnimator       _la;
        private CharacterState    _cs;

        [SetUp]
        public void SetUp()
        {
            Time.fixedDeltaTime                     = 0.01f;
            Physics.defaultSolverIterations         = 12;
            Physics.defaultSolverVelocityIterations = 4;
            Physics.IgnoreLayerCollision(GameSettings.LayerPlayer1Parts, GameSettings.LayerEnvironment, false);
            Physics.IgnoreLayerCollision(GameSettings.LayerLowerLegParts, GameSettings.LayerEnvironment, true);
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance != null) { Object.Destroy(_instance); _instance = null; }
            if (_ground   != null) { Object.Destroy(_ground);   _ground   = null; }
        }

        // ── Main sweep ────────────────────────────────────────────────────────────

        [UnityTest]
        [Timeout(7200000)] // 2h ceiling
        [Ignore("On-demand corner optimizer — excluded from CI. " +
                "Remove [Ignore] to run. Sweeps recovery/grace/kPYaw vs real prefab + 1.0m gates.")]
        public IEnumerator CornerOptimizer_SweepRecoveryParams_WritesRankedReport()
        {
            var results = new List<TrialResult>();
            int total   = RecoveryFramesSweep.Length * GraceFramesSweep.Length * KPYawSweep.Length;
            int current = 0;

            foreach (int recoveryFrames in RecoveryFramesSweep)
            foreach (int graceFrames    in GraceFramesSweep)
            foreach (float kPYaw        in KPYawSweep)
            {
                current++;
                Debug.Log($"[CornerOptimizer] Trial {current}/{total}: " +
                          $"recovery={recoveryFrames}  grace={graceFrames}  kPYaw={kPYaw}");

                SpawnCharacter();
                yield return Settle();

                // Apply parameters via reflection.
                SetIntField  (_la, "_recoveryFrames",              recoveryFrames);
                SetIntField  (_la, "_stuckFrameThreshold",         12);
                SetIntField  (_la, "_directionChangeGraceFrames",  graceFrames);
                SetFloatField(_bc, "_kPYaw",                       kPYaw);

                var trial = new TrialResult
                {
                    RecoveryFrames = recoveryFrames,
                    GraceFrames    = graceFrames,
                    KPYaw          = kPYaw,
                    TotalGates     = CourseWaypoints.Length,
                };

                yield return RunGhostDriver(trial);
                results.Add(trial);

                Object.Destroy(_instance); _instance = null;
                Object.Destroy(_ground);   _ground   = null;
            }

            results.Sort((a, b) => b.Score.CompareTo(a.Score));
            WriteReport(results);

            Debug.Log($"[CornerOptimizer] Done. Best: {results[0].ToReportLine(1)}");
            Assert.Pass($"Optimizer complete. Best score: {results[0].Score}. See {ReportPath}");
        }

        // ── Ghost driver ──────────────────────────────────────────────────────────

        private IEnumerator RunGhostDriver(TrialResult trial)
        {
            int  totalGates     = CourseWaypoints.Length;
            int  currentGate    = 1;
            int  lapFrame       = 0;
            int  framesSinceHit = 0;
            int  stableFrames   = 0;
            bool suspended      = false;

            while (lapFrame < MaxLapFrames)
            {
                lapFrame++;
                framesSinceHit++;

                // Nav suspension — mirror LapDemoRunner behaviour.
                CharacterStateType state = _cs != null ? _cs.CurrentState : CharacterStateType.Standing;
                float tilt = _bc != null ? _bc.TiltAngleDeg : 0f;
                bool fallen = state == CharacterStateType.Fallen || state == CharacterStateType.GettingUp;

                if (fallen)
                {
                    trial.FallCount++;
                    suspended     = true;
                    stableFrames  = 0;
                }
                if (suspended)
                {
                    bool standing = state == CharacterStateType.Standing || state == CharacterStateType.Moving;
                    if (standing && tilt < 15f) stableFrames++;
                    else                         stableFrames = 0;
                    if (stableFrames >= 10) suspended = false;
                }

                if (suspended)
                {
                    _pm.SetMoveInputForTest(Vector2.zero);
                    yield return new WaitForFixedUpdate();
                    continue;
                }

                Vector3 targetWorld = CourseWaypoints[currentGate] + TestOriginOffset;
                Vector3 hipsXZ      = new Vector3(_hipsRb.position.x, 0f, _hipsRb.position.z);
                Vector3 targetXZ    = new Vector3(targetWorld.x,      0f, targetWorld.z);
                Vector3 toTarget    = targetXZ - hipsXZ;
                float   dist        = toTarget.magnitude;

                Vector3 dir = dist > 0.01f ? toTarget / dist : Vector3.forward;

                // Lookahead blend.
                int nextIdx = currentGate + 1;
                if (nextIdx < totalGates && dist < LookaheadRadius)
                {
                    Vector3 nextWorld = CourseWaypoints[nextIdx] + TestOriginOffset;
                    Vector3 toNext    = new Vector3(nextWorld.x - hipsXZ.x, 0f, nextWorld.z - hipsXZ.z);
                    float   blend     = 1f - (dist / LookaheadRadius);
                    dir = Vector3.Slerp(dir, toNext.normalized, blend * 0.6f).normalized;
                }

                _pm.SetMoveInputForTest(new Vector2(dir.x, dir.z));

                if (_la != null && _la.IsRecovering) trial.StuckEvents++;

                if (dist <= GetGateRadius(currentGate))
                {
                    trial.GatesHit++;
                    framesSinceHit = 0;
                    if (++currentGate >= totalGates) break;
                }
                else if (framesSinceHit >= GateMissedTimeoutFrames)
                {
                    trial.GatesMissed++;
                    framesSinceHit = 0;
                    if (++currentGate >= totalGates) break;
                }

                yield return new WaitForFixedUpdate();
            }

            trial.LapFrames = lapFrame;
            _pm.SetMoveInputForTest(Vector2.zero);
        }

        // ── World construction ────────────────────────────────────────────────────

        private void SpawnCharacter()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "CornerOptimizerGround";
            _ground.transform.position   = TestOriginOffset;
            _ground.transform.localScale = new Vector3(80f, 1f, 80f);
            _ground.layer = GameSettings.LayerEnvironment;
            var rb = _ground.GetComponent<Rigidbody>();
            if (rb != null) Object.Destroy(rb);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"Prefab not found at {PrefabPath}");
            Vector3 spawnPos = CourseWaypoints[0] + TestOriginOffset + new Vector3(0f, 1.1f, 0f);
            _instance = Object.Instantiate(prefab, spawnPos, Quaternion.identity);

            _hipsRb = _instance.GetComponentInChildren<Rigidbody>();
            _pm     = _instance.GetComponentInChildren<PlayerMovement>();
            _bc     = _instance.GetComponentInChildren<BalanceController>();
            _la     = _instance.GetComponentInChildren<LegAnimator>();
            _cs     = _instance.GetComponentInChildren<CharacterState>();
        }

        private IEnumerator Settle()
        {
            yield return new WaitForFixedUpdate();
            _pm?.SetCameraForTest(null);
            for (int i = 1; i < SettleFrames; i++)
                yield return new WaitForFixedUpdate();
        }

        // ── Reflection helpers ────────────────────────────────────────────────────

        private static void SetFloatField(object target, string fieldName, float value)
        {
            var f = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) f.SetValue(target, value);
            else Debug.LogWarning($"[CornerOptimizer] Field not found: {fieldName}");
        }

        private static void SetIntField(object target, string fieldName, int value)
        {
            var f = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null) f.SetValue(target, value);
            else Debug.LogWarning($"[CornerOptimizer] Field not found: {fieldName}");
        }

        // ── Report ────────────────────────────────────────────────────────────────

        private static void WriteReport(List<TrialResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== CORNER OPTIMIZER RESULTS ===");
            sb.AppendLine($"Generated : {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine($"Gate radii: tight={TightGateRadius}m  open={OpenGateRadius}m");
            sb.AppendLine($"Prefab    : {PrefabPath}");
            sb.AppendLine();
            sb.AppendLine("Scoring: (gatesHit×1000) - (lapFrames/10) - (stuckEvents×200)");
            sb.AppendLine("".PadRight(80, '-'));
            sb.AppendLine($"{"Rank",-5} {"Score",8} {"Gates",7} {"Missed",7} {"Stuck",6} {"Falls",6} {"Frames",8}  {"recovery",10} {"grace",7} {"kPYaw",7}");
            sb.AppendLine("".PadRight(80, '-'));

            for (int i = 0; i < results.Count; i++)
                sb.AppendLine(results[i].ToReportLine(i + 1));

            string dir = Path.GetDirectoryName(ReportPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(ReportPath, sb.ToString());
            Debug.Log($"[CornerOptimizer] Report written to {ReportPath}");
        }

        // ── Result record ─────────────────────────────────────────────────────────

        private class TrialResult
        {
            public int   RecoveryFrames;
            public int   GraceFrames;
            public float KPYaw;
            public int   TotalGates;
            public int   GatesHit;
            public int   GatesMissed;
            public int   FallCount;
            public int   LapFrames;
            public int   StuckEvents;

            public float Score => (GatesHit * 1000f) - (LapFrames / 10f) - (StuckEvents * 200f) - (FallCount * 1000f);

            public string ToReportLine(int rank) =>
                $"{rank,-5} {Score,8:F0} {GatesHit,7}/{TotalGates,-3} {GatesMissed,7} {StuckEvents,6} {FallCount,6} {LapFrames,8}  " +
                $"{RecoveryFrames,10} {GraceFrames,7} {KPYaw,7:F0}";
        }
    }
}
