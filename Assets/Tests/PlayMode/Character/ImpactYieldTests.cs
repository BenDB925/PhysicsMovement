using System.Collections;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode coverage for the angular-impact yield window on the production player prefab.
    /// Verifies that a large hips angular-velocity spike temporarily softens the local
    /// balance scales and then restores them after the configured recovery window.
    /// </summary>
    public class ImpactYieldTests
    {
        private const int WarmUpFrames = 150;
        private const int YieldObservationFrames = 30;
        private const int RecoveryObservationFrames = 90;

        private static readonly Vector3 TestOrigin = new Vector3(500f, 0f, 9500f);

        private PlayerPrefabTestRig _rig;
        private float _savedFixedDeltaTime;
        private int _savedSolverIterations;
        private int _savedSolverVelocityIterations;

        [SetUp]
        public void SetUp()
        {
            _savedFixedDeltaTime = Time.fixedDeltaTime;
            _savedSolverIterations = Physics.defaultSolverIterations;
            _savedSolverVelocityIterations = Physics.defaultSolverVelocityIterations;

            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
        }

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;

            Time.fixedDeltaTime = _savedFixedDeltaTime;
            Physics.defaultSolverIterations = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
        }

        [UnityTest]
        public IEnumerator AngularVelocitySpike_YieldsBalanceThenRecovers()
        {
            // Arrange
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin,
                SpawnOffset = new Vector3(0f, 0.5f, 0f),
            });
            yield return _rig.WarmUp(WarmUpFrames);

            Assert.That(_rig.BalanceController.UprightStrengthScale, Is.EqualTo(1f).Within(0.001f));
            Assert.That(_rig.BalanceController.HeightMaintenanceScale, Is.EqualTo(1f).Within(0.001f));
            Assert.That(_rig.BalanceController.StabilizationScale, Is.EqualTo(1f).Within(0.001f));

            // Act
            _rig.HipsBody.angularVelocity = Vector3.forward * 4.5f;
            yield return new WaitForFixedUpdate();

            // Remove the injected spin after the trigger frame so the test isolates the
            // yield window rather than a prolonged forced tumble.
            _rig.HipsBody.angularVelocity = Vector3.zero;

            float minUprightScale = _rig.BalanceController.UprightStrengthScale;
            float minHeightScale = _rig.BalanceController.HeightMaintenanceScale;
            float minStabilizationScale = _rig.BalanceController.StabilizationScale;

            for (int frame = 0; frame < YieldObservationFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
                minUprightScale = Mathf.Min(minUprightScale, _rig.BalanceController.UprightStrengthScale);
                minHeightScale = Mathf.Min(minHeightScale, _rig.BalanceController.HeightMaintenanceScale);
                minStabilizationScale = Mathf.Min(minStabilizationScale, _rig.BalanceController.StabilizationScale);
            }

            for (int frame = 0; frame < RecoveryObservationFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            // Assert
            TestContext.Out.WriteLine($"[METRIC] ImpactYield MinUprightScale={minUprightScale:F3}");
            TestContext.Out.WriteLine($"[METRIC] ImpactYield MinHeightScale={minHeightScale:F3}");
            TestContext.Out.WriteLine($"[METRIC] ImpactYield MinStabilizationScale={minStabilizationScale:F3}");
            TestContext.Out.WriteLine($"[METRIC] ImpactYield FinalUprightScale={_rig.BalanceController.UprightStrengthScale:F3}");

            Assert.That(_rig.BalanceController.IsSurrendered, Is.False,
                "A brief impact-yield reaction should not by itself trigger surrender.");
            Assert.That(minUprightScale, Is.LessThan(0.25f),
                $"Expected UprightStrengthScale to yield near 0.15. Observed minimum {minUprightScale:F3}.");
            Assert.That(minHeightScale, Is.LessThan(0.25f),
                $"Expected HeightMaintenanceScale to yield near 0.15. Observed minimum {minHeightScale:F3}.");
            Assert.That(minStabilizationScale, Is.LessThan(0.25f),
                $"Expected StabilizationScale to yield near 0.15. Observed minimum {minStabilizationScale:F3}.");
            Assert.That(_rig.BalanceController.UprightStrengthScale, Is.EqualTo(1f).Within(0.05f),
                "Impact yield should restore upright support after the yield window expires.");
            Assert.That(_rig.BalanceController.HeightMaintenanceScale, Is.EqualTo(1f).Within(0.05f),
                "Impact yield should restore height maintenance after the yield window expires.");
            Assert.That(_rig.BalanceController.StabilizationScale, Is.EqualTo(1f).Within(0.05f),
                "Impact yield should restore COM stabilization after the yield window expires.");
        }
    }
}