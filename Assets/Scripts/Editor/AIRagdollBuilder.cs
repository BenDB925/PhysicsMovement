using UnityEditor;
using UnityEngine;
using PhysicsDrivenMovement.AI;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.Editor
{
    /// <summary>
    /// Editor utility that creates an AI ragdoll prefab variant from the existing
    /// PlayerRagdoll prefab. Removes player-specific components (PlayerMovement,
    /// GrabController, PunchController, DebugPushForce) and adds AI components
    /// (AILocomotion, AIBrain).
    /// Access via: Tools → PhysicsDrivenMovement → Build AI Ragdoll.
    /// </summary>
    public static class AIRagdollBuilder
    {
        private const string PlayerPrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";
        private const string AIPrefabPath     = "Assets/Prefabs/AIRagdoll.prefab";

        [MenuItem("Tools/PhysicsDrivenMovement/Build AI Ragdoll")]
        public static void BuildAIRagdollPrefab()
        {
            // STEP 1: Load the player ragdoll prefab.
            GameObject playerPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (playerPrefab == null)
            {
                Debug.LogError($"[AIRagdollBuilder] PlayerRagdoll prefab not found at '{PlayerPrefabPath}'. " +
                               "Run 'Build Player Ragdoll' first.");
                return;
            }

            // STEP 2: Instantiate a copy to modify.
            GameObject aiGO = Object.Instantiate(playerPrefab);
            aiGO.name = "AIRagdoll";

            // STEP 3: Remove player-specific components from the Hips root.
            RemoveComponent<PlayerMovement>(aiGO);
            RemoveComponent<GrabController>(aiGO);
            RemoveComponent<PunchController>(aiGO);
            RemoveComponent<DebugPushForce>(aiGO);

            // Remove HandGrabZones and their trigger colliders from hands.
            HandGrabZone[] grabZones = aiGO.GetComponentsInChildren<HandGrabZone>(includeInactive: true);
            foreach (HandGrabZone zone in grabZones)
            {
                // Remove the trigger SphereCollider added alongside HandGrabZone.
                SphereCollider[] triggers = zone.GetComponents<SphereCollider>();
                foreach (SphereCollider trigger in triggers)
                {
                    if (trigger.isTrigger)
                    {
                        Object.DestroyImmediate(trigger);
                    }
                }
                Object.DestroyImmediate(zone);
            }

            // Remove ArmAnimator — AI doesn't need arm swing (no PlayerMovement driving it).
            RemoveComponent<ArmAnimator>(aiGO);

            // STEP 4: Add AI components.
            AILocomotion locomotion = aiGO.AddComponent<AILocomotion>();
            using (var so = new SerializedObject(locomotion))
            {
                so.FindProperty("_moveForce").floatValue = 300f;
                so.FindProperty("_maxSpeed").floatValue = 3f;
                so.FindProperty("_arrivalDistance").floatValue = 1.0f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            AIBrain brain = aiGO.AddComponent<AIBrain>();
            using (var so = new SerializedObject(brain))
            {
                so.FindProperty("_idlePauseMin").floatValue = 0.5f;
                so.FindProperty("_idlePauseMax").floatValue = 2f;
                so.FindProperty("_observeTimeMin").floatValue = 3f;
                so.FindProperty("_observeTimeMax").floatValue = 8f;
                so.FindProperty("_fleeDuration").floatValue = 3f;
                so.FindProperty("_fleeDistance").floatValue = 5f;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            // STEP 5: Save as prefab.
            string directory = System.IO.Path.GetDirectoryName(AIPrefabPath);
            if (!AssetDatabase.IsValidFolder(directory))
            {
                AssetDatabase.CreateFolder(
                    System.IO.Path.GetDirectoryName(directory),
                    System.IO.Path.GetFileName(directory));
            }

            PrefabUtility.SaveAsPrefabAsset(aiGO, AIPrefabPath);
            Object.DestroyImmediate(aiGO);

            AssetDatabase.Refresh();
            Debug.Log($"[AIRagdollBuilder] AI ragdoll prefab saved to '{AIPrefabPath}'.");
        }

        private static void RemoveComponent<T>(GameObject go) where T : Component
        {
            T comp = go.GetComponent<T>();
            if (comp != null)
            {
                Object.DestroyImmediate(comp);
            }
        }
    }
}
