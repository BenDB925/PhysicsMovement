using UnityEngine;
using PhysicsDrivenMovement.Character;

namespace PhysicsDrivenMovement.AI
{
    /// <summary>
    /// Scene-level spawner that instantiates AI ragdoll visitors at startup.
    /// Distributes them across the museum rooms and gives each a distinct body color.
    /// Requires the AI ragdoll prefab (built by AIRagdollBuilder) to be assigned.
    /// </summary>
    public class AISpawner : MonoBehaviour
    {
        [SerializeField, Range(1, 10)]
        [Tooltip("Number of AI visitors to spawn.")]
        private int _aiCount = 4;

        [SerializeField]
        [Tooltip("The AI ragdoll prefab (built by AIRagdollBuilder). Must have AILocomotion + AIBrain.")]
        private GameObject _aiRagdollPrefab;

        private static readonly Color[] AIColors = new Color[]
        {
            new Color(0.30f, 0.55f, 0.85f), // blue
            new Color(0.85f, 0.35f, 0.30f), // red
            new Color(0.35f, 0.75f, 0.40f), // green
            new Color(0.80f, 0.60f, 0.20f), // orange
            new Color(0.65f, 0.30f, 0.70f), // purple
            new Color(0.25f, 0.70f, 0.65f), // teal
        };

        // Spawn positions distributed across rooms (not all in lobby).
        private static readonly Vector3[] SpawnPositions = new Vector3[]
        {
            new Vector3( 0f,  1.1f,  0f),    // Main Lobby
            new Vector3( 0f,  1.1f, 12f),    // Sculpture Hall
            new Vector3(-13f, 1.1f, 12f),    // West Gallery
            new Vector3( 13f, 1.1f, 12f),    // East Gallery
            new Vector3(-13f, 1.1f,  0f),    // Storage Room
            new Vector3( 13f, 1.1f,  0f),    // Security Office
        };

        private void Start()
        {
            if (_aiRagdollPrefab == null)
            {
                Debug.LogError("[AISpawner] No AI ragdoll prefab assigned.", this);
                return;
            }

            for (int i = 0; i < _aiCount; i++)
            {
                Vector3 pos = SpawnPositions[i % SpawnPositions.Length];
                // Offset slightly to avoid stacking when multiple AI share a room.
                pos += new Vector3(Random.Range(-1f, 1f), 0f, Random.Range(-1f, 1f));

                GameObject ai = Instantiate(_aiRagdollPrefab, pos, Quaternion.identity);
                ai.name = $"AIVisitor_{i + 1}";

                // Tint the ragdoll body color.
                Color color = AIColors[i % AIColors.Length];
                TintRagdoll(ai, color);
            }

            Debug.Log($"[AISpawner] Spawned {_aiCount} AI visitors.");
        }

        private static void TintRagdoll(GameObject root, Color color)
        {
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(includeInactive: true);
            foreach (Renderer rend in renderers)
            {
                // Create a unique material instance so each AI has its own color.
                Material mat = new Material(rend.sharedMaterial);
                mat.color = color;
                rend.material = mat;
            }
        }
    }
}
