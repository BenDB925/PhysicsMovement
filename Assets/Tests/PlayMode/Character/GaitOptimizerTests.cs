using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Gait parameter optimizer - runs a parameter sweep across BalanceController
    /// and LegAnimator settings, scores each combination by 5-second walk displacement,
    /// and writes a ranked report to Logs/gait-optimizer-results.txt.
    ///
    /// This is NOT a pass/fail test. It always passes; the value is in the report file.
    ///
    /// How to use:
    ///   1. Run GaitOptimizer_SweepParameters in the Unity Test Runner (takes ~5-10 min)
    ///   2. Open Logs/gait-optimizer-results.txt
    ///   3. Copy the top-ranked values into the prefab Inspector
    ///
    /// Scoring:
    ///   Primary:   horizontal displacement over 5s (higher = better)
    ///   Penalties: -1m per full-spin detected (gaitFwd reversal), -2m if fallen at end
    ///
    /// Parameters swept:
    ///   BC:  _kPYaw (40-200), _kDYaw (20-120)
    ///   LA:  _stepAngle (35-65), _stepFrequencyScale (0.05-0.2), _kneeAngle (30-70),
    ///        _upperLegLiftBoost (15-50)
    /// </summary>
    public class GaitOptimizerTests
    {
        private const string ArenaSceneName  = "Arena_01";
        private const string ReportPath      = "Logs/gait-optimizer-results.txt";
        private const int    SettleFrames    = 50;    // 0.5s settle before input
        private const int    WalkFrames      = 200;   // 2s walk trial - enough to measure gait quality
        private const int    SpinPenaltyM    = 1;     // metres deducted per spin detected
        private const int    FallenPenaltyM  = 2;     // metres deducted if fallen at end

        // ── Parameter grids ──────────────────────────────────────────────────
        // QUICK sweep: 3×3×3×2×2×2 = 216 trials × ~3s each ≈ 11 min
        // For a thorough overnight sweep, run GaitOptimizer_SweepParameters_Overnight

        // BC yaw gains
        private static readonly float[] KPYawValues   = { 40f, 80f, 160f };
        private static readonly float[] KDYawValues   = { 40f, 80f, 120f };

        // LegAnimator shape params
        private static readonly float[] StepAngles    = { 40f, 60f, 75f };
        private static readonly float[] FreqScales    = { 0.10f, 0.20f };
        private static readonly float[] KneeAngles    = { 40f, 65f };
        private static readonly float[] LiftBoosts    = { 20f, 45f };

        // ── Trial result ─────────────────────────────────────────────────────

        private struct TrialResult
        {
            public float KPYaw, KDYaw, StepAngle, FreqScale, KneeAngle, LiftBoost;
            public float RawDisplacement;
            public float Score;
            public int   Spins;
            public bool  FallenAtEnd;

            public string ToReportLine(int rank) =>
                $"#{rank,3}  score={Score:F2}m  disp={RawDisplacement:F2}m  spins={Spins}  fallen={FallenAtEnd}" +
                $"  |  kPYaw={KPYaw}  kDYaw={KDYaw}" +
                $"  stepAngle={StepAngle}  freqScale={FreqScale}  kneeAngle={KneeAngle}  liftBoost={LiftBoost}";
        }

        // ── Test entry point ─────────────────────────────────────────────────

        [UnityTest]
        [Timeout(900000)] // 15 minutes – 216 trials × ~3s each
        [Ignore("On-demand optimizer — excluded from standard CI run. Run manually when tuning gait parameters.")]
        public IEnumerator GaitOptimizer_SweepParameters_WritesRankedReport()
        {
            var results = new List<TrialResult>();
            int total   = KPYawValues.Length * KDYawValues.Length
                        * StepAngles.Length * FreqScales.Length
                        * KneeAngles.Length * LiftBoosts.Length;
            int current = 0;

            foreach (float kpYaw   in KPYawValues)
            foreach (float kdYaw   in KDYawValues)
            foreach (float step    in StepAngles)
            foreach (float freq    in FreqScales)
            foreach (float knee    in KneeAngles)
            foreach (float lift    in LiftBoosts)
            {
                current++;
                Debug.Log($"[GaitOptimizer] Trial {current}/{total}: " +
                          $"kPYaw={kpYaw} kDYaw={kdYaw} step={step} freq={freq} knee={knee} lift={lift}");

                yield return LoadArenaScene();

                BalanceController bc  = FindBC();
                LegAnimator       la  = FindLA();
                PlayerMovement    pm  = la.GetComponent<PlayerMovement>();
                Rigidbody         rb  = la.GetComponent<Rigidbody>();
                BalanceController bcForState = bc;

                // Apply parameters via reflection
                SetField(bc, "_kPYaw",             kpYaw);
                SetField(bc, "_kDYaw",             kdYaw);
                SetField(la, "_stepAngle",         step);
                SetField(la, "_stepFrequencyScale",freq);
                SetField(la, "_kneeAngle",         knee);
                SetField(la, "_upperLegLiftBoost", lift);

                // Settle
                for (int i = 0; i < SettleFrames; i++)
                    yield return new WaitForFixedUpdate();

                Vector3 startPos = rb.position;
                Vector3 prevGaitFwd = Vector3.forward;
                int spins = 0;

                pm.SetMoveInputForTest(Vector2.right);
                for (int i = 0; i < WalkFrames; i++)
                {
                    yield return new WaitForFixedUpdate();

                    // Detect spin: gaitFwd dot previous < -0.5 (reversed > 120°)
                    Vector3 gf = new Vector3(rb.transform.forward.x, 0f, rb.transform.forward.z).normalized;
                    if (gf.sqrMagnitude > 0.1f && prevGaitFwd.sqrMagnitude > 0.1f)
                    {
                        if (Vector3.Dot(gf, prevGaitFwd) < -0.5f)
                            spins++;
                    }
                    prevGaitFwd = gf;
                }
                pm.SetMoveInputForTest(Vector2.zero);

                Vector3 endPos   = rb.position;
                float   disp     = Vector3.Distance(
                    new Vector3(startPos.x, 0f, startPos.z),
                    new Vector3(endPos.x, 0f, endPos.z));
                bool fallen = bc.IsFallen;
                float score = disp - spins * SpinPenaltyM - (fallen ? FallenPenaltyM : 0f);

                results.Add(new TrialResult
                {
                    KPYaw = kpYaw, KDYaw = kdYaw,
                    StepAngle = step, FreqScale = freq,
                    KneeAngle = knee, LiftBoost = lift,
                    RawDisplacement = disp,
                    Score = score,
                    Spins = spins,
                    FallenAtEnd = fallen
                });
            }

            // Sort by score descending
            results.Sort((a, b) => b.Score.CompareTo(a.Score));

            // Write report
            var sb = new StringBuilder();
            sb.AppendLine("=== GAIT OPTIMIZER RESULTS ===");
            sb.AppendLine($"Total trials: {results.Count}");
            sb.AppendLine($"Generated: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine("Scoring: displacement(m) - spins×1 - fallen×2");
            sb.AppendLine("─────────────────────────────────────────────────────────────────────");
            sb.AppendLine("TOP 20:");
            sb.AppendLine();
            for (int i = 0; i < Mathf.Min(20, results.Count); i++)
                sb.AppendLine(results[i].ToReportLine(i + 1));

            sb.AppendLine();
            sb.AppendLine("─────────────────────────────────────────────────────────────────────");
            sb.AppendLine("BOTTOM 5 (for reference):");
            sb.AppendLine();
            for (int i = Mathf.Max(0, results.Count - 5); i < results.Count; i++)
                sb.AppendLine(results[i].ToReportLine(i + 1));

            string dir = Path.GetDirectoryName(ReportPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.WriteAllText(ReportPath, sb.ToString());

            Debug.Log($"[GaitOptimizer] Done. Report written to {ReportPath}");
            Debug.Log($"[GaitOptimizer] Best: {results[0].ToReportLine(1)}");

            // Always passes - value is in the report
            Assert.Pass($"Optimizer complete. Best score: {results[0].Score:F2}m. See {ReportPath}");
        }

        // ── Quick single-parameter sensitivity tests ─────────────────────────
        // These run faster and are useful for isolating one parameter at a time.

        /// <summary>
        /// Full grid sweep - 1,296 trials × ~3s each ≈ 2+ hours.
        /// Run this overnight. Results written to Logs/gait-optimizer-results-full.txt.
        /// </summary>
        [UnityTest]
        [Timeout(10800000)] // 3 hours
        [Ignore("On-demand optimizer — excluded from standard CI run. Overnight sweep only.")]
        public IEnumerator GaitOptimizer_SweepParameters_Overnight_FullGrid()
        {
            float[] kpFull    = { 40f, 80f, 140f, 200f };
            float[] kdFull    = { 20f, 60f, 100f, 120f };
            float[] stepFull  = { 35f, 50f, 65f };
            float[] freqFull  = { 0.05f, 0.10f, 0.20f };
            float[] kneeFull  = { 30f, 50f, 70f };
            float[] liftFull  = { 15f, 32f, 50f };

            var results = new List<TrialResult>();
            int total = kpFull.Length * kdFull.Length * stepFull.Length
                      * freqFull.Length * kneeFull.Length * liftFull.Length;
            int current = 0;

            foreach (float kpYaw  in kpFull)
            foreach (float kdYaw  in kdFull)
            foreach (float step   in stepFull)
            foreach (float freq   in freqFull)
            foreach (float knee   in kneeFull)
            foreach (float lift   in liftFull)
            {
                current++;
                if (current % 50 == 0)
                    Debug.Log($"[GaitOptimizer] Overnight: {current}/{total}");

                yield return LoadArenaScene();
                BalanceController bc = FindBC();
                LegAnimator la = FindLA();
                PlayerMovement pm = la.GetComponent<PlayerMovement>();
                Rigidbody rb = la.GetComponent<Rigidbody>();

                SetField(bc, "_kPYaw", kpYaw);
                SetField(bc, "_kDYaw", kdYaw);
                SetField(la, "_stepAngle", step);
                SetField(la, "_stepFrequencyScale", freq);
                SetField(la, "_kneeAngle", knee);
                SetField(la, "_upperLegLiftBoost", lift);

                for (int i = 0; i < SettleFrames; i++)
                    yield return new WaitForFixedUpdate();

                Vector3 startPos = rb.position;
                Vector3 prevFwd = Vector3.forward;
                int spins = 0;

                pm.SetMoveInputForTest(Vector2.right);
                for (int i = 0; i < WalkFrames; i++)
                {
                    yield return new WaitForFixedUpdate();
                    Vector3 gf = new Vector3(rb.transform.forward.x, 0f, rb.transform.forward.z).normalized;
                    if (gf.sqrMagnitude > 0.1f && prevFwd.sqrMagnitude > 0.1f && Vector3.Dot(gf, prevFwd) < -0.5f)
                        spins++;
                    prevFwd = gf;
                }
                pm.SetMoveInputForTest(Vector2.zero);

                float disp  = Vector3.Distance(new Vector3(startPos.x, 0f, startPos.z),
                                               new Vector3(rb.position.x, 0f, rb.position.z));
                bool fallen = bc.IsFallen;
                results.Add(new TrialResult
                {
                    KPYaw = kpYaw, KDYaw = kdYaw, StepAngle = step,
                    FreqScale = freq, KneeAngle = knee, LiftBoost = lift,
                    RawDisplacement = disp,
                    Score = disp - spins * SpinPenaltyM - (fallen ? FallenPenaltyM : 0f),
                    Spins = spins, FallenAtEnd = fallen
                });
            }

            results.Sort((a, b) => b.Score.CompareTo(a.Score));

            var sb = new StringBuilder();
            sb.AppendLine("=== GAIT OPTIMIZER RESULTS (FULL OVERNIGHT) ===");
            sb.AppendLine($"Total trials: {results.Count}  |  Generated: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            for (int i = 0; i < Mathf.Min(20, results.Count); i++)
                sb.AppendLine(results[i].ToReportLine(i + 1));

            File.WriteAllText("Logs/gait-optimizer-results-full.txt", sb.ToString());
            Assert.Pass($"Overnight sweep done. Best: {results[0].Score:F2}m. See Logs/gait-optimizer-results-full.txt");
        }

        [UnityTest]
        [Timeout(300000)]
        [Ignore("On-demand optimizer — excluded from standard CI run.")]
        public IEnumerator GaitOptimizer_KPYawSensitivity_LogsDisplacementPerValue()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== kPYaw sensitivity (kDYaw=60, all other params at prefab defaults) ===");

            float[] testValues = { 20f, 40f, 60f, 80f, 100f, 140f, 180f, 240f, 320f, 400f };

            foreach (float kp in testValues)
            {
                yield return LoadArenaScene();
                BalanceController bc = FindBC();
                LegAnimator la = FindLA();
                PlayerMovement pm = la.GetComponent<PlayerMovement>();
                Rigidbody rb = la.GetComponent<Rigidbody>();

                SetField(bc, "_kPYaw", kp);
                SetField(bc, "_kDYaw", 60f);

                for (int i = 0; i < SettleFrames; i++) yield return new WaitForFixedUpdate();

                Vector3 start = rb.position;
                pm.SetMoveInputForTest(Vector2.right);
                for (int i = 0; i < WalkFrames; i++) yield return new WaitForFixedUpdate();
                pm.SetMoveInputForTest(Vector2.zero);

                float disp = Vector3.Distance(
                    new Vector3(start.x, 0f, start.z),
                    new Vector3(rb.position.x, 0f, rb.position.z));

                string line = $"  kPYaw={kp,6}  disp={disp:F2}m  fallen={bc.IsFallen}";
                sb.AppendLine(line);
                Debug.Log($"[GaitOptimizer] {line}");
            }

            File.WriteAllText("Logs/gait-kpyaw-sensitivity.txt", sb.ToString());
            Assert.Pass("kPYaw sensitivity done. See Logs/gait-kpyaw-sensitivity.txt");
        }

        [UnityTest]
        [Timeout(300000)]
        [Ignore("On-demand optimizer — excluded from standard CI run.")]
        public IEnumerator GaitOptimizer_StepAngleSensitivity_LogsDisplacementPerValue()
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== stepAngle sensitivity (all other params at prefab defaults) ===");

            float[] testValues = { 20f, 30f, 40f, 50f, 60f, 70f, 80f };

            foreach (float angle in testValues)
            {
                yield return LoadArenaScene();
                LegAnimator la = FindLA();
                PlayerMovement pm = la.GetComponent<PlayerMovement>();
                Rigidbody rb = la.GetComponent<Rigidbody>();
                BalanceController bc = FindBC();

                SetField(la, "_stepAngle", angle);

                for (int i = 0; i < SettleFrames; i++) yield return new WaitForFixedUpdate();

                Vector3 start = rb.position;
                pm.SetMoveInputForTest(Vector2.right);
                for (int i = 0; i < WalkFrames; i++) yield return new WaitForFixedUpdate();
                pm.SetMoveInputForTest(Vector2.zero);

                float disp = Vector3.Distance(
                    new Vector3(start.x, 0f, start.z),
                    new Vector3(rb.position.x, 0f, rb.position.z));

                string line = $"  stepAngle={angle,5}  disp={disp:F2}m  fallen={bc.IsFallen}";
                sb.AppendLine(line);
                Debug.Log($"[GaitOptimizer] {line}");
            }

            File.WriteAllText("Logs/gait-stepangle-sensitivity.txt", sb.ToString());
            Assert.Pass("stepAngle sensitivity done. See Logs/gait-stepangle-sensitivity.txt");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static IEnumerator LoadArenaScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(ArenaSceneName, LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            yield return null;
            yield return new WaitForFixedUpdate();
        }

        private static BalanceController FindBC()
        {
            var arr = Object.FindObjectsByType<BalanceController>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Assert.That(arr.Length, Is.GreaterThan(0), "No BalanceController in scene.");
            return arr[0];
        }

        private static LegAnimator FindLA()
        {
            var arr = Object.FindObjectsByType<LegAnimator>(
                FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Assert.That(arr.Length, Is.GreaterThan(0), "No LegAnimator in scene.");
            return arr[0];
        }

        /// <summary>Sets a private serialized field via reflection. Safe to use in tests.</summary>
        private static void SetField(MonoBehaviour target, string fieldName, float value)
        {
            FieldInfo fi = target.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(fi, Is.Not.Null,
                $"Field '{fieldName}' not found on {target.GetType().Name}. " +
                $"If renamed, update GaitOptimizerTests accordingly.");
            fi.SetValue(target, value);
        }
    }
}
