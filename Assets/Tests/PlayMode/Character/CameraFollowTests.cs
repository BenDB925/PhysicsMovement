using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;
using UnityEngine.TestTools;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    public class CameraFollowTests
    {
        private const int SettleFrames = 80;
        private const int JumpFrames = 60;

        private static readonly Vector3 TestOrigin = new Vector3(900f, 0f, 900f);

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
                TestOrigin = TestOrigin,
                GroundName = "CameraFollow_Ground",
                CreateCamera = true,
                CameraName = "TestCamera",
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

        [UnityTest]
        public IEnumerator WithNullTarget_DoesNotThrow()
        {
            if (_rig.Instance != null)
            {
                Object.Destroy(_rig.Instance);
            }

            ForceTargetNull();

            for (int i = 0; i < 10; i++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(_rig.CameraFollow != null, Is.True,
                "CameraFollow should remain alive when its target is null.");
            Assert.That(_rig.CameraRoot != null, Is.True,
                "CameraFollow.LateUpdate must guard against a null target.");
        }

        [UnityTest]
        public IEnumerator DuringJump_CameraTracksUpwardMovement()
        {
            yield return _rig.WarmUp(SettleFrames, SettleFrames);

            float cameraYBefore = _rig.CameraRoot.transform.position.y;
            _rig.HipsBody.AddForce(Vector3.up * 800f, ForceMode.Impulse);

            yield return WaitPhysicsFrames(JumpFrames);
            yield return WaitRenderFrames(JumpFrames);

            float cameraYAfter = _rig.CameraRoot.transform.position.y;
            float cameraYDelta = cameraYAfter - cameraYBefore;

            Assert.That(cameraYDelta, Is.GreaterThanOrEqualTo(0.3f),
                $"Camera should move upward during a jump. Before={cameraYBefore:F3}, After={cameraYAfter:F3}, Delta={cameraYDelta:F3}.");
        }

        private void ForceTargetNull()
        {
            FieldInfo targetField = typeof(CameraFollow)
                .GetField("_target", BindingFlags.NonPublic | BindingFlags.Instance);

            if (targetField != null)
            {
                targetField.SetValue(_rig.CameraFollow, null);
            }
        }

        private static IEnumerator WaitPhysicsFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private static IEnumerator WaitRenderFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return null;
            }
        }
    }
}