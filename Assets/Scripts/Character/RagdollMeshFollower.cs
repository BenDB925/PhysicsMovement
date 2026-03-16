using System.Collections.Generic;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Copies world-space transforms from ragdoll physics segments to the corresponding
    /// bones of a skinned mesh model each LateUpdate. This keeps the SkinnedMeshRenderer
    /// deforming to match the ragdoll without reparenting bones or touching the
    /// SkinnedMeshRenderer.bones array.
    /// Attach to the Hips (root) GameObject of the ragdoll hierarchy. The skinned model
    /// should be an immediate child (instantiated by SkinnedRagdollBuilder or placed manually).
    /// Lifecycle: Awake (discover bones), LateUpdate (copy transforms).
    /// Collaborators: <see cref="RagdollSetup"/>, SkinnedRagdollBuilder (editor).
    /// </summary>
    public class RagdollMeshFollower : MonoBehaviour
    {
        // ─── Serialised Fields ──────────────────────────────────────────────

        [SerializeField]
        [Tooltip("Root transform of the skinned model (the instantiated FBX child). " +
                 "If null, Awake searches children for an Animator or SkinnedMeshRenderer.")]
        private Transform _modelRoot;

        [SerializeField]
        [Tooltip("Vertical offset applied after hips alignment. Negative moves the mesh down.")]
        private float _verticalOffset;

        [SerializeField]
        [Tooltip("Log bone mapping results to the console on startup.")]
        private bool _debugMapping;

        // ─── Private Fields ─────────────────────────────────────────────────

        /// <summary>Parallel arrays: ragdoll segment transform → model bone transform.</summary>
        private Transform[] _ragdollSegments;
        private Transform[] _modelBones;

        /// <summary>
        /// Per-bone rotation offset captured at startup:
        /// offset = Inverse(ragdollRestRotation) * modelRestRotation.
        /// Applied each frame so differing rest poses don't scramble the mesh.
        /// </summary>
        private Quaternion[] _rotationOffsets;

        private int _mappedCount;

        // ─── Bone Mapping Table ─────────────────────────────────────────────
        // ragdoll segment name → model bone name (KayKit Skeleton Minion)
        private static readonly (string ragdoll, string modelBone)[] BoneMap = new[]
        {
            ("Hips",        "hips"),
            ("Torso",       "spine"),
            ("Head",        "head"),
            ("UpperLeg_L",  "upperleg.l"),
            ("LowerLeg_L",  "lowerleg.l"),
            ("Foot_L",      "foot.l"),
            ("UpperLeg_R",  "upperleg.r"),
            ("LowerLeg_R",  "lowerleg.r"),
            ("Foot_R",      "foot.r"),
            ("UpperArm_L",  "upperarm.l"),
            ("LowerArm_L",  "lowerarm.l"),
            ("Hand_L",      "hand.l"),
            ("UpperArm_R",  "upperarm.r"),
            ("LowerArm_R",  "lowerarm.r"),
            ("Hand_R",      "hand.r"),
        };

        // ─── Unity Lifecycle ────────────────────────────────────────────────

        private void Awake()
        {
            // STEP 1: Resolve model root if not assigned.
            if (_modelRoot == null)
            {
                _modelRoot = FindModelRoot();
            }

            if (_modelRoot == null)
            {
                Debug.LogError($"[RagdollMeshFollower] '{name}': no skinned model found in children. " +
                               "Assign _modelRoot manually or ensure an FBX child exists.", this);
                enabled = false;
                return;
            }

            // STEP 2: Build the bone lookup from the model hierarchy.
            var modelBonesByName = new Dictionary<string, Transform>(64);
            CollectBones(_modelRoot, modelBonesByName);

            // STEP 3: Map ragdoll segments to model bones.
            var ragdollList = new List<Transform>(BoneMap.Length);
            var modelList   = new List<Transform>(BoneMap.Length);

            foreach ((string ragdollName, string boneName) in BoneMap)
            {
                Transform ragdollSegment = FindSegment(ragdollName);
                if (ragdollSegment == null)
                {
                    if (_debugMapping)
                        Debug.LogWarning($"[RagdollMeshFollower] Ragdoll segment '{ragdollName}' not found.", this);
                    continue;
                }

                if (!modelBonesByName.TryGetValue(boneName, out Transform modelBone))
                {
                    if (_debugMapping)
                        Debug.LogWarning($"[RagdollMeshFollower] Model bone '{boneName}' not found.", this);
                    continue;
                }

                ragdollList.Add(ragdollSegment);
                modelList.Add(modelBone);
            }

            _ragdollSegments = ragdollList.ToArray();
            _modelBones      = modelList.ToArray();
            _mappedCount     = _ragdollSegments.Length;

            // STEP 4: Capture rest-pose rotation offsets.
            // offset = Inverse(ragdollRest) * modelRest
            // At runtime: modelBone.rotation = ragdollSegment.rotation * offset
            // This accounts for the different rest orientations between the ragdoll
            // primitives and the model skeleton.
            _rotationOffsets = new Quaternion[_mappedCount];
            for (int i = 0; i < _mappedCount; i++)
            {
                _rotationOffsets[i] = Quaternion.Inverse(_ragdollSegments[i].rotation)
                                      * _modelBones[i].rotation;
            }

            if (_debugMapping)
            {
                Debug.Log($"[RagdollMeshFollower] '{name}': mapped {_mappedCount}/{BoneMap.Length} bones.", this);
            }
        }

        private void LateUpdate()
        {
            // Pass 1: Set rotations only — don't touch individual bone positions.
            // Setting world position on bones inside a scaled hierarchy (FBX 0.01
            // import scale) corrupts the skeleton chain for child bones.
            for (int i = 0; i < _mappedCount; i++)
            {
                _modelBones[i].rotation = _ragdollSegments[i].rotation * _rotationOffsets[i];
            }

            // Pass 2: Translate the entire model root so the model's Hips bone
            // lands exactly on the ragdoll Hips world position. This preserves
            // the skeleton's internal scale chain and bone lengths.
            if (_mappedCount > 0)
            {
                Vector3 hipsError = _ragdollSegments[0].position - _modelBones[0].position;
                _modelRoot.position += hipsError + new Vector3(0f, _verticalOffset, 0f);
            }
        }

        // ─── Private Helpers ────────────────────────────────────────────────

        /// <summary>
        /// Searches immediate children for a GameObject containing an Animator or
        /// SkinnedMeshRenderer, indicating it is the model root.
        /// </summary>
        private Transform FindModelRoot()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);

                // Skip ragdoll physics segments (they have Rigidbodies).
                if (child.GetComponent<Rigidbody>() != null)
                    continue;

                if (child.GetComponent<Animator>() != null ||
                    child.GetComponentInChildren<SkinnedMeshRenderer>() != null)
                {
                    return child;
                }
            }

            return null;
        }

        /// <summary>
        /// Recursively collects all transforms under <paramref name="root"/> into
        /// <paramref name="dict"/> keyed by GameObject name.
        /// </summary>
        private static void CollectBones(Transform root, Dictionary<string, Transform> dict)
        {
            dict[root.name] = root;
            for (int i = 0; i < root.childCount; i++)
            {
                CollectBones(root.GetChild(i), dict);
            }
        }

        /// <summary>
        /// Finds a ragdoll segment by name in this hierarchy. Searches children
        /// (not the model subtree) for a GameObject matching the name.
        /// </summary>
        private Transform FindSegment(string segmentName)
        {
            // This component lives on the Hips root. Match "Hips" even when the
            // GameObject has been renamed (e.g. the skinned prefab root is named
            // after the prefab, not the segment).
            if (segmentName == "Hips")
                return transform;

            // Search ragdoll children (skip the model subtree by checking for Rigidbody).
            Rigidbody[] bodies = GetComponentsInChildren<Rigidbody>(includeInactive: true);
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i].gameObject.name == segmentName)
                    return bodies[i].transform;
            }

            return null;
        }
    }
}
