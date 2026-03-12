using System.Collections;
using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Outcome-based hard-turn recovery tests for the production player prefab.
    ///
    /// These tests model player-hostile direction changes with no steering assistance:
    /// hold a full-intent direction long enough to build travel, then snap instantly to
    /// a new cardinal input. The assertions deliberately avoid internal animation fields
    /// and instead measure whether the shipped character actually keeps moving, regains
    /// progress in the new direction, avoids a long fallen window, and escapes the kind
    /// of prolonged stumble loop that players notice immediately.
    /// </summary>
    public class HardSnapRecoveryTests
    {
        private const int SettleFrames = 150;
        private const int WindupFrames = 500;
        private const int SnapFrames = 500;
        private const int SlalomSegmentFrames = 500;

        private const float MinWindupDisplacement = 1.0f;
        private const float MinHardSnapProgress = 1.25f;
        private const float MinSlalomSegmentProgress = -0.5f;
        private const float MinSlalomTotalProgress = 15f;
        private const float RecoveryProgressThreshold = 0.4f;
        private const int MaxRecoveryFrames = 200;
        private const int MaxConsecutiveFallenFrames = 150;
        private const int MaxConsecutiveStalledFrames = 200;
        private const float StalledProgressPerFrame = 0.0025f;

        private static readonly Vector3 TestOriginOffset = new Vector3(0f, 0f, 8000f);

        private PlayerPrefabTestRig _rig;

        private sealed class TurnOutcome
        {
            public float PreTurnDisplacement;
            public float PostTurnDisplacement;
            public int RecoveryFrame = -1;
            public int MaxConsecutiveFallenFrames;
            public int MaxConsecutiveStalledFrames;
        }

        private sealed class SlalomOutcome
        {
            public readonly List<float> SegmentProgress = new List<float>();
            public readonly List<int> RecoveryFrames = new List<int>();
            public int MaxConsecutiveFallenFrames;
            public int MaxConsecutiveStalledFrames;
        }

        [SetUp]
        public void SetUp()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOriginOffset,
                SpawnOffset = new Vector3(0f, 1.1f, 0f),
                GroundName = "HardSnapGround",
                GroundScale = new Vector3(600f, 1f, 600f),
            });
        }

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
        }

        /// <summary>
        /// Builds five seconds of forward travel, snaps the desired direction by 90 degrees,
        /// and asserts that the real prefab regains meaningful progress without entering a
        /// long fallen or low-progress stumble loop.
        /// </summary>
        [UnityTest]
        public IEnumerator HardSnap90_AtFullSpeed_CharacterRecoversAndMakesProgress()
        {
            yield return _rig.WarmUp(SettleFrames);

            TurnOutcome outcome = new TurnOutcome();
            yield return RunTurnScenario(Vector2.up, Vector2.right, SnapFrames, outcome);

            string summary = BuildTurnSummary(outcome);
            Debug.Log($"[HardSnap90] {summary}");
            LogBaseline(nameof(HardSnap90_AtFullSpeed_CharacterRecoversAndMakesProgress), summary);

            Assert.That(outcome.PreTurnDisplacement, Is.GreaterThanOrEqualTo(MinWindupDisplacement),
                $"Hard snap windup must build at least {MinWindupDisplacement:F2}m of travel before the turn. {summary}");

            Assert.That(outcome.PostTurnDisplacement, Is.GreaterThanOrEqualTo(MinHardSnapProgress),
                $"After a hard 90 degree snap, the character must make at least {MinHardSnapProgress:F2}m of progress in the new direction. {summary}");

            Assert.That(outcome.RecoveryFrame, Is.GreaterThanOrEqualTo(0).And.LessThanOrEqualTo(MaxRecoveryFrames),
                $"Forward progress must resume within {MaxRecoveryFrames} frames after the snap. {summary}");

            Assert.That(outcome.MaxConsecutiveFallenFrames, Is.LessThanOrEqualTo(MaxConsecutiveFallenFrames),
                $"Hard snap recovery entered a prolonged fallen window. {summary}");

            Assert.That(outcome.MaxConsecutiveStalledFrames, Is.LessThanOrEqualTo(MaxConsecutiveStalledFrames),
                $"Hard snap recovery got trapped in a long stumble loop with near-zero progress. {summary}");
        }

        /// <summary>
        /// Replays five consecutive 90 degree snaps with 5-second segments between turns.
        /// Each segment gives the character enough time to fully recover and accelerate,
        /// so the test catches corners that lead to unrecoverable states (permastuck,
        /// prolonged falls) without penalising the natural physics wobble of a ragdoll turn.
        /// </summary>
        [UnityTest]
        public IEnumerator HardSnap_Slalom5Turns_CharacterCompletesWithoutPermastuck()
        {
            yield return _rig.WarmUp(SettleFrames);

            for (int frame = 0; frame < WindupFrames; frame++)
            {
                _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
                yield return new WaitForFixedUpdate();
            }

            SlalomOutcome outcome = new SlalomOutcome();
            Vector2[] inputs =
            {
                new Vector2(1f, 0f),
                new Vector2(0f, -1f),
                new Vector2(-1f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 0f),
            };

            int fallenFrames = 0;
            int stalledFrames = 0;

            for (int segmentIndex = 0; segmentIndex < inputs.Length; segmentIndex++)
            {
                Vector3 direction = InputToWorldDirection(inputs[segmentIndex]);
                Vector3 segmentStart = Flatten(_rig.HipsBody.position);
                float cumulativeProgress = 0f;
                int recoveryFrame = -1;

                for (int frame = 0; frame < SlalomSegmentFrames; frame++)
                {
                    _rig.PlayerMovement.SetMoveInputForTest(inputs[segmentIndex]);

                    Vector3 previousPosition = Flatten(_rig.HipsBody.position);
                    yield return new WaitForFixedUpdate();
                    Vector3 currentPosition = Flatten(_rig.HipsBody.position);

                    float frameProgress = Vector3.Dot(currentPosition - previousPosition, direction);
                    cumulativeProgress += frameProgress;

                    TrackFallenAndStall(frameProgress, ref fallenFrames, ref stalledFrames, outcome);

                    if (recoveryFrame < 0 && cumulativeProgress >= RecoveryProgressThreshold)
                    {
                        recoveryFrame = frame + 1;
                    }
                }

                float segmentProgress = Vector3.Dot(Flatten(_rig.HipsBody.position) - segmentStart, direction);
                outcome.SegmentProgress.Add(segmentProgress);
                outcome.RecoveryFrames.Add(recoveryFrame);

                Debug.Log($"[HardSnapSlalom] Seg {segmentIndex} dir=({inputs[segmentIndex].x:F0},{inputs[segmentIndex].y:F0}) progress={segmentProgress:F2}m recoveryFrame={recoveryFrame}");
            }

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);

            string summary = BuildSlalomSummary(outcome);
            Debug.Log($"[HardSnapSlalom] {summary}");
            LogBaseline(nameof(HardSnap_Slalom5Turns_CharacterCompletesWithoutPermastuck), summary);

            float totalProgress = 0f;
            for (int segmentIndex = 0; segmentIndex < outcome.SegmentProgress.Count; segmentIndex++)
            {
                totalProgress += outcome.SegmentProgress[segmentIndex];
                Assert.That(outcome.SegmentProgress[segmentIndex], Is.GreaterThanOrEqualTo(MinSlalomSegmentProgress),
                    $"Slalom segment {segmentIndex} went significantly backward. {summary}");
            }

            Assert.That(totalProgress, Is.GreaterThanOrEqualTo(MinSlalomTotalProgress),
                $"Total slalom progress across all segments must be at least {MinSlalomTotalProgress:F0}m. totalProgress={totalProgress:F2}m {summary}");

            Assert.That(outcome.MaxConsecutiveFallenFrames, Is.LessThanOrEqualTo(MaxConsecutiveFallenFrames),
                $"The hard-turn slalom entered a prolonged fallen window. {summary}");

            Assert.That(outcome.MaxConsecutiveStalledFrames, Is.LessThanOrEqualTo(MaxConsecutiveStalledFrames),
                $"The hard-turn slalom got trapped in a prolonged stumble loop. {summary}");
        }

        private IEnumerator RunTurnScenario(Vector2 beforeTurnInput, Vector2 afterTurnInput, int turnFrames, TurnOutcome outcome)
        {
            // STEP 1: Build pre-turn travel so the snap happens under real movement load, not from a near-idle pose.
            Vector3 windupStart = Flatten(_rig.HipsBody.position);
            for (int frame = 0; frame < WindupFrames; frame++)
            {
                _rig.PlayerMovement.SetMoveInputForTest(beforeTurnInput);
                yield return new WaitForFixedUpdate();
            }

            Vector3 turnStart = Flatten(_rig.HipsBody.position);
            outcome.PreTurnDisplacement = Vector3.Distance(turnStart, windupStart);

            // STEP 2: Snap immediately to the new desired direction and record only observable recovery signals.
            Vector3 newDirection = InputToWorldDirection(afterTurnInput);
            float cumulativeProgress = 0f;
            int fallenFrames = 0;
            int stalledFrames = 0;

            for (int frame = 0; frame < turnFrames; frame++)
            {
                _rig.PlayerMovement.SetMoveInputForTest(afterTurnInput);

                Vector3 previousPosition = Flatten(_rig.HipsBody.position);
                yield return new WaitForFixedUpdate();
                Vector3 currentPosition = Flatten(_rig.HipsBody.position);

                float frameProgress = Vector3.Dot(currentPosition - previousPosition, newDirection);
                cumulativeProgress += frameProgress;

                TrackFallenAndStall(frameProgress, ref fallenFrames, ref stalledFrames, outcome);

                if (outcome.RecoveryFrame < 0 && cumulativeProgress >= RecoveryProgressThreshold)
                {
                    outcome.RecoveryFrame = frame + 1;
                }
            }

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);

            // STEP 3: Reduce the turn window to a small set of player-visible outcomes for clear failure messages.
            outcome.PostTurnDisplacement = Vector3.Dot(Flatten(_rig.HipsBody.position) - turnStart, newDirection);
        }

        private void TrackFallenAndStall(float frameProgress, ref int fallenFrames, ref int stalledFrames, TurnOutcome outcome)
        {
            bool isFallen = _rig.CharacterState.CurrentState == PhysicsDrivenMovement.Character.CharacterStateType.Fallen;
            if (isFallen)
            {
                fallenFrames++;
                outcome.MaxConsecutiveFallenFrames = Mathf.Max(outcome.MaxConsecutiveFallenFrames, fallenFrames);
            }
            else
            {
                fallenFrames = 0;
            }

            if (frameProgress <= StalledProgressPerFrame)
            {
                stalledFrames++;
                outcome.MaxConsecutiveStalledFrames = Mathf.Max(outcome.MaxConsecutiveStalledFrames, stalledFrames);
            }
            else
            {
                stalledFrames = 0;
            }
        }

        private void TrackFallenAndStall(float frameProgress, ref int fallenFrames, ref int stalledFrames, SlalomOutcome outcome)
        {
            bool isFallen = _rig.CharacterState.CurrentState == PhysicsDrivenMovement.Character.CharacterStateType.Fallen;
            if (isFallen)
            {
                fallenFrames++;
                outcome.MaxConsecutiveFallenFrames = Mathf.Max(outcome.MaxConsecutiveFallenFrames, fallenFrames);
            }
            else
            {
                fallenFrames = 0;
            }

            if (frameProgress <= StalledProgressPerFrame)
            {
                stalledFrames++;
                outcome.MaxConsecutiveStalledFrames = Mathf.Max(outcome.MaxConsecutiveStalledFrames, stalledFrames);
            }
            else
            {
                stalledFrames = 0;
            }
        }

        private static Vector3 InputToWorldDirection(Vector2 input)
        {
            Vector3 direction = new Vector3(input.x, 0f, input.y);
            if (direction.sqrMagnitude < 0.0001f)
            {
                return Vector3.zero;
            }

            return direction.normalized;
        }

        private static Vector3 Flatten(Vector3 position)
        {
            return new Vector3(position.x, 0f, position.z);
        }

        private static string BuildTurnSummary(TurnOutcome outcome)
        {
            return $"windup={outcome.PreTurnDisplacement:F2}m postTurn={outcome.PostTurnDisplacement:F2}m recoveryFrame={outcome.RecoveryFrame} maxFallenFrames={outcome.MaxConsecutiveFallenFrames} maxStalledFrames={outcome.MaxConsecutiveStalledFrames}";
        }

        private static string BuildSlalomSummary(SlalomOutcome outcome)
        {
            StringBuilder builder = new StringBuilder();
            builder.Append("segmentProgress=[");
            for (int index = 0; index < outcome.SegmentProgress.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(outcome.SegmentProgress[index].ToString("F2"));
                builder.Append('m');
            }

            builder.Append("] recoveryFrames=[");
            for (int index = 0; index < outcome.RecoveryFrames.Count; index++)
            {
                if (index > 0)
                {
                    builder.Append(", ");
                }

                builder.Append(outcome.RecoveryFrames[index]);
            }

            builder.Append($"] maxFallenFrames={outcome.MaxConsecutiveFallenFrames} maxStalledFrames={outcome.MaxConsecutiveStalledFrames}");
            return builder.ToString();
        }

        private static void LogBaseline(string scenario, string summary)
        {
            Debug.Log($"[C1.1 Baseline][HardSnapRecovery] {scenario} {summary}");
        }
    }
}