using UnityEngine;

namespace PhysicsDrivenMovement.Character
{
    /// <summary>
    /// Runtime spawner that instantiates AI-controlled ragdolls at Start.
    /// Each instance receives an <see cref="AIWander"/> component; the existing
    /// <see cref="RagdollSetup"/> pipeline auto-adds all required character components.
    /// Attach to any scene GameObject and assign the PlayerRagdoll prefab.
    /// </summary>
    public class AISpawner : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("The PlayerRagdoll prefab to instantiate for each AI.")]
        private GameObject _ragdollPrefab;

        [SerializeField, Range(1, 10)]
        [Tooltip("Number of AI ragdolls to spawn.")]
        private int _count = 3;

        [SerializeField, Range(1f, 15f)]
        [Tooltip("Spawn spread radius around world origin.")]
        private float _spawnRadius = 6f;

        private void Start()
        {
            if (_ragdollPrefab == null)
            {
                Debug.LogError("[AISpawner] No ragdoll prefab assigned.", this);
                return;
            }

            for (int i = 0; i < _count; i++)
            {
                Vector2 randomCircle = Random.insideUnitCircle * _spawnRadius;
                Vector3 spawnPos = new Vector3(randomCircle.x, 1.1f, randomCircle.y);

                GameObject instance = Instantiate(_ragdollPrefab, spawnPos, Quaternion.identity);
                instance.name = $"AI_Ragdoll_{i}";
                instance.AddComponent<AIWander>();
            }
        }
    }
}
