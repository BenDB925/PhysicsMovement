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
    public class FallPoseRecorderTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";
        private const int SettleFrames = 60;

        private static readonly Vector3 TestOrigin = new Vector3(2100f, 0f, 0f);

        private GameObject _ground;
        private GameObject _player;
        private PlayerMovement _playerMovement;
        private CharacterState _characterState;
        private FallPoseRecorder _recorder;
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
            _ground.name = "FallPoseRecorderTests_Ground";
            _ground.transform.position = TestOrigin + new Vector3(0f, -0.5f, 0f);
            _ground.transform.localScale = new Vector3(40f, 1f, 40f);
            _ground.layer = GameSettings.LayerEnvironment;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null, "PlayerRagdoll prefab must be loadable from Assets/Prefabs.");

            _player = UnityEngine.Object.Instantiate(prefab, TestOrigin + new Vector3(0f, 1.1f, 0f), Quaternion.identity);
            _playerMovement = _player.GetComponent<PlayerMovement>();
            _characterState = _player.GetComponent<CharacterState>();

            Assert.That(_playerMovement, Is.Not.Null, "PlayerRagdoll prefab must provide PlayerMovement.");
            Assert.That(_characterState, Is.Not.Null, "PlayerRagdoll prefab must provide CharacterState.");

            _recorder = _player.AddComponent<FallPoseRecorder>();
            Assert.That(_recorder, Is.Not.Null, "FallPoseRecorder must be addable to the full character setup.");
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
        public IEnumerator TriggerRollingCapture_WithFutureWindow_CompletesSession()
        {
            yield return WaitForPhysicsFrames(SettleFrames);

            ConfigureRecorderForTest(autoTriggerOnFallen: false);
            _playerMovement.SetMoveInputForTest(new Vector2(0.6f, 0.8f));
            _characterState.SetStateForTest(CharacterStateType.Moving);
            yield return WaitForPhysicsFrames(3);

            _recorder.TriggerRollingCapture("test-manual");
            yield return WaitForPhysicsFrames(3);

            Assert.That(_recorder.BufferedSampleCount, Is.GreaterThan(0));
            Assert.That(_recorder.CompletedSessionCount, Is.EqualTo(1));
            Assert.That(_recorder.IsCaptureActive, Is.False);
        }

        [UnityTest]
        public IEnumerator SetStateForTest_ToFallen_AutoTriggersCapture()
        {
            yield return WaitForPhysicsFrames(SettleFrames);

            ConfigureRecorderForTest(autoTriggerOnFallen: true);
            _playerMovement.SetMoveInputForTest(new Vector2(0.6f, 0.8f));
            _characterState.SetStateForTest(CharacterStateType.Moving);
            yield return new WaitForFixedUpdate();

            _characterState.SetStateForTest(CharacterStateType.Fallen);
            yield return null;

            Assert.That(_recorder.IsCaptureActive, Is.True);
            yield return WaitForPhysicsFrames(3);
            Assert.That(_recorder.CompletedSessionCount, Is.EqualTo(1));
        }

        private void ConfigureRecorderForTest(bool autoTriggerOnFallen)
        {
            SetPrivateField(_recorder, "_enableDiagnostics", true);
            SetPrivateField(_recorder, "_logToConsole", false);
            SetPrivateField(_recorder, "_logToFile", false);
            SetPrivateField(_recorder, "_clearLogOnStart", false);
            SetPrivateField(_recorder, "_sampleEveryFixedTicks", 1);
            SetPrivateField(_recorder, "_preTriggerSeconds", 0.02f);
            SetPrivateField(_recorder, "_postTriggerSeconds", 0.02f);
            SetPrivateField(_recorder, "_autoTriggerOnFallen", autoTriggerOnFallen);
            SetPrivateField(_recorder, "_recordContinuousSamples", false);
            SetPrivateField(_recorder, "_allowManualTrigger", false);
        }

        private static IEnumerator WaitForPhysicsFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Expected private field '{fieldName}' to exist.");
            field.SetValue(target, value);
        }
    }
}