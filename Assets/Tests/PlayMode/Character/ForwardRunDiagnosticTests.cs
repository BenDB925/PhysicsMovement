using System.Collections;
using System.Text;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Temporary diagnostic test to compare forward-running performance between
    /// the HardSnap prefab rig setup and the GaitOutcome budget.
    /// This test is NOT permanent — delete after diagnosis is complete.
    /// </summary>
    public class ForwardRunDiagnosticTests
    {
        private static readonly Vector3 TestOriginOffset = new Vector3(0f, 0f, 8000f);
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
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOriginOffset,
                SpawnOffset = new Vector3(0f, 1.1f, 0f),
                GroundName = "DiagnosticGround",
                GroundScale = new Vector3(600f, 1f, 600f),
            });
        }

        [TearDown]
        public void TearDown()
        {
            Time.fixedDeltaTime = _savedFixedDeltaTime;
            Physics.defaultSolverIterations = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
            _rig?.Dispose();
            _rig = null;
        }

        /// <summary>
        /// Runs forward for 500 frames (same budget as GaitOutcomeTests) using the
        /// same prefab rig as HardSnapRecoveryTests, and logs displacement at both
        /// the 300-frame mark (HardSnap budget) and 500-frame mark (GaitOutcome budget).
        /// Also logs whether Camera.main is non-null (to detect stale cameras from
        /// previous tests).
        /// </summary>
        [UnityTest]
        public IEnumerator ForwardRunFromRest_DiagnosticComparison()
        {
            yield return _rig.WarmUp(150);

            // Log initial state
            bool cameraExists = Camera.main != null;
            Vector3 startPos = Flatten(_rig.HipsBody.position);
            float startY = _rig.HipsBody.position.y;

            StringBuilder log = new StringBuilder();
            log.AppendLine("[ForwardRunDiagnostic] === INITIAL STATE ===");
            log.AppendLine($"  Camera.main exists: {cameraExists}");
            log.AppendLine($"  Start position: {_rig.HipsBody.position}");
            log.AppendLine($"  Start hips Y: {startY:F3}");
            log.AppendLine($"  CharacterState: {_rig.CharacterState.CurrentState}");
            log.AppendLine($"  IsGrounded: {_rig.BalanceController.IsGrounded}");
            log.AppendLine($"  IsFallen: {_rig.BalanceController.IsFallen}");

            // Log prefab serialized values for verification
            log.AppendLine("[ForwardRunDiagnostic] === PREFAB VALUES (verify against assumptions) ===");
            log.AppendLine($"  PlayerMovement._moveForce (script default 300): check prefab");

            float peakDisplacement = 0f;
            int peakFrame = 0;
            int firstFallenFrame = -1;
            int totalFallenFrames = 0;
            int maxConsecFallen = 0;
            int consecFallen = 0;
            float displacementAt300 = 0f;

            // Run 500 frames forward (Vector2.up = +Z world direction in no-camera mode)
            for (int frame = 0; frame < 500; frame++)
            {
                _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);

                Vector3 prevPos = Flatten(_rig.HipsBody.position);
                yield return new WaitForFixedUpdate();
                Vector3 curPos = Flatten(_rig.HipsBody.position);

                float displacement = Vector3.Distance(curPos, startPos);
                bool isFallen = _rig.CharacterState.CurrentState ==
                    PhysicsDrivenMovement.Character.CharacterStateType.Fallen;

                if (displacement > peakDisplacement)
                {
                    peakDisplacement = displacement;
                    peakFrame = frame;
                }

                if (isFallen)
                {
                    totalFallenFrames++;
                    consecFallen++;
                    if (consecFallen > maxConsecFallen)
                        maxConsecFallen = consecFallen;
                    if (firstFallenFrame < 0)
                        firstFallenFrame = frame;
                }
                else
                {
                    consecFallen = 0;
                }

                // Log key frames
                if (frame == 299)
                {
                    displacementAt300 = displacement;
                    float hSpeed = new Vector3(_rig.HipsBody.linearVelocity.x, 0f,
                        _rig.HipsBody.linearVelocity.z).magnitude;
                    float uprightAngle = Vector3.Angle(_rig.Hips.up, Vector3.up);
                    log.AppendLine($"[ForwardRunDiagnostic] === FRAME 300 (HardSnap budget) ===");
                    log.AppendLine($"  Displacement: {displacement:F3}m  (HardSnap requires >= 1.0m)");
                    log.AppendLine($"  Peak so far: {peakDisplacement:F3}m at frame {peakFrame}");
                    log.AppendLine($"  Hips Y: {_rig.HipsBody.position.y:F3}");
                    log.AppendLine($"  H-speed: {hSpeed:F3} m/s");
                    log.AppendLine($"  Upright angle: {uprightAngle:F1}°");
                    log.AppendLine($"  State: {_rig.CharacterState.CurrentState}");
                    log.AppendLine($"  IsFallen: {isFallen}");
                    log.AppendLine($"  Fallen frames so far: {totalFallenFrames}");
                    log.AppendLine($"  First fallen frame: {firstFallenFrame}");
                }

                // Log every 50 frames for trajectory overview
                if (frame % 50 == 0 || frame == 499)
                {
                    float hSpeed = new Vector3(_rig.HipsBody.linearVelocity.x, 0f,
                        _rig.HipsBody.linearVelocity.z).magnitude;
                    float uprightAngle = Vector3.Angle(_rig.Hips.up, Vector3.up);
                    log.AppendLine($"  f={frame:D3} disp={displacement:F3}m hSpd={hSpeed:F2}m/s " +
                        $"angle={uprightAngle:F1}° y={_rig.HipsBody.position.y:F3} " +
                        $"state={_rig.CharacterState.CurrentState} fallen={isFallen}");
                }
            }

            _rig.PlayerMovement.SetMoveInputForTest(Vector2.zero);

            float displacementAt500 = Vector3.Distance(Flatten(_rig.HipsBody.position), startPos);

            log.AppendLine($"[ForwardRunDiagnostic] === FINAL SUMMARY ===");
            log.AppendLine($"  Displacement at frame 300: {displacementAt300:F3}m (HardSnap needs >= 1.0m)");
            log.AppendLine($"  Displacement at frame 500: {displacementAt500:F3}m (GaitOutcome needs >= 2.5m)");
            log.AppendLine($"  Peak displacement: {peakDisplacement:F3}m at frame {peakFrame}");
            log.AppendLine($"  First fallen frame: {firstFallenFrame}");
            log.AppendLine($"  Total fallen frames: {totalFallenFrames}");
            log.AppendLine($"  Max consecutive fallen: {maxConsecFallen}");
            log.AppendLine($"  Camera.main was: {cameraExists}");

            Debug.Log(log.ToString());

            // No assertions - this is purely diagnostic
            Assert.Pass($"Diagnostic complete. disp@300={displacementAt300:F3}m disp@500={displacementAt500:F3}m peak={peakDisplacement:F3}m@f{peakFrame}");
        }

        private static Vector3 Flatten(Vector3 pos)
        {
            return new Vector3(pos.x, 0f, pos.z);
        }
    }
}
