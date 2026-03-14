using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEditor;
using UnityEngine;

namespace PhysicsDrivenMovement.Tests.EditMode.Character
{
    /// <summary>
    /// EditMode coverage for the prefab-side LocomotionDirector seam introduced by
    /// Chapter 1 task C1.3 of the unified locomotion roadmap.
    /// </summary>
    [TestFixture]
    public class LocomotionDirectorEditModeTests
    {
        private const string PlayerRagdollPrefabPath = "Assets/Prefabs/PlayerRagdoll.prefab";

        [Test]
        public void PlayerRagdollPrefab_LocomotionDirector_IsPresentAndPassThroughByDefault()
        {
            // Arrange
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);

            // Act
            LocomotionDirector director = prefabRoot.GetComponent<LocomotionDirector>();

            // Assert
            Assert.That(prefabRoot, Is.Not.Null, "PlayerRagdoll prefab must exist at the expected path.");
            Assert.That(director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector so the director exists on the Hips runtime path.");
            Assert.That(director.IsPassThroughMode, Is.True,
                "LocomotionDirector should default to pass-through mode until downstream executors are rewired.");
        }

        [Test]
        public void PlayerRagdollPrefab_LocomotionDirector_UsesSerializedObservationHysteresisThresholds()
        {
            // Arrange
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            LocomotionDirector director = prefabRoot.GetComponent<LocomotionDirector>();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

            FieldInfo contactRiseField = typeof(LocomotionDirector).GetField("_contactConfidenceRiseSpeed", flags);
            FieldInfo contactFallField = typeof(LocomotionDirector).GetField("_contactConfidenceFallSpeed", flags);
            FieldInfo plantedRiseField = typeof(LocomotionDirector).GetField("_plantedConfidenceRiseSpeed", flags);
            FieldInfo plantedFallField = typeof(LocomotionDirector).GetField("_plantedConfidenceFallSpeed", flags);
            FieldInfo plantedEnterField = typeof(LocomotionDirector).GetField("_plantedEnterThreshold", flags);
            FieldInfo plantedExitField = typeof(LocomotionDirector).GetField("_plantedExitThreshold", flags);

            // Act
            float contactRiseSpeed = (float)contactRiseField.GetValue(director);
            float contactFallSpeed = (float)contactFallField.GetValue(director);
            float plantedRiseSpeed = (float)plantedRiseField.GetValue(director);
            float plantedFallSpeed = (float)plantedFallField.GetValue(director);
            float plantedEnterThreshold = (float)plantedEnterField.GetValue(director);
            float plantedExitThreshold = (float)plantedExitField.GetValue(director);

            // Assert
            Assert.That(prefabRoot, Is.Not.Null, "PlayerRagdoll prefab must exist at the expected path.");
            Assert.That(director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for the Chapter 2 observation model.");
            Assert.That(contactRiseField, Is.Not.Null,
                "LocomotionDirector should serialize a contact-confidence rise speed for C2.3 filtering.");
            Assert.That(contactFallField, Is.Not.Null,
                "LocomotionDirector should serialize a contact-confidence fall speed for C2.3 filtering.");
            Assert.That(plantedRiseField, Is.Not.Null,
                "LocomotionDirector should serialize a planted-confidence rise speed for C2.3 filtering.");
            Assert.That(plantedFallField, Is.Not.Null,
                "LocomotionDirector should serialize a planted-confidence fall speed for C2.3 filtering.");
            Assert.That(plantedEnterField, Is.Not.Null,
                "LocomotionDirector should serialize a planted enter threshold for C2.3 hysteresis.");
            Assert.That(plantedExitField, Is.Not.Null,
                "LocomotionDirector should serialize a planted exit threshold for C2.3 hysteresis.");
            Assert.That(contactRiseSpeed, Is.GreaterThan(0f));
            Assert.That(contactFallSpeed, Is.GreaterThan(0f));
            Assert.That(plantedRiseSpeed, Is.GreaterThan(0f));
            Assert.That(plantedFallSpeed, Is.GreaterThan(0f));
            Assert.That(plantedEnterThreshold, Is.GreaterThan(plantedExitThreshold),
                "The planted enter threshold should stay above the exit threshold to preserve hysteresis.");
        }

        [Test]
        public void PlayerRagdollPrefab_LocomotionDirector_UsesSerializedObservationDebugVisibilitySettings()
        {
            // Arrange
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            LocomotionDirector director = prefabRoot.GetComponent<LocomotionDirector>();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

            FieldInfo debugDrawField = typeof(LocomotionDirector).GetField("_debugObservationDraw", flags);
            FieldInfo debugTelemetryField = typeof(LocomotionDirector).GetField("_debugObservationTelemetry", flags);
            FieldInfo debugTelemetryIntervalField = typeof(LocomotionDirector).GetField("_debugObservationTelemetryInterval", flags);
            FieldInfo debugDrawHeightField = typeof(LocomotionDirector).GetField("_debugObservationDrawHeight", flags);

            // Act
            bool debugDrawEnabled = (bool)debugDrawField.GetValue(director);
            bool debugTelemetryEnabled = (bool)debugTelemetryField.GetValue(director);
            float debugTelemetryInterval = (float)debugTelemetryIntervalField.GetValue(director);
            float debugDrawHeight = (float)debugDrawHeightField.GetValue(director);

            // Assert
            Assert.That(prefabRoot, Is.Not.Null, "PlayerRagdoll prefab must exist at the expected path.");
            Assert.That(director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for the Chapter 2 debug-visibility slice.");
            Assert.That(debugDrawField, Is.Not.Null,
                "LocomotionDirector should serialize an observation debug-draw toggle for C2.4 visibility.");
            Assert.That(debugTelemetryField, Is.Not.Null,
                "LocomotionDirector should serialize an observation telemetry toggle for C2.4 visibility.");
            Assert.That(debugTelemetryIntervalField, Is.Not.Null,
                "LocomotionDirector should serialize a telemetry interval for C2.4 visibility.");
            Assert.That(debugDrawHeightField, Is.Not.Null,
                "LocomotionDirector should serialize a draw height offset so support debug geometry stays readable.");
            Assert.That(debugDrawEnabled, Is.False,
                "Observation debug draw should default off on the production prefab.");
            Assert.That(debugTelemetryEnabled, Is.False,
                "Observation telemetry should default off on the production prefab.");
            Assert.That(debugTelemetryInterval, Is.GreaterThan(0f));
            Assert.That(debugDrawHeight, Is.GreaterThan(0f));
        }

        [Test]
        public void PlayerRagdollPrefab_LocomotionDirector_UsesSerializedObservationDecisionSettings()
        {
            // Arrange
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            LocomotionDirector director = prefabRoot.GetComponent<LocomotionDirector>();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

            FieldInfo recoveryTurnThresholdField = typeof(LocomotionDirector).GetField("_turnRecoveryThreshold", flags);
            FieldInfo recoverySupportThresholdField = typeof(LocomotionDirector).GetField("_supportRiskRecoveryThreshold", flags);
            FieldInfo minYawStrengthField = typeof(LocomotionDirector).GetField("_minimumRiskYawStrengthScale", flags);
            FieldInfo uprightBoostField = typeof(LocomotionDirector).GetField("_supportRiskUprightBoost", flags);
            FieldInfo stabilizationBoostField = typeof(LocomotionDirector).GetField("_supportRiskStabilizationBoost", flags);

            // Act
            Assert.That(prefabRoot, Is.Not.Null, "PlayerRagdoll prefab must exist at the expected path.");
            Assert.That(director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for the Chapter 2 decision-integration slice.");
            Assert.That(recoveryTurnThresholdField, Is.Not.Null,
                "LocomotionDirector should serialize a turn-severity recovery threshold for C2.5 observation-driven decisions.");
            Assert.That(recoverySupportThresholdField, Is.Not.Null,
                "LocomotionDirector should serialize a support-risk recovery threshold for C2.5 observation-driven decisions.");
            Assert.That(minYawStrengthField, Is.Not.Null,
                "LocomotionDirector should serialize a minimum yaw-strength scale for risky observation states.");
            Assert.That(uprightBoostField, Is.Not.Null,
                "LocomotionDirector should serialize an upright-strength boost for risky support observations.");
            Assert.That(stabilizationBoostField, Is.Not.Null,
                "LocomotionDirector should serialize a stabilization-strength boost for risky support observations.");

            // Act
            float recoveryTurnThreshold = (float)recoveryTurnThresholdField.GetValue(director);
            float recoverySupportThreshold = (float)recoverySupportThresholdField.GetValue(director);
            float minYawStrength = (float)minYawStrengthField.GetValue(director);
            float uprightBoost = (float)uprightBoostField.GetValue(director);
            float stabilizationBoost = (float)stabilizationBoostField.GetValue(director);

            // Assert
            Assert.That(recoveryTurnThreshold, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
            Assert.That(recoverySupportThreshold, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
            Assert.That(minYawStrength, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f));
            Assert.That(uprightBoost, Is.GreaterThan(0f));
            Assert.That(stabilizationBoost, Is.GreaterThan(0f));
        }

        [Test]
        public void PlayerRagdollPrefab_BalanceController_ExposesStandingHipsHeightForDirector()
        {
            // Arrange
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            BalanceController balanceController = prefabRoot.GetComponent<BalanceController>();

            // Act
            float standingHeight = balanceController.StandingHipsHeight;

            // Assert
            Assert.That(prefabRoot, Is.Not.Null, "PlayerRagdoll prefab must exist.");
            Assert.That(balanceController, Is.Not.Null,
                "PlayerRagdoll prefab should include BalanceController on Hips.");
            Assert.That(standingHeight, Is.GreaterThan(0.5f).And.LessThan(2f),
                "StandingHipsHeight should be within a reasonable range for the ragdoll.");
        }

        [Test]
        public void PlayerRagdollPrefab_LocomotionDirector_ExposesIsRecoveryActive()
        {
            // Arrange
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            LocomotionDirector director = prefabRoot.GetComponent<LocomotionDirector>();

            // Assert
            Assert.That(prefabRoot, Is.Not.Null, "PlayerRagdoll prefab must exist.");
            Assert.That(director, Is.Not.Null,
                "PlayerRagdoll prefab should include LocomotionDirector for the C6.4 recovery bridge.");
            Assert.That(director.IsRecoveryActive, Is.False,
                "IsRecoveryActive should default to false when no recovery is in progress.");
        }

        [Test]
        public void PlayerRagdollPrefab_CharacterState_HasCollapseDeferralLimitField()
        {
            // Arrange
            GameObject prefabRoot = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerRagdollPrefabPath);
            CharacterState characterState = prefabRoot.GetComponent<CharacterState>();
            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;

            FieldInfo deferralLimitField = typeof(CharacterState).GetField("_collapseDeferralLimit", flags);

            // Act
            float deferralLimit = (float)deferralLimitField.GetValue(characterState);

            // Assert
            Assert.That(prefabRoot, Is.Not.Null, "PlayerRagdoll prefab must exist.");
            Assert.That(characterState, Is.Not.Null,
                "PlayerRagdoll prefab should include CharacterState on Hips.");
            Assert.That(deferralLimitField, Is.Not.Null,
                "CharacterState should serialize a collapse deferral limit for C6.4.");
            Assert.That(deferralLimit, Is.GreaterThan(0f).And.LessThanOrEqualTo(3f),
                "Collapse deferral limit should be positive and bounded for safety.");
        }
    }
}