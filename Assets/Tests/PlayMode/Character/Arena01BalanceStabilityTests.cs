using System.Collections;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Scene-level PlayMode stability tests against the production Arena_01 scene.
    /// Validates startup settle and long-run behavior for the in-scene PlayerRagdoll.
    /// </summary>
    public class Arena01BalanceStabilityTests
    {
        private const string ArenaSceneName = "Arena_01";
        private const int LongRunFrames = 2000;              // 20s @ 100 Hz
        private const int RepeatRuns = 3;
        private const int RepeatRunFrames = 800;             // 8s @ 100 Hz

        private float _originalFixedDeltaTime;
        private int _originalSolverIterations;
        private int _originalSolverVelocityIterations;
        private bool[,] _originalLayerCollisionMatrix;

        [SetUp]
        public void SetUp()
        {
            _originalFixedDeltaTime = Time.fixedDeltaTime;
            _originalSolverIterations = Physics.defaultSolverIterations;
            _originalSolverVelocityIterations = Physics.defaultSolverVelocityIterations;
            _originalLayerCollisionMatrix = CaptureLayerCollisionMatrix();

            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
        }

        [TearDown]
        public void TearDown()
        {
            Time.fixedDeltaTime = _originalFixedDeltaTime;
            Physics.defaultSolverIterations = _originalSolverIterations;
            Physics.defaultSolverVelocityIterations = _originalSolverVelocityIterations;
            RestoreLayerCollisionMatrix(_originalLayerCollisionMatrix);
        }

        [UnityTest]
        public IEnumerator Arena01_InScenePlayerRagdoll_LongRun_IsRecoverablyStable()
        {
            yield return LoadArenaScene();
            BalanceController balance = FindActiveBalanceController();

            float maxTilt = 0f;
            float minHipsHeight = float.MaxValue;
            int fallenFrameCount = 0;

            for (int i = 0; i < LongRunFrames; i++)
            {
                yield return new WaitForFixedUpdate();

                float tilt = Vector3.Angle(balance.transform.up, Vector3.up);
                float hipsHeight = balance.transform.position.y;

                maxTilt = Mathf.Max(maxTilt, tilt);
                minHipsHeight = Mathf.Min(minHipsHeight, hipsHeight);

                if (balance.IsFallen)
                {
                    fallenFrameCount++;
                }
            }

            Assert.That(maxTilt, Is.LessThan(75f),
                $"Arena_01 long-run exceeded catastrophic tilt threshold: maxTilt={maxTilt:F1}° (limit < 75°).");
            Assert.That(minHipsHeight, Is.GreaterThan(0.60f),
                $"Arena_01 long-run dropped below seated-collapse floor: minHipsHeight={minHipsHeight:F2}m (limit > 0.60m).");
            Assert.That(fallenFrameCount, Is.LessThan(600),
                $"Arena_01 long-run spent too many frames fallen: fallenFrameCount={fallenFrameCount} / {LongRunFrames} (limit < 600).");
            Assert.That(balance.IsGrounded, Is.True,
                "Arena_01 long-run ended ungrounded; expected at least one grounded foot in final state.");
        }

        [UnityTest]
        public IEnumerator Arena01_StartupSettle_RepeatabilitySweep_StaysWithinSafetyBounds()
        {
            for (int runIndex = 0; runIndex < RepeatRuns; runIndex++)
            {
                yield return LoadArenaScene();
                BalanceController balance = FindActiveBalanceController();

                float maxTilt = 0f;
                float minHipsHeight = float.MaxValue;
                int fallenFrameCount = 0;

                for (int frame = 0; frame < RepeatRunFrames; frame++)
                {
                    yield return new WaitForFixedUpdate();

                    float tilt = Vector3.Angle(balance.transform.up, Vector3.up);
                    float hipsHeight = balance.transform.position.y;

                    maxTilt = Mathf.Max(maxTilt, tilt);
                    minHipsHeight = Mathf.Min(minHipsHeight, hipsHeight);

                    if (balance.IsFallen)
                    {
                        fallenFrameCount++;
                    }
                }

                Assert.That(maxTilt, Is.LessThan(75f),
                    $"Repeat run {runIndex + 1}/{RepeatRuns} exceeded maxTilt bound: {maxTilt:F1}° (limit < 75°).");
                Assert.That(minHipsHeight, Is.GreaterThan(0.60f),
                    $"Repeat run {runIndex + 1}/{RepeatRuns} dipped below minHipsHeight bound: {minHipsHeight:F2}m (limit > 0.60m).");
                Assert.That(fallenFrameCount, Is.LessThan(300),
                    $"Repeat run {runIndex + 1}/{RepeatRuns} had too many fallen frames: {fallenFrameCount} / {RepeatRunFrames} (limit < 300).");
                Assert.That(balance.IsGrounded, Is.True,
                    $"Repeat run {runIndex + 1}/{RepeatRuns} ended with no grounded foot.");
            }
        }

        private static IEnumerator LoadArenaScene()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync(ArenaSceneName, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null, $"Failed to start async load for scene '{ArenaSceneName}'.");

            while (!load.isDone)
            {
                yield return null;
            }

            yield return null;
            yield return new WaitForFixedUpdate();
        }

        private static BalanceController FindActiveBalanceController()
        {
            BalanceController[] controllers = Object.FindObjectsByType<BalanceController>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);

            Assert.That(controllers.Length, Is.GreaterThan(0),
                "Arena_01 must contain at least one active PlayerRagdoll with BalanceController.");

            return controllers[0];
        }

        private static bool[,] CaptureLayerCollisionMatrix()
        {
            bool[,] matrix = new bool[32, 32];
            for (int a = 0; a < 32; a++)
            {
                for (int b = 0; b < 32; b++)
                {
                    matrix[a, b] = Physics.GetIgnoreLayerCollision(a, b);
                }
            }

            return matrix;
        }

        private static void RestoreLayerCollisionMatrix(bool[,] matrix)
        {
            if (matrix == null || matrix.GetLength(0) != 32 || matrix.GetLength(1) != 32)
            {
                return;
            }

            for (int a = 0; a < 32; a++)
            {
                for (int b = 0; b < 32; b++)
                {
                    Physics.IgnoreLayerCollision(a, b, matrix[a, b]);
                }
            }
        }
    }
}
