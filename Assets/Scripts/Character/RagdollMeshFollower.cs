using System.Collections.Generic;
using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Creates proxy transforms that match each model bone's initial world pose,
    /// parents them under the corresponding ragdoll segment, then swaps them into
    /// the SkinnedMeshRenderer's bones array. The original bindposes stay untouched
    /// because each proxy starts at exactly the same world transform as the bone it
    /// replaces. When the ragdoll moves, the proxies (as children) move with it,
    /// and Unity's built-in skinning deforms the mesh automatically.
    /// Attach to the Hips (root) GameObject of the ragdoll hierarchy.
    /// Lifecycle: Awake only (one-time setup).
    /// </summary>
    public class RagdollMeshFollower : MonoBehaviour
    {
        // ─── Serialised Fields ──────────────────────────────────────────────

        [SerializeField]
        [Tooltip("Root transform of the skinned model (the instantiated FBX child). " +
                 "If null, Awake searches children for an Animator or SkinnedMeshRenderer.")]
        private Transform _modelRoot;

        [SerializeField]
        [Tooltip("Log bone mapping results to the console on startup.")]
        private bool _debugMapping;

        // ─── Bone Mapping Table ─────────────────────────────────────────────
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

        // ─── Unity Lifecycle ────────────────────────────────────────────────

        private void Awake()
        {
            if (_modelRoot == null)
                _modelRoot = FindModelRoot();

            if (_modelRoot == null)
            {
                Debug.LogError($"[RagdollMeshFollower] '{name}': no skinned model found in children. " +
                               "Assign _modelRoot manually or ensure an FBX child exists.", this);
                enabled = false;
                return;
            }

            SkinnedMeshRenderer smr = _modelRoot.GetComponentInChildren<SkinnedMeshRenderer>();
            if (smr == null)
            {
                Debug.LogError($"[RagdollMeshFollower] '{name}': no SkinnedMeshRenderer found.", this);
                enabled = false;
                return;
            }

            RebindBones(smr);
        }

        // ─── Core ───────────────────────────────────────────────────────────

        /// <summary>
        /// For each bone in the SkinnedMeshRenderer, creates a proxy GameObject at the
        /// bone's exact world position/rotation/scale, parents it under the matching
        /// ragdoll segment, then swaps the proxy into the bones array.
        /// The original mesh and bindposes are NOT modified.
        /// </summary>
        private void RebindBones(SkinnedMeshRenderer smr)
        {
            var modelToRagdoll = new Dictionary<string, string>(BoneMap.Length);
            foreach (var (ragdoll, mixamo) in BoneMap)
                modelToRagdoll[mixamo] = ragdoll;

            var ragdollCache = new Dictionary<string, Transform>(16);
            Transform hipsFallback = FindSegment("Hips");

            Transform[] bones = smr.bones;
            int replaced = 0;

            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null) continue;

                Transform ragdollSegment = FindRagdollForBone(
                    bones[i], modelToRagdoll, ragdollCache);

                if (ragdollSegment == null)
                    ragdollSegment = hipsFallback;

                // Create a proxy at the model bone's exact world transform.
                // Parenting under the ragdoll segment makes it follow physics.
                // The original bindposes work because the proxy's initial
                // localToWorldMatrix matches the original bone's.
                var proxy = new GameObject(bones[i].name);
                Transform pt = proxy.transform;
                pt.SetPositionAndRotation(bones[i].position, bones[i].rotation);
                pt.localScale = bones[i].lossyScale;
                pt.SetParent(ragdollSegment, worldPositionStays: true);

                if (_debugMapping)
                    Debug.Log($"[RagdollMeshFollower] bone[{i}] {bones[i].name} → {ragdollSegment.name}", this);

                bones[i] = pt;
                replaced++;
            }

            smr.bones = bones;
            smr.updateWhenOffscreen = true;

            Debug.Log($"[RagdollMeshFollower] Created {replaced} proxy bones under ragdoll segments.");
        }

        // ─── Private Helpers ────────────────────────────────────────────────

        /// <summary>
        /// Finds the ragdoll segment for a model bone by name, walking up the model
        /// hierarchy to find the nearest mapped ancestor for unmapped leaf bones.
        /// </summary>
        private Transform FindRagdollForBone(
            Transform modelBone,
            Dictionary<string, string> modelToRagdoll,
            Dictionary<string, Transform> ragdollCache)
        {
            Transform current = modelBone;
            while (current != null && current != _modelRoot.parent)
            {
                if (modelToRagdoll.TryGetValue(current.name, out string ragdollName))
                {
                    if (!ragdollCache.TryGetValue(ragdollName, out Transform segment))
                    {
                        segment = FindSegment(ragdollName);
                        ragdollCache[ragdollName] = segment;
                    }
                    return segment;
                }
                current = current.parent;
            }
            return null;
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
                {
                    return child;
                }
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
