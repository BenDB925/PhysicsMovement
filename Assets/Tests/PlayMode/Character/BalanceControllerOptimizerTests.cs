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
    /// BalanceController parameter optimizer — sweeps upright gains, COM stabilization,
    /// and height maintenance parameters, scores each combination by a composite metric,
    /// and writes a ranked report to Logs/bc-optimizer-results.txt.
    ///
    /// This is NOT a pass/fail test. It always passes; the value is in the report file.
    ///
    /// Scoring (composite — higher is better):
    ///   +displacement   metres walked in 5s (mobility)
    ///   +uprightScore   average uprightness over trial (1.0 = perfectly upright)
    ///   -tiltPenalty    per-frame tilt accumulation (punishes wobble)
    ///   -fallenPenalty  2m deducted if fallen at end
    ///
    /// Parameters swept:
    ///   BC upright:  _kP (1000–3000), _kD (100–400)
    ///   COM:         _comStabilizationStrength (200–600), _comStabilizationDamping (30–120)
    ///   Height:      _heightMaintenanceStrength (800–2000), _heightMaintenanceDamping (60–200)
    /// </summary>
    public class BalanceControllerOptimizerTests
    {
        private const string ArenaSceneName  = "Arena_01";
        private const string ReportPath      = "Logs/bc-optimizer-results.txt";
        private const int    SettleFrames    = 50;
        private const int    WalkFrames      = 200;
        private const float  FallenPenalty   = 2f;

        // Weights for composite score
        private const float  DisplacementWeight = 1.0f;
        private const float  UprightWeight      = 2.0f;   // uprightness matters more than speed
        private const float  TiltPenaltyWeight  = 0.5f;

        // ── Parameter grids ──────────────────────────────────────────────────
        // 3×3×2×2×2×2 = 144 trials × ~3s each ≈ 7 minutes

        private static readonly float[] KPValues       = { 1200f, 2000f, 3000f };
        private static readonly float[] KDValues       = { 100f,  200f,  350f  };
        private static readonly float[] ComStrValues   = { 200f,  400f          };
        private static readonly float[] ComDampValues  = { 40f,   80f           };
        private static readonly float[] HStrValues     = { 800f,  1500f         };
        private static readonly float[] HDampValues    = { 80f,   160f          };

        // ── Trial result ─────────────────────────────────────────────────────

        private struct TrialResult
        {
            public float KP, KD, ComStr, ComDamp, HStr, HDamp;
            public float Displacement;
            public float AvgUprightness;  // dot(hips.up, world.up) averaged over trial
            public float TiltAccumulator; // sum of (1 - uprightness) per frame
            public float Score;
            public bool  FallenAtEnd;

            public string ToReportLine(int rank) =>
                $"#{rank,3}  score={Score:F3}  disp={Displacement:F2}m  upright={AvgUprightness:F3}  tilt={TiltAccumulator:F1}  fallen={FallenAtEnd}" +
                $"  |  kP={KP}  kD={KD}  comStr={ComStr}  comDamp={ComDamp}  hStr={HStr}  hDamp={HDamp}";
        }

        // ── Main sweep ───────────────────────────────────────────────────────

        [UnityTest]
        [Timeout(600000)] // 10 minutes
        [Ignore("On-demand optimizer — excluded from standard CI run. Run manually when tuning BC parameters.")]
        public IEnumerator BCOptimizer_SweepUprightGains_WritesRankedReport()
        {
            var results = new List<TrialResult>();
            int total   = KPValues.Length * KDValues.Length
                        * ComStrValues.Length * ComDampValues.Length
                        * HStrValues.Length * HDampValues.Length;
            int current = 0;

            foreach (float kp    in KPValues)
            foreach (float kd    in KDValues)
            foreach (float cStr  in ComStrValues)
            foreach (float cDamp in ComDampValues)
            foreach (float hStr  in HStrValues)
            foreach (float hDamp in HDampValues)
            {
                current++;
                if (current % 20 == 0)
                    Debug.Log($"[BCOptimizer] Trial {current}/{total}");

                yield return LoadArenaScene();

                BalanceController bc = FindBC();
                LegAnimator       la = FindLA();
                PlayerMovement    pm = la.GetComponent<PlayerMovement>();
                Rigidbody         rb = la.GetComponent<Rigidbody>();

                SetField(bc, "_kP",                       kp);
                SetField(bc, "_kD",                       kd);
                SetField(bc, "_comStabilizationStrength", cStr);
                SetField(bc, "_comStabilizationDamping",  cDamp);
                SetField(bc, "_heightMaintenanceStrength",hStr);
                SetField(bc, "_heightMaintenanceDamping", hDamp);

                // Settle
                for (int i = 0; i < SettleFrames; i++)
                    yield return new WaitForFixedUpdate();

                Vector3 startPos      = rb.position;
                float   tiltAcc       = 0f;
                float   uprightSum    = 0f;

                pm.SetMoveInputForTest(Vector2.right);
                for (int i = 0; i < WalkFrames; i++)
                {
                    yield return new WaitForFixedUpdate();
                    // Measure uprightness: dot of hips up with world up
                    float u = Vector3.Dot(rb.transform.up, Vector3.up);
                    uprightSum += u;
                    tiltAcc    += (1f - u);
                }
                pm.SetMoveInputForTest(Vector2.zero);

                float disp      = Vector3.Distance(
                    new Vector3(startPos.x, 0f, startPos.z),
                    new Vector3(rb.position.x, 0f, rb.position.z));
                float avgUp     = uprightSum / WalkFrames;
                bool  fallen    = bc.IsFallen;
                float score     = DisplacementWeight * disp
                                + UprightWeight      * avgUp
                                - TiltPenaltyWeight  * (tiltAcc / WalkFrames)
                                - (fallen ? FallenPenalty : 0f);

                results.Add(new TrialResult
                {
                    KP = kp, KD = kd, ComStr = cStr, ComDamp = cDamp,
                    HStr = hStr, HDamp = hDamp,
                    Displacement = disp, AvgUprightness = avgUp,
                    TiltAccumulator = tiltAcc, Score = score,
                    FallenAtEnd = fallen
                });
            }

            results.Sort((a, b) => b.Score.CompareTo(a.Score));

            var sb = new StringBuilder();
            sb.AppendLine("=== BALANCE CONTROLLER OPTIMIZER RESULTS ===");
            sb.AppendLine($"Total trials: {results.Count}  |  Generated: {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            sb.AppendLine();
            sb.AppendLine($"Scoring: {DisplacementWeight}×displacement + {UprightWeight}×avgUprightness - {TiltPenaltyWeight}×avgTilt - fallen×{FallenPenalty}");
            sb.AppendLine("─────────────────────────────────────────────────────────────────────────────────────────────");
            sb.AppendLine("TOP 20:");
            sb.AppendLine();
            for (int i = 0; i < Mathf.Min(20, results.Count); i++)
                sb.AppendLine(results[i].ToReportLine(i + 1));

            sb.AppendLine();
            sb.AppendLine("─────────────────────────────────────────────────────────────────────────────────────────────");
            sb.AppendLine("BOTTOM 5 (for reference):");
            sb.AppendLine();
            for (int i = Mathf.Max(0, results.Count - 5); i < results.Count; i++)
                sb.AppendLine(results[i].ToReportLine(i + 1));

            // Also write a sensitivity summary — best values per parameter
            sb.AppendLine();
            sb.AppendLine("─────────────────────────────────────────────────────────────────────────────────────────────");
            sb.AppendLine("PARAMETER CONSENSUS (top 10 results):");
            sb.AppendLine();
            var top10 = results.GetRange(0, Mathf.Min(10, results.Count));
            sb.AppendLine($"  kP:      {MostCommon(top10, r => r.KP)}");
            sb.AppendLine($"  kD:      {MostCommon(top10, r => r.KD)}");
            sb.AppendLine($"  comStr:  {MostCommon(top10, r => r.ComStr)}");
            sb.AppendLine($"  comDamp: {MostCommon(top10, r => r.ComDamp)}");
            sb.AppendLine($"  hStr:    {MostCommon(top10, r => r.HStr)}");
            sb.AppendLine($"  hDamp:   {MostCommon(top10, r => r.HDamp)}");

            if (!Directory.Exists("Logs")) Directory.CreateDirectory("Logs");
            File.WriteAllText(ReportPath, sb.ToString());

            Debug.Log($"[BCOptimizer] Done. Report: {ReportPath}");
            Assert.Pass($"BC optimizer complete. Best score: {results[0].Score:F3}. See {ReportPath}");
        }

        // ── Sensitivity tests (quick, single-param) ──────────────────────────

        [UnityTest]
        [Timeout(300000)]
        [Ignore("On-demand optimizer — excluded from standard CI run.")]
        public IEnumerator BCOptimizer_KPSensitivity_LogsScorePerValue()
        {
            float[] values = { 500f, 800f, 1200f, 1600f, 2000f, 2500f, 3000f, 4000f };
            yield return RunSensitivity("_kP", values, "Logs/bc-kp-sensitivity.txt");
        }

        [UnityTest]
        [Timeout(300000)]
        [Ignore("On-demand optimizer — excluded from standard CI run.")]
        public IEnumerator BCOptimizer_KDSensitivity_LogsScorePerValue()
        {
            float[] values = { 50f, 100f, 150f, 200f, 300f, 400f };
            yield return RunSensitivity("_kD", values, "Logs/bc-kd-sensitivity.txt");
        }

        [UnityTest]
        [Timeout(300000)]
        [Ignore("On-demand optimizer — excluded from standard CI run.")]
        public IEnumerator BCOptimizer_ComStrengthSensitivity_LogsScorePerValue()
        {
            float[] values = { 100f, 200f, 400f, 600f, 800f };
            yield return RunSensitivity("_comStabilizationStrength", values, "Logs/bc-comstr-sensitivity.txt");
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private IEnumerator RunSensitivity(string fieldName, float[] values, string outputPath)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== {fieldName} sensitivity (all other params at prefab defaults) ===");

            foreach (float v in values)
            {
                yield return LoadArenaScene();
                BalanceController bc = FindBC();
                LegAnimator la = FindLA();
                PlayerMovement pm = la.GetComponent<PlayerMovement>();
                Rigidbody rb = la.GetComponent<Rigidbody>();

                SetField(bc, fieldName, v);

                for (int i = 0; i < SettleFrames; i++) yield return new WaitForFixedUpdate();

                Vector3 start = rb.position;
                float uprightSum = 0f;

                pm.SetMoveInputForTest(Vector2.right);
                for (int i = 0; i < WalkFrames; i++)
                {
                    yield return new WaitForFixedUpdate();
                    uprightSum += Vector3.Dot(rb.transform.up, Vector3.up);
                }
                pm.SetMoveInputForTest(Vector2.zero);

                float disp  = Vector3.Distance(new Vector3(start.x, 0f, start.z),
                                               new Vector3(rb.position.x, 0f, rb.position.z));
                float avgUp = uprightSum / WalkFrames;

                string line = $"  {fieldName}={v,8}  disp={disp:F2}m  avgUpright={avgUp:F3}  fallen={bc.IsFallen}";
                sb.AppendLine(line);
                Debug.Log($"[BCOptimizer] {line}");
            }

            if (!Directory.Exists("Logs")) Directory.CreateDirectory("Logs");
            File.WriteAllText(outputPath, sb.ToString());
            Assert.Pass($"{fieldName} sensitivity done. See {outputPath}");
        }

        private static string MostCommon(List<TrialResult> results, System.Func<TrialResult, float> selector)
        {
            var counts = new Dictionary<float, int>();
            foreach (var r in results)
            {
                float v = selector(r);
                counts[v] = counts.TryGetValue(v, out int c) ? c + 1 : 1;
            }
            float best = 0f; int bestCount = 0;
            foreach (var kv in counts)
                if (kv.Value > bestCount) { best = kv.Key; bestCount = kv.Value; }
            return $"{best}  (appears in {bestCount}/{results.Count} top results)";
        }

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

        private static void SetField(MonoBehaviour target, string fieldName, float value)
        {
            FieldInfo fi = target.GetType().GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(fi, Is.Not.Null,
                $"Field '{fieldName}' not found on {target.GetType().Name}.");
            fi.SetValue(target, value);
        }
    }
}
