using UnityEngine;

namespace PhysicsDrivenMovement.Environment
{
    /// <summary>
    /// Lightweight runtime metadata attached to a terrain scenario root in authored
    /// scenes. It exposes the scenario identity and axis-aligned bounds so tests and
    /// future locomotion systems can reason about the authored terrain set without
    /// hard-coding scene-object names.
    /// </summary>
    public class TerrainScenarioMarker : MonoBehaviour
    {
        [SerializeField]
        [Tooltip("Stable identifier for this terrain scenario instance inside the scene.")]
        private string _scenarioId;

        [SerializeField]
        [Tooltip("Controlled terrain scenario category authored for locomotion validation.")]
        private TerrainScenarioType _scenarioType;

        [SerializeField]
        [Tooltip("World-space axis-aligned bounds occupied by this scenario.")]
        private Bounds _bounds;

        /// <summary>
        /// Stable identifier for this terrain scenario instance.
        /// </summary>
        public string ScenarioId => _scenarioId;

        /// <summary>
        /// Terrain scenario category for this instance.
        /// </summary>
        public TerrainScenarioType ScenarioType => _scenarioType;

        /// <summary>
        /// World-space bounds covered by this scenario.
        /// </summary>
        public Bounds ScenarioBounds => _bounds;

        /// <summary>
        /// Returns whether the supplied world-space point is inside the scenario bounds.
        /// </summary>
        public bool ContainsPoint(Vector3 point)
        {
            return _bounds.Contains(point);
        }

        /// <summary>
        /// Initializes the scenario metadata after a builder authors the scene object.
        /// </summary>
        public void Initialise(string scenarioId, TerrainScenarioType scenarioType, Bounds bounds)
        {
            // STEP 1: Record the stable scenario identity so tests and future systems
            //         can resolve this authored lane without relying on hierarchy names.
            _scenarioId = scenarioId;

            // STEP 2: Persist the scenario category so the authored scene advertises
            //         which locomotion case it was built to exercise.
            _scenarioType = scenarioType;

            // STEP 3: Cache the world-space bounds used for scene queries and layout checks.
            _bounds = bounds;
        }
    }
}