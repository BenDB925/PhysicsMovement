using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Turn recovery tests: how effectively does the character complete sharp direction
    /// changes at full speed?
    ///
    /// Uses the REAL PlayerRagdoll prefab so physics material, joint limits, and spring
    /// values all match production.
    ///
    /// Pattern: alternating direction intervals (5s each). Each interval records the
    /// forward displacement (dot of hips-displacement with intended direction).
    /// A stuck or spinning character scores near 0; a good turn scores 2–4m.
    ///
    /// Tests:
    ///   180° reversal — +Z for 5s, then -Z for 5s, repeat 3 cycles
    ///   90° turn      — +Z → +X → -Z → -X, 5s each, repeat 2 full rotations
    ///
    /// Pass criteria: median displacement per interval (excluding the first warm-up
    /// interval) must exceed MinDisplacementPerInterval.
    /// </summary>
    public class TurnRecoveryTests
    {
        private const string PrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";

        private const int   SettleFrames             = 200;
        private const int   IntervalFrames           = 500;   // 5 s at 100 Hz
        private const float MinDisplacementPerInterval = 1.2f; // metres — must travel this far in input direction per interval
        private static readonly Vector3 TestOriginOffset = new Vector3(0f, 0f, 5000f);
        // ── Shared fixture fields ─────────────────────────────────────────────────

        private GameObject       _instance;
        private GameObject       _ground;
        private Rigidbody        _hipsRb;
        private PlayerMovement   _pm;
        private CharacterState   _cs;
        private BalanceController _bc;

        // ── Setup / Teardown ──────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "TurnRecoveryGround";
            _ground.transform.position   = TestOriginOffset;
            _ground.transform.localScale = new Vector3(600f, 1f, 600f);
            _ground.layer = GameSettings.LayerEnvironment;

            var groundRb = _ground.GetComponent<Rigidbody>();
            if (groundRb != null) Object.Destroy(groundRb);

            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.IsNotNull(prefab, $"PlayerRagdoll prefab not found at {PrefabPath}");

            _instance = Object.Instantiate(prefab,
                TestOriginOffset + new Vector3(0f, 1.2f, 0f),
                Quaternion.identity);

            _hipsRb = _instance.GetComponentInChildren<Rigidbody>();
            _pm     = _instance.GetComponentInChildren<PlayerMovement>();
            _cs     = _instance.GetComponentInChildren<CharacterState>();
            _bc     = _instance.GetComponentInChildren<BalanceController>();

            Assert.IsNotNull(_hipsRb, "Hips Rigidbody not found on prefab");
            Assert.IsNotNull(_pm,     "PlayerMovement not found on prefab");
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance != null) Object.Destroy(_instance);
            if (_ground   != null) Object.Destroy(_ground);
            _instance = null;
            _ground   = null;
        }

        // ── Settle helper ─────────────────────────────────────────────────────────

        private IEnumerator SettleCharacter()
        {
            // First frame: let Start() run, then null the camera so input is raw world-space.
            yield return new WaitForFixedUpdate();
            _pm?.SetCameraForTest(null);
            for (int i = 1; i < SettleFrames; i++)
                yield return new WaitForFixedUpdate();
        }

        // ── 180° reversal test ────────────────────────────────────────────────────

        /// <summary>
        /// Runs the character forward (+Z) for 5s, then backward (-Z) for 5s, repeating
        /// 3 full cycles (6 intervals total). Records how far the hips actually travel in
        /// the intended direction each interval. Skips the first interval (warm-up).
        ///
        /// This directly reproduces the hairpin failure mode: a character that can't
        /// handle a sharp direction reversal at speed will score near 0 on every
        /// reversed interval.
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator After180Reversal_CharacterRecoversAndMakesProgress()
        {
            yield return SettleCharacter();

            var displacements = new List<float>();
            var directions    = new Vector2[]
            {
                Vector2.up, Vector2.down,   // +Z, -Z
                Vector2.up, Vector2.down,
                Vector2.up, Vector2.down,
            };

            for (int interval = 0; interval < directions.Length; interval++)
            {
                Vector2 input    = directions[interval];
                Vector3 inputDir = new Vector3(input.x, 0f, input.y);

                Vector3 startPos = new Vector3(_hipsRb.position.x, 0f, _hipsRb.position.z);

                for (int f = 0; f < IntervalFrames; f++)
                {
                    _pm.SetMoveInputForTest(input);
                    yield return new WaitForFixedUpdate();
                }

                Vector3 endPos = new Vector3(_hipsRb.position.x, 0f, _hipsRb.position.z);
                float displacement = Vector3.Dot(endPos - startPos, inputDir);
                displacements.Add(displacement);
                Debug.Log($"[TurnRecovery] 180° interval {interval} dir={input} disp={displacement:F2}m");
            }

            _pm.SetMoveInputForTest(Vector2.zero);

            // Skip interval 0 (warm-up straight run).
            var scored = displacements.GetRange(1, displacements.Count - 1);
            scored.Sort();
            float median = scored[scored.Count / 2];

            Debug.Log($"[TurnRecovery] 180° median displacement (excl. warm-up): {median:F2}m " +
                      $"(min {scored[0]:F2}, max {scored[scored.Count-1]:F2})");

            Assert.That(median, Is.GreaterThanOrEqualTo(MinDisplacementPerInterval),
                $"180° turn recovery: median displacement {median:F2}m < {MinDisplacementPerInterval}m. " +
                $"Character is not making forward progress after direction reversal. " +
                $"Full results: [{string.Join(", ", displacements.ConvertAll(d => $"{d:F2}m"))}]");
        }

        // ── 90° turn test ─────────────────────────────────────────────────────────

        /// <summary>
        /// Cycles through 4 cardinal directions (+Z, +X, -Z, -X) at 5s per leg,
        /// completing 2 full rotations (8 intervals). Each 90° snap tests whether BC
        /// can yaw fast enough for the legs to regain traction within the interval.
        ///
        /// This is the hairpin corner scenario, isolated and repeatable.
        /// </summary>
        [UnityTest]
        [Timeout(120000)]
        public IEnumerator After90DegTurn_CharacterRecoversAndMakesProgress()
        {
            yield return SettleCharacter();

            var displacements = new List<float>();
            var directions    = new Vector2[]
            {
                Vector2.up,    new Vector2( 1, 0),   // +Z, +X
                Vector2.down,  new Vector2(-1, 0),   // -Z, -X
                Vector2.up,    new Vector2( 1, 0),   // repeat
                Vector2.down,  new Vector2(-1, 0),
            };

            for (int interval = 0; interval < directions.Length; interval++)
            {
                Vector2 input    = directions[interval];
                Vector3 inputDir = new Vector3(input.x, 0f, input.y);

                Vector3 startPos = new Vector3(_hipsRb.position.x, 0f, _hipsRb.position.z);

                for (int f = 0; f < IntervalFrames; f++)
                {
                    _pm.SetMoveInputForTest(input);
                    yield return new WaitForFixedUpdate();
                }

                Vector3 endPos = new Vector3(_hipsRb.position.x, 0f, _hipsRb.position.z);
                float displacement = Vector3.Dot(endPos - startPos, inputDir);
                displacements.Add(displacement);
                Debug.Log($"[TurnRecovery] 90° interval {interval} dir={input} disp={displacement:F2}m");
            }

            _pm.SetMoveInputForTest(Vector2.zero);

            // Skip interval 0 (warm-up straight run).
            var scored = displacements.GetRange(1, displacements.Count - 1);
            scored.Sort();
            float median = scored[scored.Count / 2];

            Debug.Log($"[TurnRecovery] 90° median displacement (excl. warm-up): {median:F2}m " +
                      $"(min {scored[0]:F2}, max {scored[scored.Count-1]:F2})");

            Assert.That(median, Is.GreaterThanOrEqualTo(MinDisplacementPerInterval),
                $"90° turn recovery: median displacement {median:F2}m < {MinDisplacementPerInterval}m. " +
                $"Character is not making forward progress after 90° direction change. " +
                $"Full results: [{string.Join(", ", displacements.ConvertAll(d => $"{d:F2}m"))}]");
        }
    }
}
