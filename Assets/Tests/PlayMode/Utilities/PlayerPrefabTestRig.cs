using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using PhysicsDrivenMovement.Core;
using UnityEditor;
using UnityEngine;

namespace PhysicsDrivenMovement.Tests.PlayMode
{
    /// <summary>
    /// Shared PlayMode harness for prefab-backed character tests that need standard physics,
    /// a default ground plane, optional camera follow, warm-up helpers, and typed access to
    /// the production character components and limb transforms.
    /// </summary>
    public sealed class PlayerPrefabTestRig : IDisposable
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";

        private static readonly FieldInfo CameraTargetField = typeof(CameraFollow)
            .GetField("_target", BindingFlags.NonPublic | BindingFlags.Instance);

        private readonly bool[,] _savedLayerCollisionMatrix;
        private readonly float _savedFixedDeltaTime;
        private readonly int _savedSolverIterations;
        private readonly int _savedSolverVelocityIterations;
        private readonly List<Rigidbody> _fallbackBodies;
        private readonly GameObject _gameSettingsObject;

        private PlayerPrefabTestRig(Options options)
        {
            _savedFixedDeltaTime = Time.fixedDeltaTime;
            _savedSolverIterations = Physics.defaultSolverIterations;
            _savedSolverVelocityIterations = Physics.defaultSolverVelocityIterations;
            _savedLayerCollisionMatrix = CaptureLayerCollisionMatrix();

            _gameSettingsObject = new GameObject(options.GameSettingsName);
            _gameSettingsObject.AddComponent<GameSettings>();

            Physics.IgnoreLayerCollision(GameSettings.LayerPlayer1Parts, GameSettings.LayerEnvironment, false);
            Physics.IgnoreLayerCollision(GameSettings.LayerLowerLegParts, GameSettings.LayerEnvironment, true);

            Ground = CreateGround(options);
            Instance = SpawnPlayerPrefab(options);

            BalanceController = Instance.GetComponent<BalanceController>();
            PlayerMovement = Instance.GetComponent<PlayerMovement>();
            CharacterState = Instance.GetComponent<CharacterState>();
            LegAnimator = Instance.GetComponent<LegAnimator>();
            ArmAnimator = Instance.GetComponent<ArmAnimator>();
            RagdollSetup = Instance.GetComponent<RagdollSetup>();
            HipsBody = Instance.GetComponent<Rigidbody>();

            Assert.That(BalanceController, Is.Not.Null, "PlayerRagdoll prefab is missing BalanceController.");
            Assert.That(PlayerMovement, Is.Not.Null, "PlayerRagdoll prefab is missing PlayerMovement.");
            Assert.That(CharacterState, Is.Not.Null, "PlayerRagdoll prefab is missing CharacterState.");
            Assert.That(LegAnimator, Is.Not.Null, "PlayerRagdoll prefab is missing LegAnimator.");
            Assert.That(ArmAnimator, Is.Not.Null, "PlayerRagdoll prefab is missing ArmAnimator.");
            Assert.That(RagdollSetup, Is.Not.Null, "PlayerRagdoll prefab is missing RagdollSetup.");
            Assert.That(HipsBody, Is.Not.Null, "PlayerRagdoll prefab is missing the hips Rigidbody.");

            Hips = Instance.transform;
            Torso = FindRequiredChild("Torso");
            UpperLegL = FindRequiredChild("UpperLeg_L");
            UpperLegR = FindRequiredChild("UpperLeg_R");
            LowerLegL = FindRequiredChild("LowerLeg_L");
            LowerLegR = FindRequiredChild("LowerLeg_R");
            FootL = FindRequiredChild("Foot_L");
            FootR = FindRequiredChild("Foot_R");
            UpperArmL = FindRequiredChild("UpperArm_L");
            UpperArmR = FindRequiredChild("UpperArm_R");
            LowerArmL = FindRequiredChild("LowerArm_L");
            LowerArmR = FindRequiredChild("LowerArm_R");
            HandL = FindRequiredChild("Hand_L");
            HandR = FindRequiredChild("Hand_R");

            TorsoBody = Torso.GetComponent<Rigidbody>();
            Assert.That(TorsoBody, Is.Not.Null, "PlayerRagdoll prefab is missing the torso Rigidbody.");

            _fallbackBodies = new List<Rigidbody>(Instance.GetComponentsInChildren<Rigidbody>(includeInactive: true));

            PlayerMovement.SetMoveInputForTest(Vector2.zero);
            PlayerMovement.SetJumpInputForTest(false);

            if (options.CreateCamera)
            {
                CameraRoot = new GameObject(options.CameraName);
                CameraRoot.transform.position = options.TestOrigin + options.CameraOffset;
                Camera = CameraRoot.AddComponent<Camera>();
                CameraFollow = CameraRoot.AddComponent<CameraFollow>();
                CameraTargetField?.SetValue(CameraFollow, Hips);
            }
        }

        /// <summary>
        /// Creation inputs for a prefab-backed test world.
        /// </summary>
        public sealed class Options
        {
            public Vector3 TestOrigin { get; set; } = Vector3.zero;
            public Vector3 SpawnOffset { get; set; } = new Vector3(0f, 1.1f, 0f);
            public Quaternion SpawnRotation { get; set; } = Quaternion.identity;
            public Vector3 GroundOffset { get; set; } = new Vector3(0f, -0.5f, 0f);
            public Vector3 GroundScale { get; set; } = new Vector3(400f, 1f, 400f);
            public string GroundName { get; set; } = "TestGround";
            public string GameSettingsName { get; set; } = "TestGameSettings";
            public bool CreateCamera { get; set; }
            public string CameraName { get; set; } = "TestCamera";
            public Vector3 CameraOffset { get; set; } = new Vector3(0f, 2f, -6f);
        }

        public GameObject Ground { get; }
        public GameObject Instance { get; }
        public GameObject CameraRoot { get; }
        public Camera Camera { get; }
        public CameraFollow CameraFollow { get; }
        public BalanceController BalanceController { get; }
        public PlayerMovement PlayerMovement { get; }
        public CharacterState CharacterState { get; }
        public LegAnimator LegAnimator { get; }
        public ArmAnimator ArmAnimator { get; }
        public RagdollSetup RagdollSetup { get; }
        public Rigidbody HipsBody { get; }
        public Rigidbody TorsoBody { get; }
        public Transform Hips { get; }
        public Transform Torso { get; }
        public Transform UpperLegL { get; }
        public Transform UpperLegR { get; }
        public Transform LowerLegL { get; }
        public Transform LowerLegR { get; }
        public Transform FootL { get; }
        public Transform FootR { get; }
        public Transform UpperArmL { get; }
        public Transform UpperArmR { get; }
        public Transform LowerArmL { get; }
        public Transform LowerArmR { get; }
        public Transform HandL { get; }
        public Transform HandR { get; }

        public IReadOnlyList<Rigidbody> AllBodies
        {
            get
            {
                if (RagdollSetup != null && RagdollSetup.AllBodies != null && RagdollSetup.AllBodies.Count > 0)
                {
                    return RagdollSetup.AllBodies;
                }

                return _fallbackBodies;
            }
        }

        public static PlayerPrefabTestRig Create(Options options = null)
        {
            return new PlayerPrefabTestRig(options ?? new Options());
        }

        public IEnumerator WarmUp(int physicsFrames, int renderFrames = 0)
        {
            PlayerMovement.SetMoveInputForTest(Vector2.zero);
            PlayerMovement.SetJumpInputForTest(false);

            for (int frame = 0; frame < physicsFrames; frame++)
            {
                yield return new WaitForFixedUpdate();
            }

            for (int frame = 0; frame < renderFrames; frame++)
            {
                yield return null;
            }
        }

        public void Dispose()
        {
            if (Instance != null)
            {
                UnityEngine.Object.Destroy(Instance);
            }

            if (CameraRoot != null)
            {
                UnityEngine.Object.Destroy(CameraRoot);
            }

            if (Ground != null)
            {
                UnityEngine.Object.Destroy(Ground);
            }

            if (_gameSettingsObject != null)
            {
                UnityEngine.Object.Destroy(_gameSettingsObject);
            }

            Time.fixedDeltaTime = _savedFixedDeltaTime;
            Physics.defaultSolverIterations = _savedSolverIterations;
            Physics.defaultSolverVelocityIterations = _savedSolverVelocityIterations;
            RestoreLayerCollisionMatrix(_savedLayerCollisionMatrix);
        }

        private static GameObject CreateGround(Options options)
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = options.GroundName;
            ground.transform.position = options.TestOrigin + options.GroundOffset;
            ground.transform.localScale = options.GroundScale;
            ground.layer = GameSettings.LayerEnvironment;
            return ground;
        }

        private static GameObject SpawnPlayerPrefab(Options options)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            Assert.That(prefab, Is.Not.Null,
                $"PlayerRagdoll prefab must be loadable from '{PlayerRagdollPrefabPath}'.");

            GameObject instance = UnityEngine.Object.Instantiate(
                prefab,
                options.TestOrigin + options.SpawnOffset,
                options.SpawnRotation);

            Assert.That(instance, Is.Not.Null, "Failed to instantiate PlayerRagdoll prefab.");
            return instance;
        }

        private Transform FindRequiredChild(string childName)
        {
            Transform[] transforms = Instance.GetComponentsInChildren<Transform>(includeInactive: true);
            for (int index = 0; index < transforms.Length; index++)
            {
                if (transforms[index].name == childName)
                {
                    return transforms[index];
                }
            }

            Assert.Fail($"PlayerRagdoll prefab is missing child transform '{childName}'.");
            return null;
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