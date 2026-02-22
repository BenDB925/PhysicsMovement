using UnityEngine;

namespace PhysicsDrivenMovement.Environment
{
    /// <summary>
    /// Lightweight runtime component attached to each room GameObject in a museum arena.
    /// Stores the room's name and axis-aligned bounds so gameplay systems can query which
    /// room a world-space point falls within (e.g. zone-based scoring, room-specific hazards).
    /// Access via: GetComponent on room GameObjects built by ArenaBuilder.
    /// Collaborators: None (standalone data holder).
    /// </summary>
    public class ArenaRoom : MonoBehaviour
    {
        // ─── Serialised Fields ──────────────────────────────────────────────

        [SerializeField]
        [Tooltip("Human-readable name of this room (e.g. 'Main Lobby').")]
        private string _roomName;

        [SerializeField]
        [Tooltip("World-space axis-aligned bounds of the room floor area and ceiling height.")]
        private Bounds _bounds;

        // ─── Public Properties ──────────────────────────────────────────────

        /// <summary>Human-readable name of this room.</summary>
        public string RoomName => _roomName;

        /// <summary>World-space axis-aligned bounds of this room.</summary>
        public Bounds RoomBounds => _bounds;

        // ─── Public Methods ─────────────────────────────────────────────────

        /// <summary>
        /// Returns true if <paramref name="point"/> is inside this room's bounds.
        /// </summary>
        public bool ContainsPoint(Vector3 point)
        {
            return _bounds.Contains(point);
        }

        /// <summary>
        /// Initialises the room data. Called by the editor builder at scene-build time.
        /// </summary>
        public void Initialise(string roomName, Bounds bounds)
        {
            _roomName = roomName;
            _bounds   = bounds;
        }
    }
}
