using System;
using System.Collections.Generic;
using UnityEngine;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Measures how far each foot drifts in the XZ plane while it should be planted
    /// (stationary on the ground). Call <see cref="Sample"/> every FixedUpdate with
    /// the current foot positions and planted states. When a foot transitions from
    /// not-planted to planted its position is recorded as the anchor; while planted
    /// the XZ delta from the anchor is tracked; when it lifts off the peak drift for
    /// that stance phase is finalized.
    /// </summary>
    public sealed class PlantedFootDriftTracker
    {
        private readonly FootTracker _left = new FootTracker();
        private readonly FootTracker _right = new FootTracker();

        /// <summary>Worst single-step drift across both feet.</summary>
        public float MaxDrift => Mathf.Max(_left.MaxDrift, _right.MaxDrift);

        /// <summary>Average per-step drift across all completed steps on both feet.</summary>
        public float AverageDrift
        {
            get
            {
                int totalSteps = _left.CompletedStepCount + _right.CompletedStepCount;
                if (totalSteps == 0) return 0f;
                float totalDrift = _left.DriftSum + _right.DriftSum;
                return totalDrift / totalSteps;
            }
        }

        /// <summary>Total completed step count (both feet).</summary>
        public int CompletedStepCount => _left.CompletedStepCount + _right.CompletedStepCount;

        public float MaxDriftLeft => _left.MaxDrift;
        public float MaxDriftRight => _right.MaxDrift;
        public IReadOnlyList<float> DriftPerStepLeft => _left.DriftPerStep;
        public IReadOnlyList<float> DriftPerStepRight => _right.DriftPerStep;

        /// <summary>
        /// Call once per FixedUpdate with foot world positions and planted booleans.
        /// </summary>
        public void Sample(Vector3 leftFootPos, bool leftPlanted, Vector3 rightFootPos, bool rightPlanted)
        {
            _left.Sample(leftFootPos, leftPlanted);
            _right.Sample(rightFootPos, rightPlanted);
        }

        /// <summary>
        /// Finalize any in-progress stance phases so their drift is counted.
        /// Call after the movement phase ends to capture the last step if the
        /// foot was still planted when input stopped.
        /// </summary>
        public void Flush()
        {
            _left.Flush();
            _right.Flush();
        }

        /// <summary>Reset all tracking state.</summary>
        public void Reset()
        {
            _left.Reset();
            _right.Reset();
        }

        private sealed class FootTracker
        {
            private readonly List<float> _driftPerStep = new List<float>();
            private bool _wasPlanted;
            private Vector3 _anchorXZ;
            private float _currentStepMaxDrift;

            public float MaxDrift { get; private set; }
            public float DriftSum { get; private set; }
            public int CompletedStepCount => _driftPerStep.Count;
            public IReadOnlyList<float> DriftPerStep => _driftPerStep;

            public void Sample(Vector3 footPos, bool isPlanted)
            {
                Vector3 posXZ = new Vector3(footPos.x, 0f, footPos.z);

                if (isPlanted && !_wasPlanted)
                {
                    // Foot just became planted — record anchor.
                    _anchorXZ = posXZ;
                    _currentStepMaxDrift = 0f;
                }
                else if (isPlanted && _wasPlanted)
                {
                    // Foot still planted — accumulate drift.
                    float drift = Vector3.Distance(posXZ, _anchorXZ);
                    _currentStepMaxDrift = Mathf.Max(_currentStepMaxDrift, drift);
                }
                else if (!isPlanted && _wasPlanted)
                {
                    // Foot just lifted off — finalize this step.
                    FinalizeStep();
                }

                _wasPlanted = isPlanted;
            }

            public void Flush()
            {
                if (_wasPlanted)
                {
                    FinalizeStep();
                    _wasPlanted = false;
                }
            }

            public void Reset()
            {
                _driftPerStep.Clear();
                _wasPlanted = false;
                _anchorXZ = Vector3.zero;
                _currentStepMaxDrift = 0f;
                MaxDrift = 0f;
                DriftSum = 0f;
            }

            private void FinalizeStep()
            {
                _driftPerStep.Add(_currentStepMaxDrift);
                DriftSum += _currentStepMaxDrift;
                MaxDrift = Mathf.Max(MaxDrift, _currentStepMaxDrift);
                _currentStepMaxDrift = 0f;
            }
        }
    }
}
