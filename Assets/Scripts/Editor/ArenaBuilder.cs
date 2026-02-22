using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PhysicsDrivenMovement.Core;
using PhysicsDrivenMovement.Environment;

namespace PhysicsDrivenMovement.Editor
{
    /// <summary>
    /// Editor utility that procedurally builds a 6-room museum arena scene.
    /// Uses a declarative <see cref="RoomDef"/> table (same pattern as RagdollBuilder's
    /// SegmentDef) to define room geometry, then generates floors, ceilings, and walls
    /// with door cutouts. All geometry uses Layer 12 (Environment) and the shared
    /// Ragdoll PhysicsMaterial for proper ground-sensor and friction behaviour.
    /// Access via: Tools → PhysicsDrivenMovement → Build Museum Arena.
    /// Collaborators: <see cref="ArenaRoom"/>, <see cref="GameSettings"/>.
    /// </summary>
    public static class ArenaBuilder
    {
        private const string ScenePath    = "Assets/Scenes/Museum_01.unity";
        private const string PhysMatPath  = "Assets/PhysicsMaterials/Ragdoll.asset";
        private const float  WallThickness = 0.3f;
        private const float  DoorWidth    = 2f;
        private const float  DoorHeight   = 3f;

        // ─── Door Side Flags ────────────────────────────────────────────────

        [System.Flags]
        private enum DoorSides
        {
            None  = 0,
            North = 1 << 0,
            East  = 1 << 1,
            South = 1 << 2,
            West  = 1 << 3,
        }

        // ─── Room Definition ────────────────────────────────────────────────

        /// <summary>Data bag describing one room in the museum layout.</summary>
        private readonly struct RoomDef
        {
            public readonly string    Name;
            public readonly float     XMin;
            public readonly float     XMax;
            public readonly float     ZMin;
            public readonly float     ZMax;
            public readonly float     Height;
            public readonly DoorSides Doors;
            public readonly Color     FloorColor;

            public RoomDef(
                string name, float xMin, float xMax, float zMin, float zMax,
                float height, DoorSides doors, Color floorColor)
            {
                Name       = name;
                XMin       = xMin;
                XMax       = xMax;
                ZMin       = zMin;
                ZMax       = zMax;
                Height     = height;
                Doors      = doors;
                FloorColor = floorColor;
            }

            public float Width  => XMax - XMin;
            public float Depth  => ZMax - ZMin;
            public float CenterX => (XMin + XMax) * 0.5f;
            public float CenterZ => (ZMin + ZMax) * 0.5f;
        }

        // ─── Room Table ─────────────────────────────────────────────────────
        // DESIGN: Ported from Night Shift's MuseumGenerator.Generate().
        // Each room's door flags must be reciprocal with its neighbour
        // (e.g. Lobby.North ↔ SculptureHall.South).

        private static readonly RoomDef[] Rooms = new[]
        {
            new RoomDef(
                name: "Main Lobby", xMin: -8f, xMax: 8f, zMin: -6f, zMax: 6f,
                height: 4f,
                doors: DoorSides.North | DoorSides.East | DoorSides.South | DoorSides.West,
                floorColor: new Color(0.87f, 0.81f, 0.72f)),  // warm beige

            new RoomDef(
                name: "Sculpture Hall", xMin: -8f, xMax: 8f, zMin: 6f, zMax: 18f,
                height: 6f,
                doors: DoorSides.East | DoorSides.South | DoorSides.West,
                floorColor: new Color(0.78f, 0.78f, 0.78f)),  // light gray

            new RoomDef(
                name: "West Gallery", xMin: -18f, xMax: -8f, zMin: 6f, zMax: 18f,
                height: 4f,
                doors: DoorSides.East | DoorSides.South,
                floorColor: new Color(0.36f, 0.25f, 0.17f)),  // dark wood

            new RoomDef(
                name: "East Gallery", xMin: 8f, xMax: 18f, zMin: 6f, zMax: 18f,
                height: 4f,
                doors: DoorSides.South | DoorSides.West,
                floorColor: new Color(0.36f, 0.25f, 0.17f)),  // dark wood

            new RoomDef(
                name: "Storage Room", xMin: -18f, xMax: -8f, zMin: -6f, zMax: 6f,
                height: 4f,
                doors: DoorSides.North | DoorSides.East,
                floorColor: new Color(0.55f, 0.55f, 0.55f)),  // neutral gray

            new RoomDef(
                name: "Security Office", xMin: 8f, xMax: 18f, zMin: -6f, zMax: 6f,
                height: 4f,
                doors: DoorSides.North | DoorSides.West,
                floorColor: new Color(0.30f, 0.35f, 0.42f)),  // dark blue-gray
        };

        // ─── Menu Entry ─────────────────────────────────────────────────────

        [MenuItem("Tools/PhysicsDrivenMovement/Build Museum Arena")]
        public static void BuildMuseumArena()
        {
            // STEP 1: Load shared physics material (created by RagdollBuilder).
            PhysicsMaterial physicsMat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PhysMatPath);
            if (physicsMat == null)
            {
                Debug.LogError($"[ArenaBuilder] PhysicsMaterial not found at '{PhysMatPath}'. " +
                               "Run 'Build Player Ragdoll' first to create it.");
                return;
            }

            // STEP 2: Create a new empty scene.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // STEP 3: GameSettings initialiser.
            GameObject settingsGO = new GameObject("GameSettings");
            settingsGO.AddComponent<GameSettings>();

            // STEP 4: Directional light.
            GameObject lightGO = new GameObject("Directional Light");
            Light light = lightGO.AddComponent<Light>();
            light.type      = LightType.Directional;
            light.intensity = 1f;
            light.color     = new Color(1f, 0.97f, 0.91f); // warm white
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // STEP 5: Camera.
            GameObject camGO = new GameObject("Main Camera");
            Camera cam = camGO.AddComponent<Camera>();
            cam.clearFlags  = CameraClearFlags.Skybox;
            cam.fieldOfView = 60f;
            camGO.transform.SetPositionAndRotation(
                new Vector3(0f, 8f, -16f),
                Quaternion.Euler(25f, 0f, 0f));
            camGO.tag = "MainCamera";

            // STEP 6: Build each room.
            GameObject arenaRoot = new GameObject("Museum");
            foreach (RoomDef room in Rooms)
            {
                BuildRoom(room, arenaRoot.transform, physicsMat);
            }

            // STEP 7: Spawn points in Main Lobby corners.
            BuildSpawnPoints();

            // STEP 8: Save scene.
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.Refresh();

            Debug.Log($"[ArenaBuilder] Museum arena saved to '{ScenePath}'.");
        }

        // ─── Room Builder ───────────────────────────────────────────────────

        private static void BuildRoom(in RoomDef room, Transform parent, PhysicsMaterial physicsMat)
        {
            GameObject roomGO = new GameObject(room.Name);
            roomGO.transform.SetParent(parent, worldPositionStays: false);

            // Attach ArenaRoom component for runtime queries.
            ArenaRoom arenaRoom = roomGO.AddComponent<ArenaRoom>();
            Vector3 boundsCenter = new Vector3(room.CenterX, room.Height * 0.5f, room.CenterZ);
            Vector3 boundsSize   = new Vector3(room.Width, room.Height, room.Depth);
            arenaRoom.Initialise(room.Name, new Bounds(boundsCenter, boundsSize));

            Material floorMat = new Material(Shader.Find("Standard"))
            {
                color = room.FloorColor
            };

            Material wallMat = new Material(Shader.Find("Standard"))
            {
                color = new Color(0.92f, 0.90f, 0.85f) // off-white walls
            };

            Material ceilingMat = new Material(Shader.Find("Standard"))
            {
                color = new Color(0.95f, 0.95f, 0.95f) // near-white ceiling
            };

            // STEP 6a: Floor.
            CreateBox(
                name: $"{room.Name}_Floor",
                parent: roomGO.transform,
                position: new Vector3(room.CenterX, -0.05f, room.CenterZ),
                scale: new Vector3(room.Width, 0.1f, room.Depth),
                material: floorMat,
                physicsMat: physicsMat);

            // STEP 6b: Ceiling.
            CreateBox(
                name: $"{room.Name}_Ceiling",
                parent: roomGO.transform,
                position: new Vector3(room.CenterX, room.Height + 0.05f, room.CenterZ),
                scale: new Vector3(room.Width, 0.1f, room.Depth),
                material: ceilingMat,
                physicsMat: physicsMat);

            // STEP 6c: Walls — four sides, with door cutouts where flagged.
            // North wall (along +Z edge)
            BuildWallZ(roomGO.transform, room, room.ZMax, wallMat, physicsMat,
                       hasDoor: (room.Doors & DoorSides.North) != 0);

            // South wall (along -Z edge)
            BuildWallZ(roomGO.transform, room, room.ZMin, wallMat, physicsMat,
                       hasDoor: (room.Doors & DoorSides.South) != 0);

            // East wall (along +X edge)
            BuildWallX(roomGO.transform, room, room.XMax, wallMat, physicsMat,
                       hasDoor: (room.Doors & DoorSides.East) != 0);

            // West wall (along -X edge)
            BuildWallX(roomGO.transform, room, room.XMin, wallMat, physicsMat,
                       hasDoor: (room.Doors & DoorSides.West) != 0);
        }

        // ─── Wall Builders ──────────────────────────────────────────────────
        // DESIGN: Ported from Night Shift's MuseumGenerator.BuildWallZ/X().
        // A wall with a door is split into three sections: left, right, and a
        // top piece above the doorway. The door opening is centred on the wall.

        /// <summary>
        /// Builds a wall along the Z axis (north or south edge of a room).
        /// Wall runs from room.XMin to room.XMax at the given zPos.
        /// </summary>
        private static void BuildWallZ(
            Transform parent, in RoomDef room, float zPos,
            Material wallMat, PhysicsMaterial physicsMat, bool hasDoor)
        {
            float wallLength = room.Width;
            float cx         = room.CenterX;
            string side      = zPos > room.CenterZ ? "N" : "S";

            if (!hasDoor)
            {
                // Solid wall — one box spanning the full width.
                CreateBox(
                    name: $"{room.Name}_Wall_{side}",
                    parent: parent,
                    position: new Vector3(cx, room.Height * 0.5f, zPos),
                    scale: new Vector3(wallLength, room.Height, WallThickness),
                    material: wallMat,
                    physicsMat: physicsMat);
                return;
            }

            // Door cutout — split into left section, right section, and top above door.
            float halfDoor  = DoorWidth * 0.5f;
            float leftWidth = (wallLength - DoorWidth) * 0.5f;

            // Left section.
            if (leftWidth > 0.01f)
            {
                float leftCenter = room.XMin + leftWidth * 0.5f;
                CreateBox(
                    name: $"{room.Name}_Wall_{side}_L",
                    parent: parent,
                    position: new Vector3(leftCenter, room.Height * 0.5f, zPos),
                    scale: new Vector3(leftWidth, room.Height, WallThickness),
                    material: wallMat,
                    physicsMat: physicsMat);
            }

            // Right section.
            if (leftWidth > 0.01f)
            {
                float rightCenter = room.XMax - leftWidth * 0.5f;
                CreateBox(
                    name: $"{room.Name}_Wall_{side}_R",
                    parent: parent,
                    position: new Vector3(rightCenter, room.Height * 0.5f, zPos),
                    scale: new Vector3(leftWidth, room.Height, WallThickness),
                    material: wallMat,
                    physicsMat: physicsMat);
            }

            // Top section above doorway.
            float topHeight = room.Height - DoorHeight;
            if (topHeight > 0.01f)
            {
                CreateBox(
                    name: $"{room.Name}_Wall_{side}_Top",
                    parent: parent,
                    position: new Vector3(cx, DoorHeight + topHeight * 0.5f, zPos),
                    scale: new Vector3(DoorWidth, topHeight, WallThickness),
                    material: wallMat,
                    physicsMat: physicsMat);
            }
        }

        /// <summary>
        /// Builds a wall along the X axis (east or west edge of a room).
        /// Wall runs from room.ZMin to room.ZMax at the given xPos.
        /// </summary>
        private static void BuildWallX(
            Transform parent, in RoomDef room, float xPos,
            Material wallMat, PhysicsMaterial physicsMat, bool hasDoor)
        {
            float wallLength = room.Depth;
            float cz         = room.CenterZ;
            string side      = xPos > room.CenterX ? "E" : "W";

            if (!hasDoor)
            {
                CreateBox(
                    name: $"{room.Name}_Wall_{side}",
                    parent: parent,
                    position: new Vector3(xPos, room.Height * 0.5f, cz),
                    scale: new Vector3(WallThickness, room.Height, wallLength),
                    material: wallMat,
                    physicsMat: physicsMat);
                return;
            }

            float halfDoor  = DoorWidth * 0.5f;
            float sideWidth = (wallLength - DoorWidth) * 0.5f;

            // Bottom section (lower Z).
            if (sideWidth > 0.01f)
            {
                float lowerCenter = room.ZMin + sideWidth * 0.5f;
                CreateBox(
                    name: $"{room.Name}_Wall_{side}_L",
                    parent: parent,
                    position: new Vector3(xPos, room.Height * 0.5f, lowerCenter),
                    scale: new Vector3(WallThickness, room.Height, sideWidth),
                    material: wallMat,
                    physicsMat: physicsMat);
            }

            // Upper section (higher Z).
            if (sideWidth > 0.01f)
            {
                float upperCenter = room.ZMax - sideWidth * 0.5f;
                CreateBox(
                    name: $"{room.Name}_Wall_{side}_R",
                    parent: parent,
                    position: new Vector3(xPos, room.Height * 0.5f, upperCenter),
                    scale: new Vector3(WallThickness, room.Height, sideWidth),
                    material: wallMat,
                    physicsMat: physicsMat);
            }

            // Top above doorway.
            float topHeight = room.Height - DoorHeight;
            if (topHeight > 0.01f)
            {
                CreateBox(
                    name: $"{room.Name}_Wall_{side}_Top",
                    parent: parent,
                    position: new Vector3(xPos, DoorHeight + topHeight * 0.5f, cz),
                    scale: new Vector3(WallThickness, topHeight, DoorWidth),
                    material: wallMat,
                    physicsMat: physicsMat);
            }
        }

        // ─── Spawn Points ───────────────────────────────────────────────────

        private static void BuildSpawnPoints()
        {
            // Place four spawn points inside the Main Lobby (first room in table).
            RoomDef lobby = Rooms[0];
            float inset = 2f;
            Vector3[] positions =
            {
                new Vector3(lobby.XMin + inset, 0.5f, lobby.ZMax - inset),
                new Vector3(lobby.XMax - inset, 0.5f, lobby.ZMax - inset),
                new Vector3(lobby.XMin + inset, 0.5f, lobby.ZMin + inset),
                new Vector3(lobby.XMax - inset, 0.5f, lobby.ZMin + inset),
            };

            for (int i = 0; i < positions.Length; i++)
            {
                GameObject spawn = new GameObject($"SpawnPoint_{i + 1}");
                spawn.transform.localPosition = positions[i];
            }
        }

        // ─── Geometry Helpers ───────────────────────────────────────────────

        /// <summary>
        /// Creates a static box collider with a visual cube mesh on Layer 12 (Environment).
        /// </summary>
        private static GameObject CreateBox(
            string name, Transform parent, Vector3 position, Vector3 scale,
            Material material, PhysicsMaterial physicsMat)
        {
            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name                    = name;
            go.layer                   = GameSettings.LayerEnvironment;
            go.transform.SetParent(parent, worldPositionStays: true);
            go.transform.localPosition = position;
            go.transform.localScale    = scale;

            // Apply physics material for ground friction.
            BoxCollider col = go.GetComponent<BoxCollider>();
            if (col != null)
            {
                col.material = physicsMat;
            }

            // Apply visual material.
            Renderer rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.sharedMaterial = material;
            }

            return go;
        }
    }
}
