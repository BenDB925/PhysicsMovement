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
    /// Stress tests for human-like input: hard direction snaps at full speed,
    /// no lookahead or smoothing. Validates that the character recovers from
    /// worst-case corner scenarios that a real player would experience.
    ///
    /// These deliberately replicate the failure modes found in Arena_01 manual play:
    ///   - Slalom (5 consecutive 90° turns, short segments)
    ///   - Single hard 90° snap at full speed
    ///
    /// If these fail, players will experience the bug. Fix the physics, not the test.
    /// </summary>
    public class HardSnapRecoveryTests
    {
        private const string PrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";

        private static readonly Vector3 TestOriginOffset = new Vector3(0f, 0f, 7000f);

        private const int SettleFrames = 200;
        private const int WindupFrames = 300;  // 3s of straight running to build full speed
        private const int SnapFrames   = 200;  // 2s after each snap to recover and move

        private GameObject     _instance;
        private GameObject     _ground;
        private Rigidbody      _hipsRb;
        private PlayerMovement _pm;

        [SetUp]
        public void SetUp()
        {
            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "HardSnapGround";
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
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance != null) Object.Destroy(_instance);
            if (_ground   != null) Object.Destroy(_ground);
            _instance = null;
            _ground   = null;
        }

        private IEnumerator Settle()
        {
            yield return new WaitForFixedUpdate();
            _pm?.SetCameraForTest(null);
            for (int i = 1; i < SettleFrames; i++)
                yield return new WaitForFixedUpdate();
        }

        /// <summary>
        /// Single hard 90° snap at full speed. After WindupFrames of straight running,
        /// input snaps 90° instantly with no smoothing. Character must make forward
        /// progress in the new direction within SnapFrames.
        /// </summary>
        [UnityTest]
        public IEnumerator HardSnap90_AtFullSpeed_CharacterRecoversAndMakesProgress()
        {
            yield return Settle();

            // Windup: run straight at full speed.
            for (int f = 0; f < WindupFrames; f++)
            {
                _pm.SetMoveInputForTest(Vector2.up);
                yield return new WaitForFixedUpdate();
            }

            // Hard snap 90°.
            Vector3 snapStart = new Vector3(_hipsRb.position.x, 0f, _hipsRb.position.z);
            for (int f = 0; f < SnapFrames; f++)
            {
                _pm.SetMoveInputForTest(Vector2.right);
                yield return new WaitForFixedUpdate();
            }
            Vector3 snapEnd = new Vector3(_hipsRb.position.x, 0f, _hipsRb.position.z);

            float progressInNewDir = Vector3.Dot(snapEnd - snapStart, Vector3.right);

            Assert.That(progressInNewDir, Is.GreaterThan(1.0f),
                $"After hard 90° snap at full speed, character must make > 1m progress in new " +
                $"direction within {SnapFrames} frames. Got {progressInNewDir:F2}m. " +
                $"Character is stuck in post-corner recovery loop.");
        }

        /// <summary>
        /// Slalom stress test: 5 consecutive 90° hard snaps with only 150 frames (1.5s)
        /// between each. Replicates gates 13-17 in Arena_01 which cause the drunk stumble.
        /// Character must make net forward progress in each segment.
        /// </summary>
        [UnityTest]
        public IEnumerator HardSnap_Slalom5Turns_CharacterCompletesWithoutPermastuck()
        {
            const int SegFrames = 150; // 1.5s per segment — matches Arena_01 slalom spacing
            const float MinSegProgress = 0.8f; // must travel at least 0.8m in intended direction

            yield return Settle();

            // Initial windup.
            for (int f = 0; f < WindupFrames; f++)
            {
                _pm.SetMoveInputForTest(Vector2.up);
                yield return new WaitForFixedUpdate();
            }

            // 5 hard snaps alternating +Z/+X/-Z/-X/+Z (matches slalom pattern).
            var dirs = new Vector2[]
            {
                new Vector2(1f, 0f),   // right
                new Vector2(0f, -1f),  // back
                new Vector2(-1f, 0f),  // left
                new Vector2(0f, 1f),   // forward
                new Vector2(1f, 0f),   // right
            };

            var segResults = new List<float>();
            for (int s = 0; s < dirs.Length; s++)
            {
                Vector3 inputDir3 = new Vector3(dirs[s].x, 0f, dirs[s].y);
                Vector3 start     = new Vector3(_hipsRb.position.x, 0f, _hipsRb.position.z);

                for (int f = 0; f < SegFrames; f++)
                {
                    _pm.SetMoveInputForTest(dirs[s]);
                    yield return new WaitForFixedUpdate();
                }

                Vector3 end  = new Vector3(_hipsRb.position.x, 0f, _hipsRb.position.z);
                float   disp = Vector3.Dot(end - start, inputDir3);
                segResults.Add(disp);
                Debug.Log($"[HardSnapSlalom] Seg {s} dir=({dirs[s].x},{dirs[s].y}) disp={disp:F2}m");
            }

            _pm.SetMoveInputForTest(Vector2.zero);

            // Every segment must achieve minimum forward progress.
            for (int s = 0; s < segResults.Count; s++)
            {
                Assert.That(segResults[s], Is.GreaterThan(MinSegProgress),
                    $"Slalom segment {s} (dir={dirs[s]}): displacement {segResults[s]:F2}m < {MinSegProgress}m minimum. " +
                    $"Character is stuck in post-corner recovery loop at this segment. " +
                    $"Full results: [{string.Join(", ", segResults.ConvertAll(v => v.ToString("F2")))}]");
            }
        }
    }
}
