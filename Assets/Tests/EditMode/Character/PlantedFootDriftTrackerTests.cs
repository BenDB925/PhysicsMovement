using NUnit.Framework;
using PhysicsDrivenMovement.Tests.PlayMode;
using UnityEngine;

namespace PhysicsDrivenMovement.Tests.EditMode.Character
{
    /// <summary>
    /// Pure-logic tests for <see cref="PlantedFootDriftTracker"/>. No scene or
    /// MonoBehaviour involved — just feeds synthetic position sequences and asserts
    /// correct drift computation.
    /// </summary>
    public class PlantedFootDriftTrackerTests
    {
        private PlantedFootDriftTracker _tracker;

        [SetUp]
        public void SetUp()
        {
            _tracker = new PlantedFootDriftTracker();
        }

        [Test]
        public void StationaryPlantedFoot_DriftIsZero()
        {
            Vector3 footPos = new Vector3(1f, 0f, 2f);

            // Plant for several frames, then lift off.
            _tracker.Sample(footPos, true, Vector3.zero, false);
            _tracker.Sample(footPos, true, Vector3.zero, false);
            _tracker.Sample(footPos, true, Vector3.zero, false);
            _tracker.Sample(footPos, false, Vector3.zero, false);

            Assert.That(_tracker.CompletedStepCount, Is.EqualTo(1));
            Assert.That(_tracker.MaxDriftLeft, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(_tracker.AverageDrift, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void PlantedFootMoves_DriftMatchesDistance()
        {
            Vector3 anchor = new Vector3(1f, 0f, 2f);
            Vector3 drifted = new Vector3(1.05f, 0f, 2f); // 0.05m drift in X

            _tracker.Sample(anchor, true, Vector3.zero, false);
            _tracker.Sample(drifted, true, Vector3.zero, false);
            _tracker.Sample(anchor, false, Vector3.zero, false);

            Assert.That(_tracker.CompletedStepCount, Is.EqualTo(1));
            Assert.That(_tracker.MaxDriftLeft, Is.EqualTo(0.05f).Within(0.001f));
            Assert.That(_tracker.MaxDrift, Is.EqualTo(0.05f).Within(0.001f));
        }

        [Test]
        public void OnlyXZPlane_YAxisIgnored()
        {
            Vector3 anchor = new Vector3(0f, 0f, 0f);
            Vector3 yOnly = new Vector3(0f, 5f, 0f); // foot goes up but XZ unchanged

            _tracker.Sample(anchor, true, Vector3.zero, false);
            _tracker.Sample(yOnly, true, Vector3.zero, false);
            _tracker.Sample(anchor, false, Vector3.zero, false);

            Assert.That(_tracker.MaxDriftLeft, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void MultiplePlantCycles_TracksEachSeparately()
        {
            // Step 1: left drifts 0.02m
            _tracker.Sample(new Vector3(0f, 0f, 0f), true, Vector3.zero, false);
            _tracker.Sample(new Vector3(0.02f, 0f, 0f), true, Vector3.zero, false);
            _tracker.Sample(Vector3.zero, false, Vector3.zero, false);

            // Step 2: left drifts 0.08m
            _tracker.Sample(new Vector3(1f, 0f, 1f), true, Vector3.zero, false);
            _tracker.Sample(new Vector3(1.08f, 0f, 1f), true, Vector3.zero, false);
            _tracker.Sample(Vector3.zero, false, Vector3.zero, false);

            Assert.That(_tracker.CompletedStepCount, Is.EqualTo(2));
            Assert.That(_tracker.MaxDriftLeft, Is.EqualTo(0.08f).Within(0.001f));
            Assert.That(_tracker.DriftPerStepLeft.Count, Is.EqualTo(2));
            Assert.That(_tracker.DriftPerStepLeft[0], Is.EqualTo(0.02f).Within(0.001f));
            Assert.That(_tracker.DriftPerStepLeft[1], Is.EqualTo(0.08f).Within(0.001f));
            Assert.That(_tracker.AverageDrift, Is.EqualTo(0.05f).Within(0.001f));
        }

        [Test]
        public void BothFeet_MaxDriftIsWorstOfBoth()
        {
            // Left drifts 0.03m
            _tracker.Sample(new Vector3(0f, 0f, 0f), true, new Vector3(0f, 0f, 0f), true);
            _tracker.Sample(new Vector3(0.03f, 0f, 0f), true, new Vector3(0.07f, 0f, 0f), true);
            _tracker.Sample(Vector3.zero, false, Vector3.zero, false);

            Assert.That(_tracker.MaxDriftLeft, Is.EqualTo(0.03f).Within(0.001f));
            Assert.That(_tracker.MaxDriftRight, Is.EqualTo(0.07f).Within(0.001f));
            Assert.That(_tracker.MaxDrift, Is.EqualTo(0.07f).Within(0.001f));
        }

        [Test]
        public void Flush_FinalizesInProgressStance()
        {
            Vector3 anchor = new Vector3(0f, 0f, 0f);
            Vector3 drifted = new Vector3(0.04f, 0f, 0f);

            _tracker.Sample(anchor, true, Vector3.zero, false);
            _tracker.Sample(drifted, true, Vector3.zero, false);
            // Still planted — no lift-off yet.

            Assert.That(_tracker.CompletedStepCount, Is.EqualTo(0), "Step should not finalize without lift-off or flush.");

            _tracker.Flush();

            Assert.That(_tracker.CompletedStepCount, Is.EqualTo(1));
            Assert.That(_tracker.MaxDriftLeft, Is.EqualTo(0.04f).Within(0.001f));
        }

        [Test]
        public void Reset_ClearsAllState()
        {
            _tracker.Sample(new Vector3(0f, 0f, 0f), true, Vector3.zero, false);
            _tracker.Sample(new Vector3(0.1f, 0f, 0f), true, Vector3.zero, false);
            _tracker.Sample(Vector3.zero, false, Vector3.zero, false);

            Assert.That(_tracker.CompletedStepCount, Is.EqualTo(1));

            _tracker.Reset();

            Assert.That(_tracker.CompletedStepCount, Is.EqualTo(0));
            Assert.That(_tracker.MaxDrift, Is.EqualTo(0f));
            Assert.That(_tracker.AverageDrift, Is.EqualTo(0f));
        }

        [Test]
        public void NeverPlanted_NoDriftRecorded()
        {
            // Foot swinging around but never planted.
            _tracker.Sample(new Vector3(0f, 0f, 0f), false, Vector3.zero, false);
            _tracker.Sample(new Vector3(1f, 0f, 0f), false, Vector3.zero, false);
            _tracker.Sample(new Vector3(2f, 0f, 0f), false, Vector3.zero, false);

            Assert.That(_tracker.CompletedStepCount, Is.EqualTo(0));
            Assert.That(_tracker.MaxDrift, Is.EqualTo(0f));
        }

        [Test]
        public void PeakDriftIsRecorded_NotFinalPosition()
        {
            // Foot planted, drifts out to 0.10m, then comes back to 0.02m before lift-off.
            _tracker.Sample(new Vector3(0f, 0f, 0f), true, Vector3.zero, false);
            _tracker.Sample(new Vector3(0.10f, 0f, 0f), true, Vector3.zero, false);
            _tracker.Sample(new Vector3(0.02f, 0f, 0f), true, Vector3.zero, false);
            _tracker.Sample(Vector3.zero, false, Vector3.zero, false);

            Assert.That(_tracker.MaxDriftLeft, Is.EqualTo(0.10f).Within(0.001f),
                "Tracker should record peak drift, not final drift.");
        }
    }
}
