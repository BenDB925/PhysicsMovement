using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    public class JumpTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";
        private const int SettleFrames = 80;

        /// <summary>
        /// Default wind-up duration set on the prefab / at runtime (0.2 s).
        /// At fixedDeltaTime 0.01 s this is 20 physics frames.
        /// </summary>
        private const float WindUpDuration = 0.2f;

        private static readonly Vector3 TestOrigin = new Vector3(1650f, 0f, 0f);

        private GameObject _ground;
        private GameObject _player;
        private Rigidbody _hipsBody;
        private BalanceController _balance;
        private PlayerMovement _movement;
        private CharacterState _characterState;
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

            _ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _ground.name = "JumpTests_Ground";
            _ground.transform.position = TestOrigin + new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(40f, 1f, 40f);
            _ground.layer = GameSettings.LayerEnvironment;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null, "PlayerRagdoll prefab must be loadable from Assets/Prefabs.");

            _player = UnityEngine.Object.Instantiate(prefab, TestOrigin + new Vector3(0f, 1.1f, 0f), Quaternion.identity);
            _hipsBody = _player.GetComponent<Rigidbody>();
            _balance = _player.GetComponent<BalanceController>();
            _movement = _player.GetComponent<PlayerMovement>();
            _characterState = _player.GetComponent<CharacterState>();

            Assert.That(_hipsBody, Is.Not.Null);
            Assert.That(_balance, Is.Not.Null);
            Assert.That(_movement, Is.Not.Null);
            Assert.That(_characterState, Is.Not.Null);

            _movement.SetMoveInputForTest(Vector2.zero);
        }

        [TearDown]
        public void TearDown()
        {
            if (_player != null)
            {
                UnityEngine.Object.Destroy(_player);
            }

            if (_ground != null)
            {
                UnityEngine.Object.Destroy(_ground);
            }

            Time.fixedDeltaTime = _savedFixedDeltaTime;
            Physics.defaultSolverIterations = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
        }

        [UnityTest]
        public IEnumerator Jump_WhenStandingAndGrounded_AppliesUpwardImpulse()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);
            _hipsBody.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);

            // Wait for wind-up to complete + 1 frame for the impulse to register.
            int windUpFrames = Mathf.CeilToInt(WindUpDuration / Time.fixedDeltaTime) + 1;
            yield return WaitForPhysicsFrames(windUpFrames);

            Assert.That(_hipsBody.linearVelocity.y, Is.GreaterThan(0.1f));
        }

        [UnityTest]
        public IEnumerator Jump_WhenMovingAndGrounded_AppliesUpwardImpulse()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);
            _movement.SetMoveInputForTest(new Vector2(0.11f, 0f));
            yield return WaitForPhysicsFrames(2);
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Moving));

            _hipsBody.linearVelocity = new Vector3(_hipsBody.linearVelocity.x, 0f, _hipsBody.linearVelocity.z);
            _movement.SetJumpInputForTest(true);

            // Wait for wind-up to complete + 1 frame for the impulse to register.
            int windUpFrames = Mathf.CeilToInt(WindUpDuration / Time.fixedDeltaTime) + 1;
            yield return WaitForPhysicsFrames(windUpFrames);

            Assert.That(_hipsBody.linearVelocity.y, Is.GreaterThan(0.1f));
        }

        [UnityTest]
        public IEnumerator Jump_WhenFallen_DoesNotApplyImpulse()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: true);
            yield return new WaitForFixedUpdate();
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Fallen));

            _hipsBody.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(_hipsBody.linearVelocity.y, Is.LessThan(0.5f));
        }

        [UnityTest]
        public IEnumerator Jump_WhenAirborne_DoesNotApplyImpulse()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);
            _balance.SetGroundStateForTest(isGrounded: false, isFallen: false);
            yield return new WaitForFixedUpdate();
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Airborne));

            _hipsBody.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(_hipsBody.linearVelocity.y, Is.LessThan(0.5f));
        }

        [UnityTest]
        public IEnumerator Jump_WhenHeldForSecondFrame_DoesNotFireAgain()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);
            _hipsBody.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);

            // Wait for wind-up + impulse.
            int windUpFrames = Mathf.CeilToInt(WindUpDuration / Time.fixedDeltaTime) + 1;
            yield return WaitForPhysicsFrames(windUpFrames);
            Assert.That(_hipsBody.linearVelocity.y, Is.GreaterThan(0.1f));

            // Second frame without re-pressing should not fire again.
            _hipsBody.linearVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();

            Assert.That(_hipsBody.linearVelocity.y, Is.LessThan(0.5f));
        }

        [UnityTest]
        public IEnumerator Jump_WhileGettingUp_IsNotApplied()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);
            SetPrivateField(_characterState, "_getUpForce", 0f);
            _characterState.SetStateForTest(CharacterStateType.GettingUp);
            _hipsBody.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(_hipsBody.linearVelocity.y, Is.LessThan(0.5f));
        }

        [UnityTest]
        public IEnumerator Jump_HeldDuringFallen_DoesNotFireOnGetUp()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);
            SetPrivateField(_characterState, "_getUpForce", 0f);
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: true);
            _movement.SetJumpInputForTest(true);
            _hipsBody.linearVelocity = Vector3.zero;

            yield return WaitForPhysicsFrames(5);
            _characterState.SetStateForTest(CharacterStateType.GettingUp);
            yield return WaitForPhysicsFrames(5);

            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Standing);
            _hipsBody.linearVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();

            Assert.That(_hipsBody.linearVelocity.y, Is.LessThan(0.5f));
        }

        // ── C8.5e: Full jump lifecycle tests ────────────────────────────

        [UnityTest]
        public IEnumerator WindUp_LowersHipsDuringCrouch()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);

            float standingY = _hipsBody.position.y;

            _movement.SetJumpInputForTest(true);
            Assert.That(_movement.CurrentJumpPhase, Is.EqualTo(JumpPhase.None),
                "Jump should not start until FixedUpdate processes the input.");

            // Wait for most of the wind-up so the height-maintenance spring
            // has time to push the hips toward the lowered target.
            int nearEndWindUpFrames = Mathf.CeilToInt(WindUpDuration / Time.fixedDeltaTime * 0.9f);
            yield return WaitForPhysicsFrames(nearEndWindUpFrames);

            Assert.That(_movement.CurrentJumpPhase, Is.EqualTo(JumpPhase.WindUp),
                "Should still be in WindUp phase near the end.");
            Assert.That(_hipsBody.position.y, Is.LessThan(standingY + 0.001f),
                "Hips should stay at or below standing height during the wind-up crouch (1 mm tolerance for physics noise).");
        }

        [UnityTest]
        public IEnumerator Launch_ProducesUpwardVelocity_OnlyAfterWindUpCompletes()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);

            _hipsBody.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);

            // Sample velocity each frame through wind-up. Upward velocity should
            // stay near zero (the crouch actually pushes down slightly).
            int windUpFrames = Mathf.CeilToInt(WindUpDuration / Time.fixedDeltaTime);
            float maxUpwardDuringWindUp = float.NegativeInfinity;
            for (int i = 0; i < windUpFrames - 1; i++)
            {
                yield return new WaitForFixedUpdate();
                if (_hipsBody.linearVelocity.y > maxUpwardDuringWindUp)
                {
                    maxUpwardDuringWindUp = _hipsBody.linearVelocity.y;
                }
            }

            Assert.That(maxUpwardDuringWindUp, Is.LessThan(1.0f),
                "No significant upward velocity should occur during wind-up.");

            // Wait 2 more frames for the impulse to fire at wind-up expiry.
            yield return WaitForPhysicsFrames(2);

            Assert.That(_hipsBody.linearVelocity.y, Is.GreaterThan(0.1f),
                "Upward velocity should appear only after wind-up completes and impulse fires.");
        }

        [UnityTest]
        public IEnumerator WindUp_CommitsToJump_EvenIfInputReleased()
        {
            // Policy: once wind-up starts the jump always commits.
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);

            _hipsBody.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(_movement.CurrentJumpPhase, Is.EqualTo(JumpPhase.WindUp),
                "Wind-up should have started.");

            // Release the jump input during wind-up.
            _movement.SetJumpInputForTest(false);

            // Wait for the full wind-up + 1 frame for impulse.
            int remainingWindUp = Mathf.CeilToInt(WindUpDuration / Time.fixedDeltaTime);
            yield return WaitForPhysicsFrames(remainingWindUp);

            Assert.That(_hipsBody.linearVelocity.y, Is.GreaterThan(0.1f),
                "Impulse should fire even though jump input was released during wind-up.");
        }

        [UnityTest]
        public IEnumerator WindUp_AbortsIfCharacterFallsDuringPreparation()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);

            _hipsBody.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(_movement.CurrentJumpPhase, Is.EqualTo(JumpPhase.WindUp),
                "Wind-up should have started.");

            // Force a fall during wind-up.
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: true);
            yield return WaitForPhysicsFrames(2);

            Assert.That(_movement.CurrentJumpPhase, Is.EqualTo(JumpPhase.None),
                "Wind-up should abort when the character falls.");

            // Wait past original wind-up expiry — no impulse should fire.
            int fullWindUp = Mathf.CeilToInt(WindUpDuration / Time.fixedDeltaTime);
            _hipsBody.linearVelocity = Vector3.zero;
            yield return WaitForPhysicsFrames(fullWindUp);

            Assert.That(_hipsBody.linearVelocity.y, Is.LessThan(0.5f),
                "No upward impulse should fire after a wind-up abort.");
        }

        [UnityTest]
        public IEnumerator WindUp_WhenGroundIsLostNearCompletion_StillLaunchesJump()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);

            _hipsBody.linearVelocity = Vector3.zero;
            _movement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(_movement.CurrentJumpPhase, Is.EqualTo(JumpPhase.WindUp),
                "Wind-up should have started.");

            int nearEndWindUpFrames = Mathf.CeilToInt(WindUpDuration / Time.fixedDeltaTime) - 2;
            yield return WaitForPhysicsFrames(nearEndWindUpFrames);

            _balance.SetGroundStateForTest(isGrounded: false, isFallen: false);
            yield return new WaitForFixedUpdate();

            Assert.That(_movement.CurrentJumpPhase, Is.EqualTo(JumpPhase.Launch),
                "Late transient ground loss should still commit the jump instead of aborting the accepted wind-up.");
            Assert.That(_hipsBody.linearVelocity.y, Is.GreaterThan(0.1f),
                "A late ground-loss commit should still fire the jump impulse.");
        }

        [UnityTest]
        public IEnumerator SprintJump_WhenLaunchCommits_EntersAirborneWithinShortBudget()
        {
            const int SprintRampFrames = 500;
            const int AirborneBudgetFrames = 20;

            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();

            // Clear the test seam so the real foot sensors drive grounded/airborne transitions.
            SetPrivateField(_balance, "_overrideGroundState", false);
            yield return WaitForPhysicsFrames(5);

            _movement.SetMoveInputForTest(Vector2.up);
            _movement.SetSprintInputForTest(true);
            yield return WaitForPhysicsFrames(SprintRampFrames);

            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Moving),
                "The sprint ramp should establish a real moving baseline before the jump request.");

            _movement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();
            _movement.SetJumpInputForTest(false);

            int launchFrames = Mathf.CeilToInt(WindUpDuration / Time.fixedDeltaTime) + 1;
            yield return WaitForPhysicsFrames(launchFrames);

            bool wasAirborne = _characterState.CurrentState == CharacterStateType.Airborne;
            for (int frame = 0; frame < AirborneBudgetFrames && !wasAirborne; frame++)
            {
                yield return new WaitForFixedUpdate();
                wasAirborne = _characterState.CurrentState == CharacterStateType.Airborne;
            }

            Assert.That(wasAirborne, Is.True,
                "A committed sprint jump should enter Airborne shortly after launch even if a planted foot sensor lags for a few frames.");
        }

        [UnityTest]
        public IEnumerator LandingAbsorption_LowersHipsBriefly_AfterAirborneToStanding()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();

            // Use a strong jump force and clear the ground-state override so that
            // natural GroundSensor detection drives the Airborne transition.
            SetPrivateField(_movement, "_jumpForce", 40f);
            SetPrivateField(_balance, "_overrideGroundState", false);

            float standingY = _hipsBody.position.y;

            // Jump and wait through wind-up + impulse.
            _movement.SetJumpInputForTest(true);
            int windUpFrames = Mathf.CeilToInt(WindUpDuration / Time.fixedDeltaTime) + 2;
            yield return WaitForPhysicsFrames(windUpFrames);

            // Wait for the character to go airborne and land (up to ~5 s budget).
            float budget = 5f;
            float elapsed = 0f;
            bool wasAirborne = false;
            bool landed = false;
            while (elapsed < budget)
            {
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;

                CharacterStateType state = _characterState.CurrentState;
                if (state == CharacterStateType.Airborne)
                {
                    wasAirborne = true;
                }
                if (wasAirborne && (state == CharacterStateType.Standing || state == CharacterStateType.Moving))
                {
                    landed = true;
                    break;
                }
                // Abort if fallen.
                if (state == CharacterStateType.Fallen)
                {
                    break;
                }
            }

            Assert.That(wasAirborne, Is.True, "Character should have entered Airborne.");
            Assert.That(landed, Is.True, "Character should have landed (Standing or Moving).");

            // Immediately after landing, sample hips height for the absorption window.
            // The absorption hold phase is 0.15 s = 15 physics frames at 0.01 s dt.
            float lowestY = _hipsBody.position.y;
            int absorptionSampleFrames = 20;
            for (int i = 0; i < absorptionSampleFrames; i++)
            {
                yield return new WaitForFixedUpdate();
                if (_hipsBody.position.y < lowestY)
                {
                    lowestY = _hipsBody.position.y;
                }
            }

            Assert.That(lowestY, Is.LessThan(standingY + 0.005f),
                "Hips should dip below standing height during landing absorption (tolerance 5mm for physics noise).");

            // Wait for blend-out to finish and verify recovery toward standing.
            yield return WaitForPhysicsFrames(60);
            Assert.That(_hipsBody.position.y, Is.GreaterThan(standingY - 0.05f),
                "Hips should recover close to standing height after absorption blend-out.");
        }

        [UnityTest]
        public IEnumerator JumpLifecycle_PhaseSequence_NoneToWindUpToLaunchToNone()
        {
            yield return WaitForPhysicsFrames(SettleFrames);
            yield return PrepareStandingBaseline();
            SetPrivateField(_movement, "_jumpForce", 15f);

            Assert.That(_movement.CurrentJumpPhase, Is.EqualTo(JumpPhase.None),
                "Should start in None phase.");

            _movement.SetJumpInputForTest(true);
            yield return new WaitForFixedUpdate();

            Assert.That(_movement.CurrentJumpPhase, Is.EqualTo(JumpPhase.WindUp),
                "Should transition to WindUp after jump input.");

            // Tick through wind-up.
            int windUpFrames = Mathf.CeilToInt(WindUpDuration / Time.fixedDeltaTime);
            yield return WaitForPhysicsFrames(windUpFrames);

            Assert.That(_movement.CurrentJumpPhase, Is.EqualTo(JumpPhase.Launch),
                "Should transition to Launch after wind-up completes.");

            // Tick through launch. Default _jumpLaunchDuration is 0.1 s = 10 frames.
            int launchFrames = Mathf.CeilToInt(0.1f / Time.fixedDeltaTime) + 1;
            yield return WaitForPhysicsFrames(launchFrames);

            Assert.That(_movement.CurrentJumpPhase, Is.EqualTo(JumpPhase.None),
                "Should return to None after launch timer expires.");
        }

        private IEnumerator PrepareStandingBaseline()
        {
            _movement.SetMoveInputForTest(Vector2.zero);
            _balance.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _characterState.SetStateForTest(CharacterStateType.Standing);
            _hipsBody.linearVelocity = Vector3.zero;
            _hipsBody.angularVelocity = Vector3.zero;
            yield return new WaitForFixedUpdate();
        }

        private static IEnumerator WaitForPhysicsFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private static void SetPrivateField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Could not find private field '{fieldName}' on {instance.GetType().Name}.");
            field.SetValue(instance, value);
        }
    }
}