using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PhysicsDrivenMovement.Tests.EditMode.Character
{
    /// <summary>
    /// EditMode validation for the Chapter 9 scenario matrix catalog.
    /// </summary>
    [TestFixture]
    public class ScenarioDefinitionsTests
    {
        private const string PlayModeAssemblyName = "PhysicsDrivenMovement.Tests.PlayMode";
        private const string ScenarioDefinitionsTypeName = "PhysicsDrivenMovement.Tests.PlayMode.ScenarioDefinitions";

        [Test]
        public void AllScenariosAreValid()
        {
            // Arrange
            Assembly playModeAssembly = LoadPlayModeAssembly();
            Type scenarioDefinitionsType = playModeAssembly?.GetType(ScenarioDefinitionsTypeName);
            PropertyInfo allProperty = scenarioDefinitionsType?.GetProperty(
                "All",
                BindingFlags.Public | BindingFlags.Static);

            // Act
            Array scenarios = allProperty?.GetValue(null) as Array;

            // Assert
            Assert.That(playModeAssembly, Is.Not.Null,
                $"Expected PlayMode test assembly '{PlayModeAssemblyName}' to be loadable in the editor domain.");
            Assert.That(scenarioDefinitionsType, Is.Not.Null,
                $"Expected type '{ScenarioDefinitionsTypeName}' to exist for the Chapter 9 scenario catalog.");
            Assert.That(allProperty, Is.Not.Null,
                "ScenarioDefinitions should expose a public static All property for the canonical scenario list.");
            Assert.That(scenarios, Is.Not.Null,
                "ScenarioDefinitions.All should return a concrete array of scenario entries.");
            Assert.That(scenarios.Length, Is.EqualTo(10),
                "Chapter 9 scenario coverage should include the SprintJump regression scenario.");

            HashSet<string> uniqueNames = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < scenarios.Length; index++)
            {
                object scenario = scenarios.GetValue(index);
                Type scenarioType = scenario.GetType();

                string name = (string)scenarioType.GetProperty("Name")?.GetValue(scenario);
                Vector3[] waypoints = scenarioType.GetProperty("Waypoints")?.GetValue(scenario) as Vector3[];
                float expectedDurationSeconds = (float)(scenarioType.GetProperty("ExpectedDurationSeconds")?.GetValue(scenario) ?? 0f);
                string[] exercisedSubsystems = scenarioType.GetProperty("ExercisedSubsystems")?.GetValue(scenario) as string[];

                Assert.That(string.IsNullOrWhiteSpace(name), Is.False,
                    $"Scenario at index {index} should have a non-empty name.");
                Assert.That(uniqueNames.Add(name), Is.True,
                    $"Scenario '{name}' should appear only once in ScenarioDefinitions.All.");
                Assert.That(waypoints, Is.Not.Null,
                    $"Scenario '{name}' should expose a waypoint sequence.");
                Assert.That(waypoints.Length, Is.GreaterThan(0),
                    $"Scenario '{name}' should include at least one waypoint.");
                Assert.That(expectedDurationSeconds, Is.GreaterThan(0f),
                    $"Scenario '{name}' should define a positive duration budget.");
                Assert.That(exercisedSubsystems, Is.Not.Null,
                    $"Scenario '{name}' should expose exercised subsystems.");
                Assert.That(exercisedSubsystems.Length, Is.GreaterThan(0),
                    $"Scenario '{name}' should list at least one exercised subsystem.");
            }

            Assert.That(uniqueNames.Contains("SprintJump"), Is.True,
                "ScenarioDefinitions.All should include the SprintJump regression scenario.");
        }

        private static Assembly LoadPlayModeAssembly()
        {
            // STEP 1: Prefer an already-loaded test assembly so the editor domain stays stable.
            Assembly[] loadedAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int index = 0; index < loadedAssemblies.Length; index++)
            {
                Assembly assembly = loadedAssemblies[index];
                if (assembly.GetName().Name == PlayModeAssemblyName)
                {
                    return assembly;
                }
            }

            // STEP 2: Fall back to an explicit load when the PlayMode test assembly has not been resolved yet.
            return Assembly.Load(PlayModeAssemblyName);
        }
    }
}
