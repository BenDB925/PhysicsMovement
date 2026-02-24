using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using PhysicsDrivenMovement.AI;
using PhysicsDrivenMovement.Core;

namespace PhysicsDrivenMovement.Editor
{
    /// <summary>
    /// Editor utility that populates the museum arena with interactive props.
    /// Spawns pedestal exhibits (static base + dynamic top piece) and wall paintings
    /// as physics-enabled obstacles for ragdolls to collide with and push over.
    /// Separated from <see cref="ArenaBuilder"/> so arena geometry and props can be
    /// rebuilt independently.
    /// Access via: Tools → PhysicsDrivenMovement → Populate Museum Props.
    /// Collaborators: <see cref="ArenaBuilder"/>, <see cref="GameSettings"/>.
    /// </summary>
    public static class PropBuilder
    {
        private const string PhysMatPath = "Assets/PhysicsMaterials/Ragdoll.asset";

        // ─── Prop Definition ────────────────────────────────────────────────

        private enum PropType { Pedestal, Painting }

        /// <summary>Data bag describing one prop placement.</summary>
        private readonly struct PropDef
        {
            public readonly string   Name;
            public readonly PropType Type;
            public readonly Vector3  Position;
            public readonly Color    Color;

            public PropDef(string name, PropType type, Vector3 position, Color color)
            {
                Name     = name;
                Type     = type;
                Position = position;
                Color    = color;
            }
        }

        // ─── Prop Table ─────────────────────────────────────────────────────
        // DESIGN: Pedestal exhibits go in Sculpture Hall and Main Lobby.
        // Wall paintings go in West/East Gallery along walls.

        private static readonly PropDef[] Props = new[]
        {
            // ── Sculpture Hall pedestals ────────────────────────────────────
            new PropDef(
                name: "Exhibit_Sculpture_1", type: PropType.Pedestal,
                position: new Vector3(-3f, 0f, 12f),
                color: new Color(0.85f, 0.25f, 0.20f)),  // red

            new PropDef(
                name: "Exhibit_Sculpture_2", type: PropType.Pedestal,
                position: new Vector3(3f, 0f, 12f),
                color: new Color(0.20f, 0.55f, 0.85f)),  // blue

            new PropDef(
                name: "Exhibit_Sculpture_3", type: PropType.Pedestal,
                position: new Vector3(0f, 0f, 15f),
                color: new Color(0.90f, 0.75f, 0.20f)),  // gold

            // ── Main Lobby pedestals ────────────────────────────────────────
            new PropDef(
                name: "Exhibit_Lobby_1", type: PropType.Pedestal,
                position: new Vector3(-4f, 0f, 0f),
                color: new Color(0.35f, 0.75f, 0.40f)),  // green

            new PropDef(
                name: "Exhibit_Lobby_2", type: PropType.Pedestal,
                position: new Vector3(4f, 0f, 0f),
                color: new Color(0.70f, 0.30f, 0.70f)),  // purple

            // ── West Gallery paintings (east-facing wall, X = -17.8) ────────
            new PropDef(
                name: "Painting_West_1", type: PropType.Painting,
                position: new Vector3(-17.7f, 2.2f, 10f),
                color: new Color(0.80f, 0.45f, 0.20f)),  // burnt orange

            new PropDef(
                name: "Painting_West_2", type: PropType.Painting,
                position: new Vector3(-17.7f, 2.2f, 14f),
                color: new Color(0.20f, 0.40f, 0.65f)),  // navy

            // ── East Gallery paintings (west-facing wall, X = 17.8) ─────────
            new PropDef(
                name: "Painting_East_1", type: PropType.Painting,
                position: new Vector3(17.7f, 2.2f, 10f),
                color: new Color(0.65f, 0.20f, 0.30f)),  // crimson

            new PropDef(
                name: "Painting_East_2", type: PropType.Painting,
                position: new Vector3(17.7f, 2.2f, 14f),
                color: new Color(0.25f, 0.55f, 0.45f)),  // teal
        };

        // ─── Menu Entry ─────────────────────────────────────────────────────

        [MenuItem("Tools/PhysicsDrivenMovement/Populate Museum Props")]
        public static void PopulateMuseumProps()
        {
            // STEP 1: Load shared physics material.
            PhysicsMaterial physicsMat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(PhysMatPath);
            if (physicsMat == null)
            {
                Debug.LogError($"[PropBuilder] PhysicsMaterial not found at '{PhysMatPath}'. " +
                               "Run 'Build Player Ragdoll' first to create it.");
                return;
            }

            // STEP 2: Find or create the Props container in the active scene.
            GameObject propsRoot = GameObject.Find("Museum_Props");
            if (propsRoot != null)
            {
                Object.DestroyImmediate(propsRoot);
            }
            propsRoot = new GameObject("Museum_Props");

            // STEP 3: Spawn each prop from the table.
            foreach (PropDef prop in Props)
            {
                switch (prop.Type)
                {
                    case PropType.Pedestal:
                        BuildPedestal(prop, propsRoot.transform, physicsMat);
                        break;
                    case PropType.Painting:
                        BuildPainting(prop, propsRoot.transform, physicsMat);
                        break;
                }
            }

            // STEP 4: Mark scene dirty so changes are saved.
            EditorSceneManager.MarkSceneDirty(
                UnityEngine.SceneManagement.SceneManager.GetActiveScene());

            Debug.Log($"[PropBuilder] Spawned {Props.Length} props in active scene.");
        }

        // ─── Prop Builders ──────────────────────────────────────────────────

        /// <summary>
        /// Builds a pedestal exhibit: static cube base + dynamic cube on top.
        /// The top piece has a Rigidbody so the ragdoll can knock it over.
        /// </summary>
        private static void BuildPedestal(
            in PropDef prop, Transform parent, PhysicsMaterial physicsMat)
        {
            // Pedestal base — static (no Rigidbody), acts as furniture.
            GameObject baseCube = CreateStaticBox(
                name: $"{prop.Name}_Base",
                parent: parent,
                position: prop.Position + new Vector3(0f, 0.5f, 0f),
                scale: new Vector3(0.6f, 1f, 0.6f),
                color: new Color(0.75f, 0.72f, 0.68f),  // stone gray
                physicsMat: physicsMat);

            // Exhibit piece — dynamic, sits on top of the pedestal.
            Material exhibitMat = new Material(Shader.Find("Standard"))
            {
                color = prop.Color
            };

            GameObject exhibit = GameObject.CreatePrimitive(PrimitiveType.Cube);
            exhibit.name                    = $"{prop.Name}_Piece";
            exhibit.layer                   = GameSettings.LayerEnvironment;
            exhibit.transform.SetParent(parent, worldPositionStays: true);
            exhibit.transform.localPosition = prop.Position + new Vector3(0f, 1.25f, 0f);
            exhibit.transform.localScale    = new Vector3(0.4f, 0.5f, 0.4f);

            Renderer rend = exhibit.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.sharedMaterial = exhibitMat;
            }

            BoxCollider col = exhibit.GetComponent<BoxCollider>();
            if (col != null)
            {
                col.material = physicsMat;
            }

            // Add Rigidbody so this piece is pushable.
            Rigidbody rb    = exhibit.AddComponent<Rigidbody>();
            rb.mass         = 5f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // Add AI interest point on the base (stable, won't move when knocked).
            MuseumInterestPoint interestPoint = baseCube.AddComponent<MuseumInterestPoint>();
            interestPoint.Initialise(viewDistance: 1.5f, viewDirectionLocal: Vector3.forward);
        }

        /// <summary>
        /// Builds a wall painting: a thin, dynamic box mounted on a wall.
        /// Light enough that a ragdoll collision can knock it off.
        /// </summary>
        private static void BuildPainting(
            in PropDef prop, Transform parent, PhysicsMaterial physicsMat)
        {
            Material paintingMat = new Material(Shader.Find("Standard"))
            {
                color = prop.Color
            };

            GameObject painting = GameObject.CreatePrimitive(PrimitiveType.Cube);
            painting.name                    = prop.Name;
            painting.layer                   = GameSettings.LayerEnvironment;
            painting.transform.SetParent(parent, worldPositionStays: true);
            painting.transform.localPosition = prop.Position;
            painting.transform.localScale    = new Vector3(0.1f, 1.2f, 1.8f);

            Renderer rend = painting.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.sharedMaterial = paintingMat;
            }

            BoxCollider col = painting.GetComponent<BoxCollider>();
            if (col != null)
            {
                col.material = physicsMat;
            }

            // Add Rigidbody — paintings can be knocked off walls.
            Rigidbody rb    = painting.AddComponent<Rigidbody>();
            rb.mass         = 2f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;

            // Add AI interest point — surface normal points away from the wall.
            // West wall paintings (negative X) face east (+X), east wall paintings face west (-X).
            Vector3 surfaceNormal = prop.Position.x < 0f ? Vector3.right : Vector3.left;
            MuseumInterestPoint interestPoint = painting.AddComponent<MuseumInterestPoint>();
            interestPoint.Initialise(viewDistance: 2f, viewDirectionLocal: surfaceNormal);
        }

        // ─── Geometry Helpers ───────────────────────────────────────────────

        /// <summary>
        /// Creates a static box (no Rigidbody) on Layer 12 with a visual material.
        /// </summary>
        private static GameObject CreateStaticBox(
            string name, Transform parent, Vector3 position, Vector3 scale,
            Color color, PhysicsMaterial physicsMat)
        {
            Material mat = new Material(Shader.Find("Standard"))
            {
                color = color
            };

            GameObject go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name                    = name;
            go.layer                   = GameSettings.LayerEnvironment;
            go.transform.SetParent(parent, worldPositionStays: true);
            go.transform.localPosition = position;
            go.transform.localScale    = scale;

            Renderer rend = go.GetComponent<Renderer>();
            if (rend != null)
            {
                rend.sharedMaterial = mat;
            }

            BoxCollider col = go.GetComponent<BoxCollider>();
            if (col != null)
            {
                col.material = physicsMat;
            }

            return go;
        }
    }
}
