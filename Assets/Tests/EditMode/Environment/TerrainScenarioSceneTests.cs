using System;
using System.Linq;
using NUnit.Framework;
using PhysicsDrivenMovement.Environment;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PhysicsDrivenMovement.Tests.EditMode.Environment
{
    /// <summary>
    /// EditMode scene-authoring checks for Chapter 7 terrain scenarios.
    /// Validates that the shipped Arena_01 and Museum_01 scenes expose the full
    /// controlled terrain set and keep the flat Arena baseline corridor available
    /// for the existing scene-level locomotion regression suites.
    /// </summary>
    [TestFixture]
    public class TerrainScenarioSceneTests
    {
        private const string ArenaScenePath = "Assets/Scenes/Arena_01.unity";
        private const string MuseumScenePath = "Assets/Scenes/Museum_01.unity";
        private const string TerrainScenarioRootName = "TerrainScenarios";

        private string _originalScenePath;

        [SetUp]
        public void SetUp()
        {
            Scene activeScene = SceneManager.GetActiveScene();
            _originalScenePath = activeScene.IsValid() ? activeScene.path : string.Empty;
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_originalScenePath))
            {
                EditorSceneManager.OpenScene(_originalScenePath, OpenSceneMode.Single);
                return;
            }

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        }

        [Test]
        public void Arena01_ContainsOneMarkerPerControlledTerrainScenario()
        {
            // Arrange
            OpenScene(ArenaScenePath);

            // Act
            TerrainScenarioMarker[] markers = FindTerrainMarkers();

            // Assert
            AssertSceneContainsScenarioRoot();
            AssertContainsFullScenarioSet(markers, "Arena_01");
        }

        [Test]
        public void Arena01_TerrainScenarios_LeaveTheFlatBaselineCorridorClear()
        {
            // Arrange
            OpenScene(ArenaScenePath);
            TerrainScenarioMarker[] markers = FindTerrainMarkers();
            Vector3[] baselineSamples =
            {
                new Vector3(0f, 0.1f, 0f),
                new Vector3(6f, 0.1f, 0f),
                new Vector3(12f, 0.1f, 0f),
            };

            // Act / Assert
            foreach (Vector3 sample in baselineSamples)
            {
                bool intersectsScenario = markers.Any(marker => marker.ContainsPoint(sample));
                Assert.That(intersectsScenario, Is.False,
                    $"Arena_01 baseline corridor sample {sample} should remain outside all terrain scenarios.");
            }
        }

        [Test]
        public void Museum01_ContainsOneMarkerPerControlledTerrainScenario()
        {
            // Arrange
            OpenScene(MuseumScenePath);

            // Act
            TerrainScenarioMarker[] markers = FindTerrainMarkers();

            // Assert
            AssertSceneContainsScenarioRoot();
            AssertContainsFullScenarioSet(markers, "Museum_01");
        }

        [Test]
        public void Museum01_TerrainScenarioCenters_FallInsideArenaRooms()
        {
            // Arrange
            OpenScene(MuseumScenePath);
            TerrainScenarioMarker[] markers = FindTerrainMarkers();
            ArenaRoom[] rooms = UnityEngine.Object.FindObjectsByType<ArenaRoom>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);

            Assert.That(rooms.Length, Is.GreaterThan(0), "Museum_01 should contain ArenaRoom metadata components.");

            // Act / Assert
            foreach (TerrainScenarioMarker marker in markers)
            {
                bool insideAnyRoom = rooms.Any(room => room.ContainsPoint(marker.ScenarioBounds.center));
                Assert.That(insideAnyRoom, Is.True,
                    $"Scenario '{marker.ScenarioId}' ({marker.ScenarioType}) should live inside at least one ArenaRoom bounds volume.");
            }
        }

        [Test]
        public void TerrainScenarioMarkers_ReportUsableBoundsAndContainTheirOwnCenters()
        {
            // Arrange
            OpenScene(ArenaScenePath);
            TerrainScenarioMarker[] arenaMarkers = FindTerrainMarkers();
            OpenScene(MuseumScenePath);
            TerrainScenarioMarker[] museumMarkers = FindTerrainMarkers();
            TerrainScenarioMarker[] allMarkers = arenaMarkers.Concat(museumMarkers).ToArray();

            // Act / Assert
            foreach (TerrainScenarioMarker marker in allMarkers)
            {
                Assert.That(string.IsNullOrWhiteSpace(marker.ScenarioId), Is.False,
                    "Terrain scenario ids should be stable, non-empty strings.");
                Assert.That(marker.ScenarioBounds.size.x, Is.GreaterThan(1f),
                    $"Scenario '{marker.ScenarioId}' should have meaningful X coverage.");
                Assert.That(marker.ScenarioBounds.size.z, Is.GreaterThan(1f),
                    $"Scenario '{marker.ScenarioId}' should have meaningful Z coverage.");
                Assert.That(marker.ScenarioBounds.size.y, Is.GreaterThan(0.1f),
                    $"Scenario '{marker.ScenarioId}' should report non-zero vertical extent.");
                Assert.That(marker.ContainsPoint(marker.ScenarioBounds.center), Is.True,
                    $"Scenario '{marker.ScenarioId}' should contain the center of its own bounds.");
            }
        }

        private static void OpenScene(string scenePath)
        {
            Scene openedScene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Assert.That(openedScene.IsValid(), Is.True, $"Failed to open scene '{scenePath}'.");
        }

        private static TerrainScenarioMarker[] FindTerrainMarkers()
        {
            return UnityEngine.Object.FindObjectsByType<TerrainScenarioMarker>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
        }

        private static void AssertSceneContainsScenarioRoot()
        {
            GameObject scenarioRoot = GameObject.Find(TerrainScenarioRootName);
            Assert.That(scenarioRoot, Is.Not.Null,
                $"Scene should contain a '{TerrainScenarioRootName}' hierarchy root for authored terrain scenarios.");
        }

        private static void AssertContainsFullScenarioSet(TerrainScenarioMarker[] markers, string sceneName)
        {
            TerrainScenarioType[] expectedTypes = Enum.GetValues(typeof(TerrainScenarioType))
                .Cast<TerrainScenarioType>()
                .ToArray();

            Assert.That(markers.Length, Is.EqualTo(expectedTypes.Length),
                $"{sceneName} should expose exactly one marker per controlled terrain scenario.");

            foreach (TerrainScenarioType expectedType in expectedTypes)
            {
                int matchingCount = markers.Count(marker => marker.ScenarioType == expectedType);
                Assert.That(matchingCount, Is.EqualTo(1),
                    $"{sceneName} should contain exactly one '{expectedType}' terrain scenario marker.");
            }
        }
    }
}