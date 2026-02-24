using System.Collections.Generic;
using UnityEngine;

namespace PhysicsDrivenMovement.AI
{
    /// <summary>
    /// Static navigation helper for the 6-room museum layout. Uses a hard-coded
    /// room connectivity graph matching <see cref="PhysicsDrivenMovement.Editor.ArenaBuilder"/>
    /// and BFS to find the shortest path between any two rooms. Returns a list of
    /// door waypoints the AI must walk through to reach the destination room.
    /// </summary>
    public static class MuseumNavigator
    {
        // ─── Room Indices ─────────────────────────────────────────────────────
        public const int Lobby          = 0;
        public const int SculptureHall  = 1;
        public const int WestGallery    = 2;
        public const int EastGallery    = 3;
        public const int StorageRoom    = 4;
        public const int SecurityOffice = 5;
        public const int RoomCount      = 6;

        // ─── Room Bounds (matching ArenaBuilder) ──────────────────────────────
        // Each entry: (xMin, xMax, zMin, zMax)
        private static readonly Vector4[] RoomBounds = new Vector4[]
        {
            new Vector4(-8f, 8f, -6f, 6f),    // Lobby
            new Vector4(-8f, 8f, 6f, 18f),     // Sculpture Hall
            new Vector4(-18f, -8f, 6f, 18f),   // West Gallery
            new Vector4(8f, 18f, 6f, 18f),     // East Gallery
            new Vector4(-18f, -8f, -6f, 6f),   // Storage Room
            new Vector4(8f, 18f, -6f, 6f),     // Security Office
        };

        // ─── Door Definitions ─────────────────────────────────────────────────
        // Each door connects two rooms and has a world-space position.

        private struct DoorDef
        {
            public int RoomA;
            public int RoomB;
            public Vector3 Position;
        }

        private static readonly DoorDef[] Doors = new DoorDef[]
        {
            // Lobby <-> Sculpture Hall (north wall of lobby at Z=6, center X=0)
            new DoorDef { RoomA = Lobby, RoomB = SculptureHall, Position = new Vector3(0f, 0f, 6f) },
            // Lobby <-> Storage Room (west wall of lobby at X=-8, center Z=0)
            new DoorDef { RoomA = Lobby, RoomB = StorageRoom, Position = new Vector3(-8f, 0f, 0f) },
            // Lobby <-> Security Office (east wall of lobby at X=8, center Z=0)
            new DoorDef { RoomA = Lobby, RoomB = SecurityOffice, Position = new Vector3(8f, 0f, 0f) },
            // Sculpture Hall <-> West Gallery (west wall at X=-8, center Z=12)
            new DoorDef { RoomA = SculptureHall, RoomB = WestGallery, Position = new Vector3(-8f, 0f, 12f) },
            // Sculpture Hall <-> East Gallery (east wall at X=8, center Z=12)
            new DoorDef { RoomA = SculptureHall, RoomB = EastGallery, Position = new Vector3(8f, 0f, 12f) },
            // West Gallery <-> Storage Room (south wall of WG at Z=6, center X=-13)
            new DoorDef { RoomA = WestGallery, RoomB = StorageRoom, Position = new Vector3(-13f, 0f, 6f) },
            // East Gallery <-> Security Office (south wall of EG at Z=6, center X=13)
            new DoorDef { RoomA = EastGallery, RoomB = SecurityOffice, Position = new Vector3(13f, 0f, 6f) },
        };

        // ─── Adjacency Graph ──────────────────────────────────────────────────
        // Built once from door definitions. adjacency[roomA] = list of (neighbourRoom, doorIndex).

        private static readonly List<(int neighbour, int doorIndex)>[] Adjacency;

        static MuseumNavigator()
        {
            Adjacency = new List<(int, int)>[RoomCount];
            for (int i = 0; i < RoomCount; i++)
            {
                Adjacency[i] = new List<(int, int)>();
            }

            for (int d = 0; d < Doors.Length; d++)
            {
                Adjacency[Doors[d].RoomA].Add((Doors[d].RoomB, d));
                Adjacency[Doors[d].RoomB].Add((Doors[d].RoomA, d));
            }
        }

        /// <summary>
        /// Determines which room a world-space point falls within.
        /// Uses the hard-coded bounds. Returns -1 if the point is outside all rooms.
        /// </summary>
        public static int GetRoomIndex(Vector3 point)
        {
            for (int i = 0; i < RoomCount; i++)
            {
                Vector4 b = RoomBounds[i];
                if (point.x >= b.x && point.x <= b.y &&
                    point.z >= b.z && point.z <= b.w)
                {
                    return i;
                }
            }
            return -1;
        }

        /// <summary>
        /// Returns the name of a room by index.
        /// </summary>
        public static string GetRoomName(int roomIndex)
        {
            switch (roomIndex)
            {
                case Lobby: return "Main Lobby";
                case SculptureHall: return "Sculpture Hall";
                case WestGallery: return "West Gallery";
                case EastGallery: return "East Gallery";
                case StorageRoom: return "Storage Room";
                case SecurityOffice: return "Security Office";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// Finds the shortest path of door waypoints from <paramref name="from"/> to
        /// <paramref name="to"/> using BFS on the room connectivity graph.
        /// Returns an empty list if both points are in the same room.
        /// Returns null if either point is outside all rooms.
        /// </summary>
        public static List<Vector3> FindPath(Vector3 from, Vector3 to)
        {
            int startRoom = GetRoomIndex(from);
            int endRoom = GetRoomIndex(to);

            if (startRoom < 0 || endRoom < 0)
            {
                return null;
            }

            if (startRoom == endRoom)
            {
                return new List<Vector3>();
            }

            // BFS
            int[] parent = new int[RoomCount];
            int[] parentDoor = new int[RoomCount];
            bool[] visited = new bool[RoomCount];

            for (int i = 0; i < RoomCount; i++)
            {
                parent[i] = -1;
                parentDoor[i] = -1;
            }

            Queue<int> queue = new Queue<int>();
            queue.Enqueue(startRoom);
            visited[startRoom] = true;

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();

                if (current == endRoom)
                {
                    break;
                }

                foreach (var (neighbour, doorIndex) in Adjacency[current])
                {
                    if (visited[neighbour])
                    {
                        continue;
                    }

                    visited[neighbour] = true;
                    parent[neighbour] = current;
                    parentDoor[neighbour] = doorIndex;
                    queue.Enqueue(neighbour);
                }
            }

            if (!visited[endRoom])
            {
                return null; // No path found (shouldn't happen in connected graph)
            }

            // Reconstruct path — collect door positions from end back to start.
            List<Vector3> waypoints = new List<Vector3>();
            int room = endRoom;
            while (parent[room] >= 0)
            {
                waypoints.Add(Doors[parentDoor[room]].Position);
                room = parent[room];
            }

            waypoints.Reverse();
            return waypoints;
        }

        /// <summary>
        /// Returns the world-space center of the room at the given index (at ground level).
        /// </summary>
        public static Vector3 GetRoomCenter(int roomIndex)
        {
            if (roomIndex < 0 || roomIndex >= RoomCount)
            {
                return Vector3.zero;
            }

            Vector4 b = RoomBounds[roomIndex];
            return new Vector3((b.x + b.y) * 0.5f, 0f, (b.z + b.w) * 0.5f);
        }
    }
}
