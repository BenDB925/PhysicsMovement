using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Optimizer sweep for yaw controller parameters (kPYaw, kDYaw).
    ///
    /// Scores each combination on a COMBINED metric:
    ///   - Straight score: displacement in 5s of straight running (higher = better)
    ///   - Turn score: median displacement per interval across 4× 90° turns (higher = better)
    ///   - Combined = turnScore * 0.6 + straightScore * 0.4
    ///
    /// We weight turns more heavily because straight-line locomotion is already good;
    /// the gap is in cornering.
    ///
    /// All tests are [Ignore] — run on demand only (not in CI).
    /// Results written to Logs/turn-optimizer-results.txt.
    /// Apply winning values to prefab via RagdollBuilder — never change C# defaults.
    /// </summary>
    public class TurnOptimizerTests
    {
        private const string PrefabPath   = "Assets/Prefabs/PlayerRagdoll.prefab";
        private const string ResultsPath  = "Logs/turn-optimizer-results.txt";
        private const int    SettleFrames = 200;
        private const int    IntervalFrames = 500; // 5s at 100Hz

        private static readonly Vector3 TestOriginOffset = new Vector3(0f, 0f, 6000f);

        // ── Sweep ranges ──────────────────────────────────────────────────────────

        private static readonly float[] KPYawValues  = { 80f, 120f, 160f, 200f, 240f, 300f };
        private static readonly float[] KDYawValues  = { 20f, 40f, 60f, 80f, 100f };

        // ── Shared fixture ────────────────────────────────────────────────────────

        private GameObject        _instance;
        private GameObject        _ground;
        private Rigidbody         _hipsRb;
        private PlayerMovement    _pm;
        private BalanceController _bc;

        [SetUp]
        public void SetUp()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "TurnOptGround";
            _ground.transform.position   = TestOriginOffset;
            _ground.transform.localScale = new Vector3(600f, 1f, 600f);
            _ground.layer = GameSettings.LayerEnvironment;
            var rb = _ground.GetComponent<Rigidbody>();
            if (rb != null) Object.Destroy(rb);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"Prefab not found at {PrefabPath}");
            _instance = Object.Instantiate(prefab,
                TestOriginOffset + new Vector3(0f, 1.2f, 0f), Quaternion.identity);

            _hipsRb = _instance.GetComponentInChildren<Rigidbody>();
            _pm     = _instance.GetComponentInChildren<PlayerMovement>();
            _bc     = _instance.GetComponentInChildren<BalanceController>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance != null) Object.Destroy(_instance);
            if (_ground   != null) Object.Destroy(_ground);
        }

        // ── Helper: set BC yaw params via reflection ──────────────────────────────

        private void SetYawParams(float kPYaw, float kDYaw)
        {
            if (_bc == null) return;
            var t = typeof(BalanceController);
            var kp = t.GetField("_kPYaw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var kd = t.GetField("_kDYaw", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            kp?.SetValue(_bc, kPYaw);
            kd?.SetValue(_bc, kDYaw);
        }

        // ── Helper: settle ────────────────────────────────────────────────────────

        private IEnumerator Settle()
        {
            yield return new WaitForFixedUpdate();
            _pm?.SetCameraForTest(null);
            for (int i = 1; i < SettleFrames; i++)
                yield return new WaitForFixedUpdate();
        }

        // ── Helper: measure straight displacement ─────────────────────────────────

        private IEnumerator MeasureStraight(System.Action<float> result)
        {
            Vector3 start = new Vector3(_hipsRb.position.x, 0f, _hipsRb.position.z);
            for (int f = 0; f < IntervalFrames; f++)
            {
                _pm.SetMoveInputForTest(Vector2.up);
                yield return new WaitForFixedUpdate();
            }
            Vector3 end = new Vector3(_hipsRb.position.x, 0f, _hipsRb.position.z);
            result(Vector3.Dot(end - start, Vector3.forward));
        }

        // ── Helper: measure 90° turn median ──────────────────────────────────────

        private IEnumerator Measure90TurnMedian(System.Action<float> result)
        {
            var dirs = new Vector2[]
            {
                Vector2.up, new Vector2(1,0), Vector2.down, new Vector2(-1,0),
                Vector2.up, new Vector2(1,0), Vector2.down, new Vector2(-1,0),
            };
            var disps = new List<float>();
            for (int interval = 0; interval < dirs.Length; interval++)
            {
                Vector2 input    = dirs[interval];
                Vector3 inputDir = new Vector3(input.x, 0f, input.y);
                Vector3 start    = new Vector3(_hipsRb.position.x, 0f, _hipsRb.position.z);
                for (int f = 0; f < IntervalFrames; f++)
                {
                    _pm.SetMoveInputForTest(input);
                    yield return new WaitForFixedUpdate();
                }
                Vector3 end = new Vector3(_hipsRb.position.x, 0f, _hipsRb.position.z);
                disps.Add(Vector3.Dot(end - start, inputDir));
            }
            _pm.SetMoveInputForTest(Vector2.zero);
            // Skip first (warm-up), take median of rest.
            var scored = disps.GetRange(1, disps.Count - 1);
            scored.Sort();
            result(scored[scored.Count / 2]);
        }

        // ── Quick sweep: 6×5 = 30 combos ─────────────────────────────────────────

        [Ignore("Run on demand — optimizer sweep, not CI")]
        [UnityTest]
        [Timeout(600000)] // 10 min
        public IEnumerator TurnOptimizer_SweepKPYawKDYaw_WritesRankedReport()
        {
            var rows = new List<(float kp, float kd, float straight, float turn, float combined)>();

            foreach (float kp in KPYawValues)
            foreach (float kd in KDYawValues)
            {
                // Destroy old instance, spawn fresh.
                if (_instance != null) Object.Destroy(_instance);
                if (_ground   != null) Object.Destroy(_ground);
                yield return null;

                _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
                _ground.transform.position   = TestOriginOffset;
                _ground.transform.localScale = new Vector3(600f, 1f, 600f);
                _ground.layer = GameSettings.LayerEnvironment;
                var grb = _ground.GetComponent<Rigidbody>();
                if (grb != null) Object.Destroy(grb);

                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
                _instance = Object.Instantiate(prefab,
                    TestOriginOffset + new Vector3(0f, 1.2f, 0f), Quaternion.identity);
                _hipsRb = _instance.GetComponentInChildren<Rigidbody>();
                _pm     = _instance.GetComponentInChildren<PlayerMovement>();
                _bc     = _instance.GetComponentInChildren<BalanceController>();

                SetYawParams(kp, kd);
                yield return Settle();

                float straightDisp = 0f;
                yield return MeasureStraight(v => straightDisp = v);

                float turnMedian = 0f;
                yield return Measure90TurnMedian(v => turnMedian = v);

                float combined = turnMedian * 0.6f + straightDisp * 0.4f;
                rows.Add((kp, kd, straightDisp, turnMedian, combined));
                Debug.Log($"[TurnOpt] kPYaw={kp} kDYaw={kd} | straight={straightDisp:F2} turn={turnMedian:F2} combined={combined:F2}");
            }

            // Sort by combined score descending and write report.
            rows.Sort((a, b) => b.combined.CompareTo(a.combined));

            Directory.CreateDirectory("Logs");
            var sb = new StringBuilder();
            sb.AppendLine("Turn Optimizer Results — kPYaw × kDYaw sweep");
            sb.AppendLine("Combined = turnMedian×0.6 + straightDisp×0.4");
            sb.AppendLine("─────────────────────────────────────────────────────────");
            sb.AppendLine($"{"Rank",-5}{"kPYaw",-8}{"kDYaw",-8}{"Straight",-12}{"Turn",-12}{"Combined",-10}");
            sb.AppendLine("─────────────────────────────────────────────────────────");
            for (int i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                sb.AppendLine($"{i+1,-5}{r.kp,-8}{r.kd,-8}{r.straight,-12:F2}{r.turn,-12:F2}{r.combined,-10:F2}");
            }
            sb.AppendLine($"\nTop result: kPYaw={rows[0].kp} kDYaw={rows[0].kd} → combined={rows[0].combined:F2}");
            File.WriteAllText(ResultsPath, sb.ToString());

            Debug.Log($"[TurnOpt] Complete. Top: kPYaw={rows[0].kp} kDYaw={rows[0].kd} combined={rows[0].combined:F2}. Report: {ResultsPath}");
            Assert.Pass($"Sweep complete. Top: kPYaw={rows[0].kp} kDYaw={rows[0].kd} combined={rows[0].combined:F2}");
        }
    }
}
