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
    /// <summary>
    /// PlayMode get-up reliability tests using the real PlayerRagdoll prefab and the full
    /// runtime locomotion stack. Falls are triggered by external directional impulses and
    /// recovery is asserted through CharacterState and world-space posture outcomes.
    /// </summary>
    public class GetUpReliabilityTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";
        private const int SettleFrames = 220;
        private const int WaitForDestabilizationFrames = 500;

        // Raised from 1.5 → 2.5 to accommodate the surrender floor dwell added by
        // the comedic-knockdown overhaul. Large impulses push the character past the
        // 80° surrender threshold, adding up to 3.0 s of severity-based floor dwell
        // before GettingUp begins. The budget is now (getUpTimeout * 2.5) ≈ 7.5 s,
        // covering floor dwell + procedural stand-up + margin.
        // Floor dwell (1.5–3.0 s) + ProceduralStandUp phases (up to 2.6 s per
        // attempt) extend recovery time beyond the base _getUpTimeout. Scale
        // generously to allow a full retry cycle.
        private const float GetUpTimeoutScale = 4f;
        private const float DefaultGetUpTimeout = 3f;
        private const float LateralFallImpulseMagnitude = 800f;
        private const float LongitudinalFallImpulseMagnitude = 400f;
        private const float DestabilizationTiltThreshold = 15f;

        private static readonly Vector3 TestOrigin = new Vector3(700f, 0f, 700f);

        private GameObject _groundGO;
        private GameObject _instance;
        private Rigidbody _hipsRb;
        private Rigidbody _torsoRb;
        private BalanceController _balance;
        private CharacterState _characterState;
        private PlayerMovement _movement;
        private LegAnimator _legAnimator;
        private ArmAnimator _armAnimator;
        private RagdollSetup _ragdollSetup;

        private float _savedFixedDeltaTime;
        private int _savedSolverIterations;
        private int _savedSolverVelocityIterations;
        private bool[,] _savedLayerCollisionMatrix;

        [SetUp]
        public void SetUp()
        {
            _savedFixedDeltaTime = Time.fixedDeltaTime;
            _savedSolverIterations = Physics.defaultSolverIterations;
            _savedSolverVelocityIterations = Physics.defaultSolverVelocityIterations;
            _savedLayerCollisionMatrix = CaptureLayerCollisionMatrix();

            Time.fixedDeltaTime = 0.01f;
            Physics.defaultSolverIterations = 12;
            Physics.defaultSolverVelocityIterations = 4;
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance != null)
            {
                Object.Destroy(_instance);
            }

            if (_groundGO != null)
            {
                Object.Destroy(_groundGO);
            }

            Time.fixedDeltaTime = _savedFixedDeltaTime;
            Physics.defaultSolverIterations = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
            RestoreLayerCollisionMatrix(_savedLayerCollisionMatrix);
        }

        [UnityTest]
        public IEnumerator AfterLeftDirectionalImpulse_CharacterRecoversFromDestabilizationWithinTimeout()
        {
            yield return SpawnCharacterOnGround();
            yield return RunDirectionalRecoveryTest(Vector3.left, "Left");
        }

        [UnityTest]
        public IEnumerator AfterRightDirectionalImpulse_CharacterRecoversFromDestabilizationWithinTimeout()
        {
            yield return SpawnCharacterOnGround();
            yield return RunDirectionalRecoveryTest(Vector3.right, "Right");
        }

        [UnityTest]
        public IEnumerator AfterBackwardDirectionalImpulse_CharacterRecoversFromDestabilizationWithinTimeout()
        {
            yield return SpawnCharacterOnGround();
            yield return RunDirectionalRecoveryTest(Vector3.back, "Backward");
        }

        private IEnumerator RunDirectionalRecoveryTest(Vector3 direction, string directionLabel)
        {
            Assert.That(_characterState.CurrentState, Is.EqualTo(CharacterStateType.Standing).Or.EqualTo(CharacterStateType.Moving),
                $"[{directionLabel}] Precondition failed: expected the real character to start settled on the ground.");

            float impulseMagnitude = Mathf.Abs(direction.x) > Mathf.Abs(direction.z)
                ? LateralFallImpulseMagnitude
                : LongitudinalFallImpulseMagnitude;
            Vector3 impulse = direction.normalized * impulseMagnitude;
            Vector3 forcePoint = _torsoRb != null
                ? _torsoRb.worldCenterOfMass
                : _hipsRb.worldCenterOfMass + Vector3.up * 0.15f;

            _hipsRb.AddForceAtPosition(impulse, forcePoint, ForceMode.Impulse);

            int waitForDestabilization = 0;
            float maxTiltWhileRecovering = Vector3.Angle(_balance.transform.up, Vector3.up);
            bool enteredFallen = false;
            bool destabilized = false;

            while (!enteredFallen && !destabilized && waitForDestabilization < WaitForDestabilizationFrames)
            {
                yield return new WaitForFixedUpdate();
                waitForDestabilization++;

                float tilt = Vector3.Angle(_balance.transform.up, Vector3.up);
                maxTiltWhileRecovering = Mathf.Max(maxTiltWhileRecovering, tilt);
                enteredFallen = _characterState.CurrentState == CharacterStateType.Fallen;
                destabilized = tilt >= DestabilizationTiltThreshold;
            }

            Assert.That(enteredFallen || destabilized, Is.True,
                $"[{directionLabel}] Directional impulse should either enter Fallen or visibly destabilize the real prefab within {WaitForDestabilizationFrames} frames. Max tilt observed={maxTiltWhileRecovering:F1} degrees.");

            float getUpTimeout = GetGetUpTimeout();
            int budgetFrames = Mathf.CeilToInt(getUpTimeout * GetUpTimeoutScale * (1f / Time.fixedDeltaTime));

            bool recovered = false;
            int framesElapsed = 0;

            while (framesElapsed < budgetFrames)
            {
                yield return new WaitForFixedUpdate();
                framesElapsed++;

                CharacterStateType state = _characterState.CurrentState;
                float tilt = Vector3.Angle(_balance.transform.up, Vector3.up);
                float currentHipsHeight = _hipsRb.position.y;
                if ((state == CharacterStateType.Standing || state == CharacterStateType.Moving) &&
                    tilt < DestabilizationTiltThreshold &&
                    currentHipsHeight > 0.20f)
                {
                    recovered = true;
                    break;
                }
            }

            float finalTilt = Vector3.Angle(_balance.transform.up, Vector3.up);
            float hipsHeight = _hipsRb.position.y;

            Assert.That(recovered, Is.True,
                $"[{directionLabel}] Character did not recover to a stable Standing or Moving posture within {budgetFrames} frames ({budgetFrames * Time.fixedDeltaTime:F1} s). Current state={_characterState.CurrentState}, final tilt={finalTilt:F1} degrees, hipsY={hipsHeight:F2}.");
            Assert.That(_balance.IsFallen, Is.False,
                $"[{directionLabel}] BalanceController should clear fallen state after recovery.");
            Assert.That(finalTilt, Is.LessThan(DestabilizationTiltThreshold),
                $"[{directionLabel}] Recovered posture should be upright in world space. final tilt={finalTilt:F1} degrees.");
            Assert.That(hipsHeight, Is.GreaterThan(0.20f),
                $"[{directionLabel}] Recovered posture should lift the hips back off the ground. hipsY={hipsHeight:F2}.");
        }

        private IEnumerator SpawnCharacterOnGround()
        {
            BuildGroundPlane();

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null,
                $"PlayerRagdoll prefab was not found at '{PlayerRagdollPrefabPath}'.");

            _instance = Object.Instantiate(prefab, TestOrigin + new Vector3(0f, 0.5f, 0f), Quaternion.identity);
            Assert.That(_instance, Is.Not.Null, "Failed to instantiate PlayerRagdoll prefab.");

            _hipsRb = _instance.GetComponent<Rigidbody>();
            _balance = _instance.GetComponent<BalanceController>();
            _characterState = _instance.GetComponent<CharacterState>();
            _movement = _instance.GetComponent<PlayerMovement>();
            _legAnimator = _instance.GetComponent<LegAnimator>();
            _armAnimator = _instance.GetComponent<ArmAnimator>();
            _ragdollSetup = _instance.GetComponent<RagdollSetup>();
            _torsoRb = FindChildComponent<Rigidbody>(_instance.transform, "Torso");

            Assert.That(_hipsRb, Is.Not.Null, "PlayerRagdoll prefab is missing the hips Rigidbody.");
            Assert.That(_balance, Is.Not.Null, "PlayerRagdoll prefab is missing BalanceController.");
            Assert.That(_characterState, Is.Not.Null, "PlayerRagdoll prefab is missing CharacterState.");
            Assert.That(_movement, Is.Not.Null, "PlayerRagdoll prefab is missing PlayerMovement.");
            Assert.That(_legAnimator, Is.Not.Null, "PlayerRagdoll prefab is missing LegAnimator.");
            Assert.That(_armAnimator, Is.Not.Null, "PlayerRagdoll prefab is missing ArmAnimator.");
            Assert.That(_ragdollSetup, Is.Not.Null, "PlayerRagdoll prefab is missing RagdollSetup.");

            _movement.SetMoveInputForTest(Vector2.zero);
            yield return WaitPhysicsFrames(SettleFrames);
        }

        private void BuildGroundPlane()
        {
            _groundGO = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _groundGO.name = "GetUpReliability_Ground";
            _groundGO.transform.position = TestOrigin + new Vector3(0f, -0.5f, 0f);
            _groundGO.transform.localScale = new Vector3(400f, 1f, 400f);
            _groundGO.layer = GameSettings.LayerEnvironment;
        }

        private float GetGetUpTimeout()
        {
            FieldInfo field = typeof(CharacterState).GetField(
                "_getUpTimeout",
                BindingFlags.Instance | BindingFlags.NonPublic);
            if (field != null)
            {
                return (float)field.GetValue(_characterState);
            }

            return DefaultGetUpTimeout;
        }

        private static T FindChildComponent<T>(Transform root, string childName) where T : Component
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(includeInactive: true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == childName)
                {
                    return children[i].GetComponent<T>();
                }
            }

            return null;
        }

        private static IEnumerator WaitPhysicsFrames(int count)
        {
            for (int i = 0; i < count; i++)
            {
                yield return new WaitForFixedUpdate();
            }
        }

        private static bool[,] CaptureLayerCollisionMatrix()
        {
            bool[,] matrix = new bool[32, 32];
            for (int a = 0; a < 32; a++)
            {
                for (int b = 0; b < 32; b++)
                {
                    matrix[a, b] = Physics.GetIgnoreLayerCollision(a, b);
                }
            }

            return matrix;
        }

        private static void RestoreLayerCollisionMatrix(bool[,] matrix)
        {
            if (matrix == null || matrix.GetLength(0) != 32 || matrix.GetLength(1) != 32)
            {
                return;
            }

            for (int a = 0; a < 32; a++)
            {
                for (int b = 0; b < 32; b++)
                {
                    Physics.IgnoreLayerCollision(a, b, matrix[a, b]);
                }
            }
        }
    }
}
