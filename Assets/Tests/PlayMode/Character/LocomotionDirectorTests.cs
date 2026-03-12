using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// PlayMode coverage for the pass-through LocomotionDirector seam introduced by
    /// Chapter 1 task C1.3 of the unified locomotion roadmap.
    /// </summary>
    public class LocomotionDirectorTests
    {
        private const int SettleFrames = 100;

        private static readonly Vector3 TestOrigin = new Vector3(2500f, 0f, 2500f);

        private PlayerPrefabTestRig _rig;
        private LocomotionDirector _director;

        [SetUp]
        public void SetUp()
        {
            _rig = PlayerPrefabTestRig.Create(new PlayerPrefabTestRig.Options
            {
                TestOrigin = TestOrigin,
                GroundName = "LocomotionDirectorTests_Ground",
            });

            _director = _rig.Instance.GetComponent<LocomotionDirector>();
        }

        [TearDown]
        public void TearDown()
        {
            _rig?.Dispose();
            _rig = null;
            _director = null;
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WithForwardInput_CapturesDesiredInputObservationAndPassThroughSupportCommand()
        {
            // Arrange
            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for roadmap task C1.3.");

            yield return _rig.WarmUp(SettleFrames);

            // Act
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            object desiredInput = GetPropertyValue<object>(_director, "CurrentDesiredInput");
            object observation = GetPropertyValue<object>(_director, "CurrentObservation");
            object supportCommand = GetPropertyValue<object>(_director, "CurrentBodySupportCommand");

            Vector2 moveInput = GetPropertyValue<Vector2>(desiredInput, "MoveInput");
            bool hasMoveIntent = GetPropertyValue<bool>(desiredInput, "HasMoveIntent");
            Vector3 desiredFacing = GetPropertyValue<Vector3>(desiredInput, "FacingDirection");
            CharacterStateType characterState = GetPropertyValue<CharacterStateType>(observation, "CharacterState");
            bool isGrounded = GetPropertyValue<bool>(observation, "IsGrounded");
            Vector3 supportFacing = GetPropertyValue<Vector3>(supportCommand, "FacingDirection");

            // Assert
            Assert.That(_director.HasCommandFrame, Is.True,
                "LocomotionDirector should publish a command frame after FixedUpdate.");
            AssertVector2Equal(moveInput, Vector2.up, "DesiredInput.MoveInput");
            Assert.That(hasMoveIntent, Is.True, "DesiredInput should report forward move intent.");
            Assert.That(characterState, Is.EqualTo(_rig.CharacterState.CurrentState),
                "LocomotionDirector should snapshot the authoritative CharacterState value.");
            Assert.That(isGrounded, Is.EqualTo(_rig.BalanceController.IsGrounded),
                "LocomotionDirector should snapshot the grounded observation from BalanceController.");
            Assert.That(Vector3.Dot(desiredFacing, supportFacing), Is.GreaterThan(0.999f),
                "Pass-through body support should preserve the desired facing direction.");
            Assert.That(_director.IsPassThroughMode, Is.True,
                "LocomotionDirector should remain in pass-through mode for roadmap task C1.3.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenLegacyGaitIsAdvancing_EmitsCycleCommandsWithMirroredPhaseOffset()
        {
            // Arrange
            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for roadmap task C1.3.");

            yield return _rig.WarmUp(SettleFrames);

            // Act
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            for (int frame = 0; frame < 30; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            object leftLegCommand = GetPropertyValue<object>(_director, "LeftLegCommand");
            object rightLegCommand = GetPropertyValue<object>(_director, "RightLegCommand");

            string leftMode = GetPropertyValue<object>(leftLegCommand, "Mode").ToString();
            string rightMode = GetPropertyValue<object>(rightLegCommand, "Mode").ToString();
            float leftPhase = GetPropertyValue<float>(leftLegCommand, "CyclePhase");
            float rightPhase = GetPropertyValue<float>(rightLegCommand, "CyclePhase");
            float leftBlendWeight = GetPropertyValue<float>(leftLegCommand, "BlendWeight");
            float rightBlendWeight = GetPropertyValue<float>(rightLegCommand, "BlendWeight");

            // Assert
            Assert.That(leftMode, Is.EqualTo("Cycle"),
                "Pass-through director should mirror legacy gait as Cycle commands while moving.");
            Assert.That(rightMode, Is.EqualTo("Cycle"),
                "Pass-through director should mirror legacy gait as Cycle commands while moving.");
            Assert.That(leftBlendWeight, Is.EqualTo(_rig.LegAnimator.SmoothedInputMag).Within(0.0001f),
                "Left leg command should mirror the legacy gait blend weight.");
            Assert.That(rightBlendWeight, Is.EqualTo(_rig.LegAnimator.SmoothedInputMag).Within(0.0001f),
                "Right leg command should mirror the legacy gait blend weight.");
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(leftPhase * Mathf.Rad2Deg, _rig.LegAnimator.Phase * Mathf.Rad2Deg)),
                Is.LessThan(0.01f),
                "Left leg command should mirror the legacy gait phase.");

            float expectedRightPhase = Mathf.Repeat(_rig.LegAnimator.Phase + Mathf.PI, Mathf.PI * 2f);
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(rightPhase * Mathf.Rad2Deg, expectedRightPhase * Mathf.Rad2Deg)),
                Is.LessThan(0.01f),
                "Right leg command should mirror the legacy gait phase with a half-cycle offset.");
        }

        [UnityTest]
        public IEnumerator FixedUpdate_WhenCollapseWatchdogConfirmsButStateRemainsMoving_StillEmitsCycleCommands()
        {
            // Arrange
            Assert.That(_director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for roadmap task C1.5.");

            yield return _rig.WarmUp(SettleFrames);

            LocomotionCollapseDetector collapseDetector = _rig.Instance.GetComponent<LocomotionCollapseDetector>();
            Assert.That(collapseDetector, Is.Not.Null,
                "PlayerRagdoll prefab must provide LocomotionCollapseDetector for watchdog-boundary coverage.");

            _rig.BalanceController.SetGroundStateForTest(isGrounded: true, isFallen: false);
            _rig.CharacterState.SetStateForTest(CharacterStateType.Moving);
            _rig.CharacterState.enabled = false;
            collapseDetector.enabled = false;
            LocomotionCollapseDetectorTestSeams.SetCollapseConfirmed(collapseDetector, true);

            // Act
            _rig.PlayerMovement.SetMoveInputForTest(Vector2.up);
            for (int frame = 0; frame < 30; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            object leftLegCommand = GetPropertyValue<object>(_director, "LeftLegCommand");
            object rightLegCommand = GetPropertyValue<object>(_director, "RightLegCommand");

            string leftMode = GetPropertyValue<object>(leftLegCommand, "Mode").ToString();
            string rightMode = GetPropertyValue<object>(rightLegCommand, "Mode").ToString();

            // Assert
            Assert.That(leftMode, Is.EqualTo("Cycle"),
                "Raw collapse confirmation should not disable left-leg cycle commands while CharacterState still labels the character Moving.");
            Assert.That(rightMode, Is.EqualTo("Cycle"),
                "Raw collapse confirmation should not disable right-leg cycle commands while CharacterState still labels the character Moving.");
        }

        private static T GetPropertyValue<T>(object instance, string propertyName)
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            PropertyInfo property = instance.GetType().GetProperty(propertyName, flags);

            Assert.That(property, Is.Not.Null,
                $"Expected type '{instance.GetType().FullName}' to expose property '{propertyName}'.");

            object value = property.GetValue(instance);
            Assert.That(value, Is.Not.Null, $"Property '{propertyName}' should not be null.");
            return (T)value;
        }

        private static void AssertVector2Equal(Vector2 actual, Vector2 expected, string propertyName)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f), $"{propertyName}.x mismatch.");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f), $"{propertyName}.y mismatch.");
        }
    }
}