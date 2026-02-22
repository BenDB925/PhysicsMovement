using System.Collections.Generic;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    public class RagdollMeshFollower : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Root transform of the skinned model (the instantiated FBX child). " +
                 "If null, Awake searches children for an Animator or SkinnedMeshRenderer.")]
        private Transform _modelRoot;

        // ragdoll segment name → Mixamo bone name
        private static readonly (string ragdoll, string mixamo)[] BoneMap = new[]
        {
            ("Hips",        "mixamorig:Hips"),
            ("Hips",        "mixamorig:Spine"),
            ("Torso",       "mixamorig:Spine1"),
            ("Torso",       "mixamorig:Spine2"),
            ("Torso",       "mixamorig:Neck"),
            ("Head",        "mixamorig:Head"),
            ("Torso",       "mixamorig:LeftShoulder"),
            ("Torso",       "mixamorig:RightShoulder"),
            ("UpperLeg_L",  "mixamorig:LeftUpLeg"),
            ("LowerLeg_L",  "mixamorig:LeftLeg"),
            ("Foot_L",      "mixamorig:LeftFoot"),
            ("UpperLeg_R",  "mixamorig:RightUpLeg"),
            ("LowerLeg_R",  "mixamorig:RightLeg"),
            ("Foot_R",      "mixamorig:RightFoot"),
            ("UpperArm_L",  "mixamorig:LeftArm"),
            ("LowerArm_L",  "mixamorig:LeftForeArm"),
            ("Hand_L",      "mixamorig:LeftHand"),
            ("UpperArm_R",  "mixamorig:RightArm"),
            ("LowerArm_R",  "mixamorig:RightForeArm"),
            ("Hand_R",      "mixamorig:RightHand"),
        };

        private Transform[] _ragdollSegments;
        private Transform[] _modelBones;
        private Quaternion[] _rotationOffsets;
        private int _mappedCount;
        private Transform _modelRoot_resolved;
        private Transform _hipsModelBone;

        private void Awake()
        {
            if (_modelRoot == null)
                _modelRoot = FindModelRoot();

            if (_modelRoot == null)
            {
                Debug.LogError($"[RagdollMeshFollower] no skinned model found.", this);
                enabled = false;
                return;
            }

            _modelRoot_resolved = _modelRoot;

            // Kill the Animator entirely so it can't interfere with bone transforms.
            var animator = _modelRoot.GetComponent<Animator>();
            if (animator != null)
                Destroy(animator);

            // Use the SkinnedMeshRenderer's own bones array — these are the exact
            // transforms Unity reads for mesh deformation. Finding bones by name
            // from the hierarchy could return different Transform instances.
            SkinnedMeshRenderer smr = _modelRoot.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null)
            {
                Debug.LogError($"[RagdollMeshFollower] no SkinnedMeshRenderer found.", this);
                enabled = false;
                return;
            }

            Transform[] smrBones = smr.bones;

            // Index smr bones by name for fast lookup.
            var bonesByName = new Dictionary<string, Transform>(smrBones.Length);
            for (int i = 0; i < smrBones.Length; i++)
            {
                if (smrBones[i] != null)
                    bonesByName[smrBones[i].name] = smrBones[i];
            }

            // Build the mapping arrays.
            var ragdollList = new List<Transform>(BoneMap.Length);
            var modelList = new List<Transform>(BoneMap.Length);

            foreach (var (ragdollName, mixamoName) in BoneMap)
            {
                Transform ragdollSegment = FindSegment(ragdollName);
                if (ragdollSegment == null)
                {
                    Debug.LogWarning($"[RagdollMeshFollower] ragdoll '{ragdollName}' not found.", this);
                    continue;
                }

                if (!bonesByName.TryGetValue(mixamoName, out Transform modelBone))
                {
                    Debug.LogWarning($"[RagdollMeshFollower] smr bone '{mixamoName}' not found.", this);
                    continue;
                }

                ragdollList.Add(ragdollSegment);
                modelList.Add(modelBone);
            }

            _ragdollSegments = ragdollList.ToArray();
            _modelBones = modelList.ToArray();
            _mappedCount = _ragdollSegments.Length;

            // Capture rest-pose rotation offsets.
            _rotationOffsets = new Quaternion[_mappedCount];
            for (int i = 0; i < _mappedCount; i++)
            {
                _rotationOffsets[i] = Quaternion.Inverse(_ragdollSegments[i].rotation)
                                      * _modelBones[i].rotation;
            }

            // Remember the Hips model bone for root translation.
            if (_mappedCount > 0)
                _hipsModelBone = _modelBones[0];

            Debug.Log($"[RagdollMeshFollower] mapped {_mappedCount}/{BoneMap.Length} bones. " +
                      $"SMR has {smrBones.Length} bones total.");

            // Log each mapping so we can verify arms are included.
            for (int i = 0; i < _mappedCount; i++)
            {
                Debug.Log($"  [{i}] {_ragdollSegments[i].name} → {_modelBones[i].name} " +
                          $"(offset {_rotationOffsets[i].eulerAngles})");
            }
        }

        private void LateUpdate()
        {
            for (int i = 0; i < _mappedCount; i++)
            {
                _modelBones[i].rotation = _ragdollSegments[i].rotation * _rotationOffsets[i];
            }

            if (_hipsModelBone != null)
            {
                Vector3 error = _ragdollSegments[0].position - _hipsModelBone.position;
                _modelRoot_resolved.position += error;
            }
        }

        private Transform FindModelRoot()
        {
            for (int i = 0; i < transform.childCount; i++)
            {
                Transform child = transform.GetChild(i);
                if (child.GetComponent<Rigidbody>() != null)
                    continue;
                if (child.GetComponent<Animator>() != null ||
                    child.GetComponentInChildren<SkinnedMeshRenderer>() != null)
                    return child;
            }
            return null;
        }

        private Transform FindSegment(string segmentName)
        {
            if (segmentName == "Hips")
                return transform;

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
