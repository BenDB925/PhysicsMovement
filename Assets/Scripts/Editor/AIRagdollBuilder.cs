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

            // STEP 3b: Reduce mass so AI visitors are easy to grab and throw.
            //          0.35x player mass (~17kg total vs ~49kg).
            const float massMultiplier = 0.35f;
            Rigidbody[] allBodies = aiGO.GetComponentsInChildren<Rigidbody>(includeInactive: true);
            foreach (Rigidbody rb in allBodies)
            {
                rb.mass *= massMultiplier;
            }

            // STEP 3c: Move HitReceiver from Head to Torso so body punches trigger KO.
            //          Lower knockout threshold so punches reliably KO the lighter AI.
            HitReceiver oldHitReceiver = aiGO.GetComponentInChildren<HitReceiver>();
            if (oldHitReceiver != null)
            {
                Object.DestroyImmediate(oldHitReceiver);
            }

            // Find the Torso child and add HitReceiver there.
            Transform torso = aiGO.transform.Find("Torso");
            if (torso != null)
            {
                HitReceiver hitReceiver = torso.gameObject.AddComponent<HitReceiver>();
                using var hrSo = new SerializedObject(hitReceiver);
                hrSo.FindProperty("_knockoutVelocityThreshold").floatValue = 3f;
                hrSo.FindProperty("_knockoutDuration").floatValue = 4f;
                hrSo.ApplyModifiedPropertiesWithoutUndo();
            }

            // STEP 4: Add AI components.
            AILocomotion locomotion = aiGO.AddComponent<AILocomotion>();
            using (var so = new SerializedObject(locomotion))
            {
                so.FindProperty("_moveForce").floatValue = 30f;
                so.FindProperty("_maxSpeed").floatValue = 2f;
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
