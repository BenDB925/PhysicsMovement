using UnityEditor;
using UnityEngine;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Editor
{
    /// <summary>
    /// Editor utility that creates a skinned ragdoll prefab by layering a KayKit Skeleton Minion FBX
    /// model on top of the existing physics-driven <c>PlayerRagdoll.prefab</c>.
    /// Strips primitive mesh visuals, parents the skinned model under Hips, disables
    /// its Animator, and adds <see cref="RagdollMeshFollower"/> to sync bones at runtime.
    /// Access via: Tools → PhysicsDrivenMovement → Build Skinned Player Ragdoll.
    /// Collaborators: <see cref="RagdollBuilder"/>, <see cref="RagdollMeshFollower"/>.
    /// </summary>
    public static class SkinnedRagdollBuilder
    {
        private const string SourcePrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";
        private const string ModelPath        = "Assets/Models/KayKit_Skeletons/Skeleton_Minion.fbx";
        private const string OutputPrefabPath = "Assets/Prefabs/PlayerRagdoll_Skinned.prefab";
        private const string CharacterMatPath = "Assets/Materials/SkeletonMinion.mat";
        private const string DiffusePath      = "Assets/Models/KayKit_Skeletons/skeleton_texture.png";

        [MenuItem("Tools/PhysicsDrivenMovement/Build Skinned Player Ragdoll")]
        public static void BuildSkinnedRagdollPrefab()
        {
            // STEP 1: Load the source prefab and model assets.
            GameObject sourcePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SourcePrefabPath);
            if (sourcePrefab == null)
            {
                Debug.LogError($"[SkinnedRagdollBuilder] Source prefab not found at '{SourcePrefabPath}'. " +
                               "Run 'Tools → PhysicsDrivenMovement → Build Player Ragdoll' first.");
                return;
            }

            GameObject modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
            {
                Debug.LogError($"[SkinnedRagdollBuilder] Character model not found at '{ModelPath}'.");
                return;
            }

            // STEP 2: Instantiate the physics prefab.
            GameObject rootGO = (GameObject)PrefabUtility.InstantiatePrefab(sourcePrefab);
            PrefabUtility.UnpackPrefabInstance(rootGO, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);

            // STEP 3: Strip all child GameObjects named "Visual" (primitive mesh placeholders).
            StripVisuals(rootGO.transform);

            // STEP 4: Instantiate the model as a child of the Hips root.
            GameObject modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset);
            modelInstance.transform.SetParent(rootGO.transform, worldPositionStays: false);
            modelInstance.transform.localPosition = Vector3.zero;
            modelInstance.transform.localRotation = Quaternion.identity;

            // STEP 5: Disable the Animator so we drive bones manually via RagdollMeshFollower.
            Animator animator = modelInstance.GetComponent<Animator>();
            if (animator != null)
            {
                animator.enabled = false;
            }

            // STEP 6: Create (or load) the character material and apply to all skinned meshes.
            Material charMat = CreateOrLoadCharacterMaterial();
            if (charMat != null)
            {
                foreach (SkinnedMeshRenderer smr in modelInstance.GetComponentsInChildren<SkinnedMeshRenderer>())
                {
                    Material[] mats = smr.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++)
                        mats[i] = charMat;
                    smr.sharedMaterials = mats;
                }
            }

            // STEP 7: Add RagdollMeshFollower to the Hips root.
            if (rootGO.GetComponent<RagdollMeshFollower>() == null)
            {
                RagdollMeshFollower follower = rootGO.AddComponent<RagdollMeshFollower>();

                // Wire the model root via SerializedObject so it serialises into the prefab.
                using var so = new SerializedObject(follower);
                so.FindProperty("_modelRoot").objectReferenceValue = modelInstance.transform;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // STEP 7: Ensure output directory exists and save prefab.
            string directory = System.IO.Path.GetDirectoryName(OutputPrefabPath);
            if (!AssetDatabase.IsValidFolder(directory))
            {
                AssetDatabase.CreateFolder(
                    System.IO.Path.GetDirectoryName(directory),
                    System.IO.Path.GetFileName(directory));
            }

            PrefabUtility.SaveAsPrefabAsset(rootGO, OutputPrefabPath);
            Object.DestroyImmediate(rootGO);

            AssetDatabase.Refresh();
            Debug.Log($"[SkinnedRagdollBuilder] Skinned ragdoll prefab saved to '{OutputPrefabPath}'.");
        }

        /// <summary>
        /// Creates or loads a URP Lit material using the KayKit skeleton texture.
        /// </summary>
        private static Material CreateOrLoadCharacterMaterial()
        {
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(CharacterMatPath);
            if (mat != null)
                return mat;

            Texture2D diffuse = AssetDatabase.LoadAssetAtPath<Texture2D>(DiffusePath);
            if (diffuse == null)
            {
                Debug.LogWarning("[SkinnedRagdollBuilder] Diffuse texture not found — skipping material creation.");
                return null;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                         ?? Shader.Find("Standard");

            mat = new Material(shader) { name = "SkeletonMinion" };
            mat.SetTexture("_BaseMap", diffuse);
            mat.mainTexture = diffuse;

            string directory = System.IO.Path.GetDirectoryName(CharacterMatPath);
            if (!AssetDatabase.IsValidFolder(directory))
            {
                AssetDatabase.CreateFolder(
                    System.IO.Path.GetDirectoryName(directory),
                    System.IO.Path.GetFileName(directory));
            }

            AssetDatabase.CreateAsset(mat, CharacterMatPath);
            Debug.Log($"[SkinnedRagdollBuilder] Skeleton material created at '{CharacterMatPath}'.");
            return mat;
        }

        /// <summary>
        /// Recursively destroys all child GameObjects named "Visual" under <paramref name="root"/>.
        /// These are the primitive mesh placeholders created by <see cref="RagdollBuilder"/>.
        /// </summary>
        private static void StripVisuals(Transform root)
        {
            // Iterate backwards so removal doesn't skip siblings.
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                Transform child = root.GetChild(i);

                if (child.gameObject.name == "Visual")
                {
                    Object.DestroyImmediate(child.gameObject);
                }
                else
                {
                    StripVisuals(child);
                }
            }
        }
    }
}
