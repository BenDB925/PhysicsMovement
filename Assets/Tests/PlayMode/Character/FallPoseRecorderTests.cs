using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    public class FallPoseRecorderTests
    {
        private GameObject _hips;
        private PlayerMovement _playerMovement;
        private CharacterState _characterState;
        private FallPoseRecorder _recorder;

        [TearDown]
        public void TearDown()
        {
            if (_hips != null)
            {
                Object.Destroy(_hips);
            }
        }

        [UnityTest]
        public IEnumerator TriggerRollingCapture_WithFutureWindow_CompletesSession()
        {
            // Arrange
            CreateRecorderRig();
            ConfigureRecorderForTest(autoTriggerOnFallen: false);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Act
            _recorder.TriggerRollingCapture("test-manual");
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // Assert
            Assert.That(_recorder.BufferedSampleCount, Is.GreaterThan(0),
                "Recorder must retain pre-trigger samples in its rolling buffer.");
            Assert.That(_recorder.CompletedSessionCount, Is.EqualTo(1),
                "Recorder must complete one session after the configured post-trigger window.");
            Assert.That(_recorder.IsCaptureActive, Is.False,
                "Recorder must stop writing future samples once the post-trigger window expires.");
        }

        [UnityTest]
        public IEnumerator SetStateForTest_ToFallen_AutoTriggersCapture()
        {
            // Arrange
            CreateRecorderRig();
            ConfigureRecorderForTest(autoTriggerOnFallen: true);
            yield return new WaitForFixedUpdate();

            // Act
            _characterState.SetStateForTest(CharacterStateType.Fallen);
            yield return null;

            // Assert
            Assert.That(_recorder.IsCaptureActive, Is.True,
                "Recorder must auto-trigger a rolling capture when CharacterState enters Fallen.");

            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.That(_recorder.CompletedSessionCount, Is.EqualTo(1),
                "Auto-triggered fall capture must complete after the configured post-trigger window.");
        }

        private void CreateRecorderRig()
        {
            _hips = new GameObject("Hips");
            _hips.AddComponent<Rigidbody>();
            _hips.AddComponent<BalanceController>();
            _playerMovement = _hips.AddComponent<PlayerMovement>();
            _characterState = _hips.AddComponent<CharacterState>();

            CreateSegment("LowerLeg_L", new Vector3(-0.2f, -0.6f, 0.3f));
            CreateSegment("LowerLeg_R", new Vector3(0.2f, -0.6f, 0.3f));
            CreateSegment("Foot_L", new Vector3(-0.2f, -1.1f, 0.4f));
            CreateSegment("Foot_R", new Vector3(0.2f, -1.1f, 0.4f));

            _recorder = _hips.AddComponent<FallPoseRecorder>();

            _playerMovement.SetMoveInputForTest(new Vector2(0.6f, 0.8f));
            _characterState.SetStateForTest(CharacterStateType.Moving);
        }

        private void ConfigureRecorderForTest(bool autoTriggerOnFallen)
        {
            SetPrivateField(_recorder, "_enableDiagnostics", true);
            SetPrivateField(_recorder, "_logToConsole", false);
            SetPrivateField(_recorder, "_logToFile", false);
            SetPrivateField(_recorder, "_sampleEveryFixedTicks", 1);
            SetPrivateField(_recorder, "_preTriggerSeconds", 0.02f);
            SetPrivateField(_recorder, "_postTriggerSeconds", 0.02f);
            SetPrivateField(_recorder, "_autoTriggerOnFallen", autoTriggerOnFallen);
            SetPrivateField(_recorder, "_recordContinuousSamples", false);
            SetPrivateField(_recorder, "_allowManualTrigger", false);
        }

        private void CreateSegment(string name, Vector3 localPosition)
        {
            GameObject segment = new GameObject(name);
            segment.transform.SetParent(_hips.transform, false);
            segment.transform.localPosition = localPosition;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected private field '{fieldName}' to exist.");
            field.SetValue(target, value);
        }
    }
}