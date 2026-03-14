using System;
using System.Reflection;
using NUnit.Framework;
using PhysicsDrivenMovement.Character;
using UnityEngine;

namespace PhysicsDrivenMovement.Tests.EditMode.Character
{
    /// <summary>
    /// EditMode tests that define the internal locomotion contract surface introduced by
    /// Chapter 1 task C1.2 of the unified locomotion roadmap.
    /// These tests use reflection so the contracts can remain internal to the Character
    /// assembly while still being validated from the test assemblies.
    /// </summary>
    [TestFixture]
    public class LocomotionContractsTests
    {
        private const string DesiredInputTypeName = "PhysicsDrivenMovement.Character.DesiredInput";
        private const string FootContactObservationTypeName = "PhysicsDrivenMovement.Character.FootContactObservation";
        private const string SupportObservationFilterTypeName = "PhysicsDrivenMovement.Character.SupportObservationFilter";
        private const string SupportGeometryTypeName = "PhysicsDrivenMovement.Character.SupportGeometry";
        private const string LocomotionSensorSnapshotTypeName = "PhysicsDrivenMovement.Character.LocomotionSensorSnapshot";
        private const string LocomotionSensorAggregatorTypeName = "PhysicsDrivenMovement.Character.LocomotionSensorAggregator";
        private const string SupportObservationTypeName = "PhysicsDrivenMovement.Character.SupportObservation";
        private const string LocomotionObservationTypeName = "PhysicsDrivenMovement.Character.LocomotionObservation";
        private const string BodySupportCommandTypeName = "PhysicsDrivenMovement.Character.BodySupportCommand";
        private const string LegStateFrameTypeName = "PhysicsDrivenMovement.Character.LegStateFrame";
        private const string LegStateMachineTypeName = "PhysicsDrivenMovement.Character.LegStateMachine";
        private const string LegStateTypeTypeName = "PhysicsDrivenMovement.Character.LegStateType";
        private const string LegStateTransitionReasonTypeName = "PhysicsDrivenMovement.Character.LegStateTransitionReason";
        private const string LegCommandOutputTypeName = "PhysicsDrivenMovement.Character.LegCommandOutput";
        private const string LocomotionLegTypeName = "PhysicsDrivenMovement.Character.LocomotionLeg";
        private const string LegCommandModeTypeName = "PhysicsDrivenMovement.Character.LegCommandMode";
        private const string StepTargetTypeName = "PhysicsDrivenMovement.Character.StepTarget";
        private const string StepPlannerTypeName = "PhysicsDrivenMovement.Character.StepPlanner";
        private const string RecoverySituationTypeName = "PhysicsDrivenMovement.Character.RecoverySituation";
        private const string RecoveryStateTypeName = "PhysicsDrivenMovement.Character.RecoveryState";
        private const string RecoveryResponseProfileTypeName = "PhysicsDrivenMovement.Character.RecoveryResponseProfile";
        private const string RecoveryTransitionGuardTypeName = "PhysicsDrivenMovement.Character.RecoveryTransitionGuard";

        private static Assembly CharacterAssembly => typeof(PlayerMovement).Assembly;

        [Test]
        public void ContractTypes_QueriedFromCharacterAssembly_AllRoadmapContractsExistAndRemainInternal()
        {
            // Arrange
            string[] typeNames =
            {
                DesiredInputTypeName,
                FootContactObservationTypeName,
                SupportObservationFilterTypeName,
                SupportGeometryTypeName,
                LocomotionSensorSnapshotTypeName,
                LocomotionSensorAggregatorTypeName,
                SupportObservationTypeName,
                LocomotionObservationTypeName,
                BodySupportCommandTypeName,
                LegStateFrameTypeName,
                LegStateMachineTypeName,
                LegStateTypeTypeName,
                LegStateTransitionReasonTypeName,
                LegCommandOutputTypeName,
                LocomotionLegTypeName,
                LegCommandModeTypeName,
                StepTargetTypeName,
                StepPlannerTypeName,
                RecoverySituationTypeName,
                RecoveryStateTypeName,
                RecoveryResponseProfileTypeName,
                RecoveryTransitionGuardTypeName,
            };

            // Act / Assert
            foreach (string typeName in typeNames)
            {
                Type contractType = CharacterAssembly.GetType(typeName);

                Assert.That(contractType, Is.Not.Null, $"Expected internal locomotion contract '{typeName}' to exist.");
                Assert.That(contractType.IsNotPublic, Is.True,
                    $"Locomotion contract '{typeName}' should remain internal to the Character assembly for C1.2.");
            }
        }

        [Test]
        public void DesiredInput_ConstructedWithRawDirections_NormalizesVectorsAndPreservesIntentFlags()
        {
            // Arrange
            Type desiredInputType = RequireType(DesiredInputTypeName);
            object desiredInput = CreateInstance(
                desiredInputType,
                new Vector2(0.8f, 0.6f),
                new Vector3(4f, 0f, 0f),
                new Vector3(0f, 0f, 7f),
                true);

            // Act
            Vector3 moveWorldDirection = GetPropertyValue<Vector3>(desiredInput, "MoveWorldDirection");
            Vector3 facingDirection = GetPropertyValue<Vector3>(desiredInput, "FacingDirection");
            float moveMagnitude = GetPropertyValue<float>(desiredInput, "MoveMagnitude");
            bool hasMoveIntent = GetPropertyValue<bool>(desiredInput, "HasMoveIntent");
            bool jumpRequested = GetPropertyValue<bool>(desiredInput, "JumpRequested");

            // Assert
            AssertVector3Equal(moveWorldDirection, Vector3.right, "MoveWorldDirection");
            AssertVector3Equal(facingDirection, Vector3.forward, "FacingDirection");
            Assert.That(moveMagnitude, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(hasMoveIntent, Is.True);
            Assert.That(jumpRequested, Is.True);
        }

        [Test]
        public void LocomotionObservation_ConstructedWithRuntimeState_ReportsPlanarSpeedAndNormalizedBasis()
        {
            // Arrange
            Type observationType = RequireType(LocomotionObservationTypeName);
            object observation = CreateInstance(
                observationType,
                CharacterStateType.Moving,
                true,
                false,
                true,
                false,
                12.5f,
                new Vector3(3f, 8f, 4f),
                new Vector3(0f, 2f, 0f),
                new Vector3(0f, 0f, 5f),
                new Vector3(0f, 3f, 0f));

            // Act
            float planarSpeed = GetPropertyValue<float>(observation, "PlanarSpeed");
            Vector3 bodyForward = GetPropertyValue<Vector3>(observation, "BodyForward");
            Vector3 bodyUp = GetPropertyValue<Vector3>(observation, "BodyUp");
            bool isLocomotionCollapsed = GetPropertyValue<bool>(observation, "IsLocomotionCollapsed");

            // Assert
            Assert.That(planarSpeed, Is.EqualTo(5f).Within(0.0001f));
            AssertVector3Equal(bodyForward, Vector3.forward, "BodyForward");
            AssertVector3Equal(bodyUp, Vector3.up, "BodyUp");
            Assert.That(isLocomotionCollapsed, Is.True);
        }

        [Test]
        public void FootContactObservation_ConstructedWithOutOfRangeSignals_ClampsConfidenceValuesIntoSchemaRange()
        {
            // Arrange
            Type footObservationType = RequireType(FootContactObservationTypeName);
            Type legType = RequireType(LocomotionLegTypeName);
            object leftLeg = Enum.Parse(legType, "Left");

            // Act
            object footObservation = CreateInstance(
                footObservationType,
                leftLeg,
                true,
                1.4f,
                -0.25f,
                2.2f);

            float contactConfidence = GetPropertyValue<float>(footObservation, "ContactConfidence");
            float plantedConfidence = GetPropertyValue<float>(footObservation, "PlantedConfidence");
            float slipEstimate = GetPropertyValue<float>(footObservation, "SlipEstimate");

            // Assert
            Assert.That(GetPropertyValue<object>(footObservation, "Leg"), Is.EqualTo(leftLeg));
            Assert.That(GetPropertyValue<bool>(footObservation, "IsGrounded"), Is.True);
            Assert.That(contactConfidence, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(plantedConfidence, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(slipEstimate, Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void SupportObservation_ConstructedWithFootStates_ExposesAggregatedSupportSignals()
        {
            // Arrange
            Type footObservationType = RequireType(FootContactObservationTypeName);
            Type supportObservationType = RequireType(SupportObservationTypeName);
            Type legType = RequireType(LocomotionLegTypeName);

            object leftFoot = CreateInstance(
                footObservationType,
                Enum.Parse(legType, "Left"),
                true,
                0.9f,
                0.75f,
                0.2f);
            object rightFoot = CreateInstance(
                footObservationType,
                Enum.Parse(legType, "Right"),
                false,
                0.15f,
                0.25f,
                0.6f);

            // Act
            object supportObservation = CreateInstance(
                supportObservationType,
                leftFoot,
                rightFoot,
                1.25f,
                0.8f,
                0.65f,
                -0.3f,
                true);

            float supportQuality = GetPropertyValue<float>(supportObservation, "SupportQuality");
            float contactConfidence = GetPropertyValue<float>(supportObservation, "ContactConfidence");
            float plantedFootConfidence = GetPropertyValue<float>(supportObservation, "PlantedFootConfidence");
            float slipEstimate = GetPropertyValue<float>(supportObservation, "SlipEstimate");
            bool isComOutsideSupport = GetPropertyValue<bool>(supportObservation, "IsComOutsideSupport");

            // Assert
            Assert.That(supportQuality, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(contactConfidence, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(plantedFootConfidence, Is.EqualTo(0.65f).Within(0.0001f));
            Assert.That(slipEstimate, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(isComOutsideSupport, Is.True);
            Assert.That(GetPropertyValue<object>(supportObservation, "LeftFoot"), Is.EqualTo(leftFoot));
            Assert.That(GetPropertyValue<object>(supportObservation, "RightFoot"), Is.EqualTo(rightFoot));
        }

        [Test]
        public void SupportGeometry_ConstructedWithTrailingFeet_ComputesSupportCenterAndOutsideClassification()
        {
            // Arrange
            Type supportGeometryType = RequireType(SupportGeometryTypeName);
            object supportGeometry = CreateInstance(
                supportGeometryType,
                new Vector3(-0.18f, 0f, -0.42f),
                true,
                new Vector3(0.18f, 0f, -0.42f),
                true);

            MethodInfo supportBehindMethod = RequireInstanceMethod(
                supportGeometryType,
                "GetSupportBehindDistance",
                typeof(Vector3),
                typeof(Vector3));
            MethodInfo outsideSupportMethod = RequireInstanceMethod(
                supportGeometryType,
                "IsPointOutsideSupport",
                typeof(Vector3));

            // Act
            Vector3 supportCenter = GetPropertyValue<Vector3>(supportGeometry, "SupportCenter");
            float supportSpan = GetPropertyValue<float>(supportGeometry, "SupportSpan");
            int groundedFootCount = GetPropertyValue<int>(supportGeometry, "GroundedFootCount");
            float supportBehindDistance = (float)supportBehindMethod.Invoke(
                supportGeometry,
                new object[] { Vector3.zero, Vector3.forward });
            bool isPointOutsideSupport = (bool)outsideSupportMethod.Invoke(
                supportGeometry,
                new object[] { Vector3.zero });

            // Assert
            AssertVector3Equal(supportCenter, new Vector3(0f, 0f, -0.42f), "SupportCenter");
            Assert.That(supportSpan, Is.EqualTo(0.36f).Within(0.0001f));
            Assert.That(groundedFootCount, Is.EqualTo(2));
            Assert.That(supportBehindDistance, Is.EqualTo(0.42f).Within(0.0001f));
            Assert.That(isPointOutsideSupport, Is.True,
                "Support geometry should classify a point ahead of a trailing support segment as outside support.");
        }

        [Test]
        public void LocomotionObservation_ConstructedWithSupportModel_ExposesWorldModelSignalsAndClampedTurnSeverity()
        {
            // Arrange
            Type footObservationType = RequireType(FootContactObservationTypeName);
            Type supportObservationType = RequireType(SupportObservationTypeName);
            Type observationType = RequireType(LocomotionObservationTypeName);
            Type legType = RequireType(LocomotionLegTypeName);

            object leftFoot = CreateInstance(
                footObservationType,
                Enum.Parse(legType, "Left"),
                true,
                0.9f,
                0.8f,
                0.1f);
            object rightFoot = CreateInstance(
                footObservationType,
                Enum.Parse(legType, "Right"),
                true,
                0.85f,
                0.7f,
                0.3f);
            object supportObservation = CreateInstance(
                supportObservationType,
                leftFoot,
                rightFoot,
                0.9f,
                0.95f,
                0.8f,
                0.25f,
                true);

            // Act
            object observation = CreateInstance(
                observationType,
                CharacterStateType.Moving,
                true,
                false,
                true,
                false,
                12.5f,
                new Vector3(3f, 8f, 4f),
                new Vector3(0f, 2f, 0f),
                new Vector3(0f, 0f, 5f),
                new Vector3(0f, 3f, 0f),
                supportObservation,
                1.4f);

            float supportQuality = GetPropertyValue<float>(observation, "SupportQuality");
            float contactConfidence = GetPropertyValue<float>(observation, "ContactConfidence");
            float plantedFootConfidence = GetPropertyValue<float>(observation, "PlantedFootConfidence");
            float slipEstimate = GetPropertyValue<float>(observation, "SlipEstimate");
            float turnSeverity = GetPropertyValue<float>(observation, "TurnSeverity");
            bool isComOutsideSupport = GetPropertyValue<bool>(observation, "IsComOutsideSupport");

            // Assert
            Assert.That(GetPropertyValue<object>(observation, "Support"), Is.EqualTo(supportObservation));
            Assert.That(supportQuality, Is.EqualTo(0.9f).Within(0.0001f));
            Assert.That(contactConfidence, Is.EqualTo(0.95f).Within(0.0001f));
            Assert.That(plantedFootConfidence, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(slipEstimate, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(turnSeverity, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(isComOutsideSupport, Is.True);
        }

        [Test]
        public void SupportObservationFilter_WhenPlantedSignalDipsInsideHysteresis_KeepsFootPlanted()
        {
            // Arrange
            Type footObservationType = RequireType(FootContactObservationTypeName);
            Type supportObservationType = RequireType(SupportObservationTypeName);
            Type filterType = RequireType(SupportObservationFilterTypeName);
            Type legType = RequireType(LocomotionLegTypeName);
            MethodInfo filterMethod = RequireInstanceMethod(filterType, "Filter", supportObservationType, typeof(float));
            object filter = CreateInstance(filterType, 20f, 30f, 12f, 20f, 0.75f, 0.55f);

            object settledSupport = CreateSupportObservation(
                footObservationType,
                supportObservationType,
                legType,
                leftPlantedConfidence: 1f,
                rightPlantedConfidence: 1f);
            object jitterSupport = CreateSupportObservation(
                footObservationType,
                supportObservationType,
                legType,
                leftPlantedConfidence: 0.62f,
                rightPlantedConfidence: 1f);

            filterMethod.Invoke(filter, new object[] { settledSupport, 0.1f });

            // Act
            object filteredSupport = filterMethod.Invoke(filter, new object[] { jitterSupport, 0.02f });
            object leftFoot = GetPropertyValue<object>(filteredSupport, "LeftFoot");
            bool isPlanted = GetPropertyValue<bool>(leftFoot, "IsPlanted");
            float plantedConfidence = GetPropertyValue<float>(leftFoot, "PlantedConfidence");

            // Assert
            Assert.That(isPlanted, Is.True,
                "A planted foot should stay planted when the confidence dip remains inside the hysteresis band.");
            Assert.That(plantedConfidence, Is.GreaterThan(0.55f),
                "The filtered planted confidence should not collapse to the unplanted state from a single in-band dip.");
        }

        [Test]
        public void SupportObservationFilter_WhenUnplantedSignalRisesInsideHysteresis_DoesNotReplantUntilEnterThreshold()
        {
            // Arrange
            Type footObservationType = RequireType(FootContactObservationTypeName);
            Type supportObservationType = RequireType(SupportObservationTypeName);
            Type filterType = RequireType(SupportObservationFilterTypeName);
            Type legType = RequireType(LocomotionLegTypeName);
            MethodInfo filterMethod = RequireInstanceMethod(filterType, "Filter", supportObservationType, typeof(float));
            object filter = CreateInstance(filterType, 20f, 30f, 12f, 20f, 0.75f, 0.55f);

            object unplantedSupport = CreateSupportObservation(
                footObservationType,
                supportObservationType,
                legType,
                leftPlantedConfidence: 0.2f,
                rightPlantedConfidence: 1f);
            object borderlineSupport = CreateSupportObservation(
                footObservationType,
                supportObservationType,
                legType,
                leftPlantedConfidence: 0.7f,
                rightPlantedConfidence: 1f);

            filterMethod.Invoke(filter, new object[] { unplantedSupport, 0.1f });

            // Act
            object filteredSupport = filterMethod.Invoke(filter, new object[] { borderlineSupport, 0.02f });
            object leftFoot = GetPropertyValue<object>(filteredSupport, "LeftFoot");
            bool isPlanted = GetPropertyValue<bool>(leftFoot, "IsPlanted");
            float plantedConfidence = GetPropertyValue<float>(leftFoot, "PlantedConfidence");

            // Assert
            Assert.That(isPlanted, Is.False,
                "An unplanted foot should not re-enter the planted state until the enter threshold is crossed.");
            Assert.That(plantedConfidence, Is.LessThan(0.75f),
                "The filtered planted confidence should stay below the replant threshold while the raw signal remains in-band.");
        }

        [Test]
        public void BodySupportCommand_PassThroughRequested_UsesWorldUpUnitScalesAndFallbackFacing()
        {
            // Arrange
            Type commandType = RequireType(BodySupportCommandTypeName);
            MethodInfo passThroughMethod = RequireStaticMethod(commandType, "PassThrough", typeof(Vector3));

            // Act
            object command = passThroughMethod.Invoke(null, new object[] { Vector3.zero });
            Vector3 facingDirection = GetPropertyValue<Vector3>(command, "FacingDirection");
            Vector3 uprightDirection = GetPropertyValue<Vector3>(command, "UprightDirection");
            float uprightStrengthScale = GetPropertyValue<float>(command, "UprightStrengthScale");
            float yawStrengthScale = GetPropertyValue<float>(command, "YawStrengthScale");
            float stabilizationStrengthScale = GetPropertyValue<float>(command, "StabilizationStrengthScale");
            float heightMaintenanceScale = GetPropertyValue<float>(command, "HeightMaintenanceScale");
            float desiredLeanDegrees = GetPropertyValue<float>(command, "DesiredLeanDegrees");

            // Assert
            AssertVector3Equal(facingDirection, Vector3.forward, "FacingDirection");
            AssertVector3Equal(uprightDirection, Vector3.up, "UprightDirection");
            Assert.That(uprightStrengthScale, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(yawStrengthScale, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(stabilizationStrengthScale, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(heightMaintenanceScale, Is.EqualTo(1f).Within(0.0001f),
                "PassThrough should default HeightMaintenanceScale to 1 for parity.");
            Assert.That(desiredLeanDegrees, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void BodySupportCommand_ConstructedWithExplicitHeightScale_ClampsAndPreservesScale()
        {
            // Arrange
            Type commandType = RequireType(BodySupportCommandTypeName);

            Type situationType = RequireType(RecoverySituationTypeName);
            object noneEnum = Enum.Parse(situationType, "None");

            // Act — construct with explicit heightMaintenanceScale = 2.5
            object boosted = CreateInstance(commandType,
                Vector3.forward,    // facingDirection
                Vector3.up,         // uprightDirection
                Vector3.forward,    // travelDirection
                0f,                 // desiredLeanDegrees
                1f,                 // uprightStrengthScale
                1f,                 // yawStrengthScale
                1f,                 // stabilizationStrengthScale
                0f,                 // recoveryBlend
                0f,                 // recoveryKdBlend
                2.5f,               // heightMaintenanceScale
                noneEnum);          // recoverySituation

            // Act — construct with negative heightMaintenanceScale (should clamp to 0)
            object clamped = CreateInstance(commandType,
                Vector3.forward, Vector3.up, Vector3.forward,
                0f, 1f, 1f, 1f, 0f, 0f,
                -1f, noneEnum);

            // Assert
            float boostedScale = GetPropertyValue<float>(boosted, "HeightMaintenanceScale");
            float clampedScale = GetPropertyValue<float>(clamped, "HeightMaintenanceScale");
            Assert.That(boostedScale, Is.EqualTo(2.5f).Within(0.0001f),
                "Explicit HeightMaintenanceScale should be preserved.");
            Assert.That(clampedScale, Is.EqualTo(0f).Within(0.0001f),
                "Negative HeightMaintenanceScale should be clamped to 0.");
        }

        [Test]
        public void BodySupportCommand_ConstructedWithLeanDegrees_PreservesLeanValue()
        {
            // Arrange
            Type commandType = RequireType(BodySupportCommandTypeName);

            Type situationType = RequireType(RecoverySituationTypeName);
            object noneEnum = Enum.Parse(situationType, "None");

            // Act — construct with nonzero desiredLeanDegrees
            object command = CreateInstance(commandType,
                Vector3.forward,    // facingDirection
                Vector3.up,         // uprightDirection
                Vector3.forward,    // travelDirection
                7.5f,               // desiredLeanDegrees
                1f,                 // uprightStrengthScale
                1f,                 // yawStrengthScale
                1f,                 // stabilizationStrengthScale
                0f,                 // recoveryBlend
                0f,                 // recoveryKdBlend
                1f,                 // heightMaintenanceScale
                noneEnum);          // recoverySituation

            // Assert
            float leanDegrees = GetPropertyValue<float>(command, "DesiredLeanDegrees");
            Assert.That(leanDegrees, Is.EqualTo(7.5f).Within(0.0001f),
                "DesiredLeanDegrees should be preserved on construction.");
        }

        [Test]
        public void LegStateFrame_ConstructedWithExplicitRole_PreservesLegStateAndTransitionReason()
        {
            // Arrange
            Type legType = RequireType(LocomotionLegTypeName);
            Type stateType = RequireType(LegStateTypeTypeName);
            Type transitionReasonType = RequireType(LegStateTransitionReasonTypeName);
            Type stateFrameType = RequireType(LegStateFrameTypeName);

            object rightLeg = Enum.Parse(legType, "Right");
            object swingState = Enum.Parse(stateType, "Swing");
            object speedUpReason = Enum.Parse(transitionReasonType, "SpeedUp");

            // Act
            object stateFrame = CreateInstance(stateFrameType, rightLeg, swingState, speedUpReason);

            // Assert
            Assert.That(GetPropertyValue<object>(stateFrame, "Leg"), Is.EqualTo(rightLeg));
            Assert.That(GetPropertyValue<object>(stateFrame, "State"), Is.EqualTo(swingState));
            Assert.That(GetPropertyValue<object>(stateFrame, "TransitionReason"), Is.EqualTo(speedUpReason));
        }

        [Test]
        public void LegStateTransitionReason_Chapter3FailureHandling_IncludesLowConfidenceFallback()
        {
            // Arrange
            Type transitionReasonType = RequireType(LegStateTransitionReasonTypeName);

            // Act
            string[] transitionReasonNames = Enum.GetNames(transitionReasonType);

            // Assert
            Assert.That(transitionReasonNames, Does.Contain("LowConfidenceFallback"),
                "Chapter 3 C3.5 should expose an explicit transition reason for low-confidence fallback so runtime logs can distinguish graceful fallback gait from normal cadence or stumble recovery.");
        }

        [Test]
        public void LegStateMachine_AdvanceMovingWhenOppositeLegStillSwinging_HoldsSupportStateAtPhaseEnd()
        {
            // Arrange
            Type legType = RequireType(LocomotionLegTypeName);
            Type footObservationType = RequireType(FootContactObservationTypeName);
            Type stateMachineType = RequireType(LegStateMachineTypeName);
            Type stateType = RequireType(LegStateTypeTypeName);
            Type transitionReasonType = RequireType(LegStateTransitionReasonTypeName);

            MethodInfo syncMethod = RequireInstanceMethod(
                stateMachineType,
                "SyncFromLegacyPhase",
                typeof(float),
                transitionReasonType);
            MethodInfo advanceMovingMethod = RequireInstanceMethod(
                stateMachineType,
                "AdvanceMoving",
                footObservationType,
                stateType,
                transitionReasonType,
                typeof(float),
                typeof(bool));

            object rightLeg = Enum.Parse(legType, "Right");
            object swingState = Enum.Parse(stateType, "Swing");
            object speedUpReason = Enum.Parse(transitionReasonType, "SpeedUp");
            object plantedRightFoot = CreateInstance(
                footObservationType,
                rightLeg,
                true,
                1f,
                1f,
                0f);
            object machine = CreateInstance(stateMachineType, rightLeg, false);

            syncMethod.Invoke(machine, new object[] { Mathf.PI * 2f - 0.02f, speedUpReason });

            // Act
            object stateFrame = advanceMovingMethod.Invoke(
                machine,
                new[] { plantedRightFoot, swingState, speedUpReason, 0.05f, false });

            // Assert
            Assert.That(GetPropertyValue<object>(stateFrame, "State"), Is.EqualTo(Enum.Parse(stateType, "Stance")),
                "A leg should remain in support when the opposite leg is still swinging and cannot hand support back yet.");
            Assert.That(GetPropertyValue<float>(machine, "CyclePhase"), Is.EqualTo(Mathf.PI * 2f).Within(0.0001f),
                "The support leg should hold at the end of stance rather than wrapping immediately into swing while the opposite leg is airborne.");
        }

        [Test]
        public void LegStateMachine_AdvanceMovingWhenSupportPhaseCompletesAndOppositeLegCanSupport_TransitionsIntoSwing()
        {
            // Arrange
            Type legType = RequireType(LocomotionLegTypeName);
            Type footObservationType = RequireType(FootContactObservationTypeName);
            Type stateMachineType = RequireType(LegStateMachineTypeName);
            Type stateType = RequireType(LegStateTypeTypeName);
            Type transitionReasonType = RequireType(LegStateTransitionReasonTypeName);

            MethodInfo syncMethod = RequireInstanceMethod(
                stateMachineType,
                "SyncFromLegacyPhase",
                typeof(float),
                transitionReasonType);
            MethodInfo advanceMovingMethod = RequireInstanceMethod(
                stateMachineType,
                "AdvanceMoving",
                footObservationType,
                stateType,
                transitionReasonType,
                typeof(float),
                typeof(bool));

            object rightLeg = Enum.Parse(legType, "Right");
            object stanceState = Enum.Parse(stateType, "Stance");
            object defaultCadenceReason = Enum.Parse(transitionReasonType, "DefaultCadence");
            object plantedRightFoot = CreateInstance(
                footObservationType,
                rightLeg,
                true,
                1f,
                1f,
                0f);
            object machine = CreateInstance(stateMachineType, rightLeg, false);

            syncMethod.Invoke(machine, new object[] { Mathf.PI * 2f - 0.02f, defaultCadenceReason });

            // Act
            object stateFrame = advanceMovingMethod.Invoke(
                machine,
                new[] { plantedRightFoot, stanceState, defaultCadenceReason, 0.05f, false });

            // Assert
            Assert.That(GetPropertyValue<object>(stateFrame, "State"), Is.EqualTo(Enum.Parse(stateType, "Swing")),
                "Once the opposite leg can support again, the controller should release the held leg back into swing instead of keeping a hard-wired mirror offset.");
            Assert.That(GetPropertyValue<float>(machine, "CyclePhase"), Is.LessThan(0.05f),
                "Entering swing should restart the leg's cycle near zero rather than preserving the previous support phase.");
        }

        [Test]
        public void LegStateMachine_AdvanceMovingWhenSwingFootRegainsGroundAfterMinimumProgress_TransitionsIntoPlant()
        {
            // Arrange
            Type legType = RequireType(LocomotionLegTypeName);
            Type footObservationType = RequireType(FootContactObservationTypeName);
            Type stateMachineType = RequireType(LegStateMachineTypeName);
            Type stateType = RequireType(LegStateTypeTypeName);
            Type transitionReasonType = RequireType(LegStateTransitionReasonTypeName);

            MethodInfo syncMethod = RequireInstanceMethod(
                stateMachineType,
                "SyncFromLegacyPhase",
                typeof(float),
                transitionReasonType);
            MethodInfo advanceMovingMethod = RequireInstanceMethod(
                stateMachineType,
                "AdvanceMoving",
                footObservationType,
                stateType,
                transitionReasonType,
                typeof(float),
                typeof(bool));

            object leftLeg = Enum.Parse(legType, "Left");
            object stanceState = Enum.Parse(stateType, "Stance");
            object defaultCadenceReason = Enum.Parse(transitionReasonType, "DefaultCadence");
            object groundedLeftFoot = CreateInstance(
                footObservationType,
                leftLeg,
                true,
                1f,
                1f,
                0f);
            object machine = CreateInstance(stateMachineType, leftLeg, true);

            syncMethod.Invoke(machine, new object[] { Mathf.PI * 0.8f, defaultCadenceReason });

            // Act
            object stateFrame = advanceMovingMethod.Invoke(
                machine,
                new[] { groundedLeftFoot, stanceState, defaultCadenceReason, 0.05f, false });

            // Assert
            Assert.That(GetPropertyValue<object>(stateFrame, "State"), Is.EqualTo(Enum.Parse(stateType, "Plant")),
                "A swing leg that re-establishes ground after meaningful forward progress should enter Plant instead of snapping straight back to stance.");
            Assert.That(GetPropertyValue<float>(machine, "CyclePhase"), Is.GreaterThan(Mathf.PI * 0.8f),
                "Touchdown should preserve forward cycle progress so the plant window can finish before stance resumes.");
        }

        [Test]
        public void LegCommandOutput_DisabledRequested_UsesDisabledModeAndZeroedExecutionPayload()
        {
            // Arrange
            Type legType = RequireType(LocomotionLegTypeName);
            Type stateType = RequireType(LegStateTypeTypeName);
            Type transitionReasonType = RequireType(LegStateTransitionReasonTypeName);
            Type commandModeType = RequireType(LegCommandModeTypeName);
            Type commandOutputType = RequireType(LegCommandOutputTypeName);
            MethodInfo disabledMethod = RequireStaticMethod(commandOutputType, "Disabled", legType);
            object leftLeg = Enum.Parse(legType, "Left");

            // Act
            object command = disabledMethod.Invoke(null, new[] { leftLeg });
            object leg = GetPropertyValue<object>(command, "Leg");
            object mode = GetPropertyValue<object>(command, "Mode");
            object state = GetPropertyValue<object>(command, "State");
            object transitionReason = GetPropertyValue<object>(command, "TransitionReason");
            float cyclePhase = GetPropertyValue<float>(command, "CyclePhase");
            float blendWeight = GetPropertyValue<float>(command, "BlendWeight");
            Vector3 footTarget = GetPropertyValue<Vector3>(command, "FootTarget");

            // Assert
            Assert.That(leg, Is.EqualTo(leftLeg));
            Assert.That(mode, Is.EqualTo(Enum.Parse(commandModeType, "Disabled")));
            Assert.That(state, Is.EqualTo(Enum.Parse(stateType, "Stance")));
            Assert.That(transitionReason, Is.EqualTo(Enum.Parse(transitionReasonType, "None")));
            Assert.That(cyclePhase, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(blendWeight, Is.EqualTo(0f).Within(0.0001f));
            AssertVector3Equal(footTarget, Vector3.zero, "FootTarget");
        }

        [Test]
        public void StepTarget_ConstructedWithValues_ClampsAndPreservesFields()
        {
            // Arrange
            Type stepTargetType = RequireType(StepTargetTypeName);
            Vector3 landing = new Vector3(1f, 0f, 3f);
            float timing = 0.5f;
            float widthBias = 1.5f;
            float brakingBias = -2f;
            float confidence = 1.5f;

            // Act
            object target = CreateInstance(stepTargetType, landing, timing, widthBias, brakingBias, confidence);

            // Assert
            Vector3 landingPos = GetPropertyValue<Vector3>(target, "LandingPosition");
            float desiredTiming = GetPropertyValue<float>(target, "DesiredTiming");
            float width = GetPropertyValue<float>(target, "WidthBias");
            float braking = GetPropertyValue<float>(target, "BrakingBias");
            float conf = GetPropertyValue<float>(target, "Confidence");
            bool isValid = GetPropertyValue<bool>(target, "IsValid");

            AssertVector3Equal(landingPos, landing, "LandingPosition");
            Assert.That(desiredTiming, Is.EqualTo(0.5f).Within(0.0001f), "DesiredTiming should be preserved when non-negative.");
            Assert.That(width, Is.EqualTo(1f).Within(0.0001f), "WidthBias should be clamped to +1.");
            Assert.That(braking, Is.EqualTo(-1f).Within(0.0001f), "BrakingBias should be clamped to -1.");
            Assert.That(conf, Is.EqualTo(1f).Within(0.0001f), "Confidence should be clamped to 1.");
            Assert.That(isValid, Is.True, "Constructed StepTarget should be valid.");
        }

        [Test]
        public void StepTarget_InvalidProperty_ReturnsInvalidMarker()
        {
            // Arrange
            Type stepTargetType = RequireType(StepTargetTypeName);
            PropertyInfo invalidProp = stepTargetType.GetProperty(
                "Invalid",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(invalidProp, Is.Not.Null, "StepTarget should expose a static Invalid property.");

            // Act
            object invalid = invalidProp.GetValue(null);

            // Assert
            bool isValid = GetPropertyValue<bool>(invalid, "IsValid");
            float confidence = GetPropertyValue<float>(invalid, "Confidence");
            Assert.That(isValid, Is.False, "Invalid StepTarget should report IsValid=false.");
            Assert.That(confidence, Is.EqualTo(0f).Within(0.0001f), "Invalid StepTarget should have zero confidence.");
        }

        [Test]
        public void StepTarget_NegativeTiming_ClampedToZero()
        {
            // Arrange
            Type stepTargetType = RequireType(StepTargetTypeName);

            // Act
            object target = CreateInstance(stepTargetType, Vector3.zero, -1f, 0f, 0f, 0.5f);

            // Assert
            float desiredTiming = GetPropertyValue<float>(target, "DesiredTiming");
            Assert.That(desiredTiming, Is.EqualTo(0f).Within(0.0001f), "Negative timing should be clamped to 0.");
        }

        [Test]
        public void LegCommandOutput_DisabledWithStepTarget_CarriesInvalidStepTarget()
        {
            // Arrange
            Type legType = RequireType(LocomotionLegTypeName);
            Type commandOutputType = RequireType(LegCommandOutputTypeName);
            Type stepTargetType = RequireType(StepTargetTypeName);
            MethodInfo disabledMethod = RequireStaticMethod(commandOutputType, "Disabled", legType);
            object leftLeg = Enum.Parse(legType, "Left");

            // Act
            object command = disabledMethod.Invoke(null, new[] { leftLeg });

            // Assert
            PropertyInfo stepTargetProp = command.GetType().GetProperty(
                "StepTarget",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(stepTargetProp, Is.Not.Null,
                "LegCommandOutput should expose a StepTarget property for Chapter 4 step planning.");
            object stepTarget = stepTargetProp.GetValue(command);
            bool isValid = GetPropertyValue<bool>(stepTarget, "IsValid");
            Assert.That(isValid, Is.False, "Disabled command should carry an invalid StepTarget.");
        }

        // ── LegCommandOutput recovery context tests (C6.5) ───────────────────

        [Test]
        public void LegCommandOutput_DisabledDefault_HasNoneRecoverySituation()
        {
            // Arrange
            Type legType = RequireType(LocomotionLegTypeName);
            Type commandOutputType = RequireType(LegCommandOutputTypeName);
            Type recoverySituationType = RequireType(RecoverySituationTypeName);
            MethodInfo disabledMethod = RequireStaticMethod(commandOutputType, "Disabled", legType);
            object leftLeg = Enum.Parse(legType, "Left");

            // Act
            object command = disabledMethod.Invoke(null, new[] { leftLeg });

            // Assert
            object situation = GetPropertyValue<object>(command, "RecoverySituation");
            float blend = GetPropertyValue<float>(command, "RecoveryBlend");
            Assert.That(situation, Is.EqualTo(Enum.Parse(recoverySituationType, "None")),
                "Disabled command should default to RecoverySituation.None.");
            Assert.That(blend, Is.EqualTo(0f).Within(0.0001f),
                "Disabled command should default to zero RecoveryBlend.");
        }

        [Test]
        public void LegCommandOutput_WithRecoveryContext_StampsSituationAndBlend()
        {
            // Arrange
            Type legType = RequireType(LocomotionLegTypeName);
            Type commandOutputType = RequireType(LegCommandOutputTypeName);
            Type recoverySituationType = RequireType(RecoverySituationTypeName);
            MethodInfo disabledMethod = RequireStaticMethod(commandOutputType, "Disabled", legType);
            object leftLeg = Enum.Parse(legType, "Left");
            object original = disabledMethod.Invoke(null, new[] { leftLeg });

            // Act — stamp recovery context onto a disabled command.
            MethodInfo withRecoveryMethod = commandOutputType.GetMethod(
                "WithRecoveryContext",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(withRecoveryMethod, Is.Not.Null,
                "LegCommandOutput should expose a WithRecoveryContext method for C6.5.");
            object stumble = Enum.Parse(recoverySituationType, "Stumble");
            object stamped = withRecoveryMethod.Invoke(original, new object[] { stumble, 0.75f });

            // Assert
            object situation = GetPropertyValue<object>(stamped, "RecoverySituation");
            float blend = GetPropertyValue<float>(stamped, "RecoveryBlend");
            Assert.That(situation, Is.EqualTo(stumble),
                "WithRecoveryContext should stamp the situation onto the copy.");
            Assert.That(blend, Is.EqualTo(0.75f).Within(0.0001f),
                "WithRecoveryContext should stamp the blend onto the copy.");
        }

        [Test]
        public void LegCommandOutput_WithRecoveryContext_PreservesOriginalFields()
        {
            // Arrange
            Type legType = RequireType(LocomotionLegTypeName);
            Type commandModeType = RequireType(LegCommandModeTypeName);
            Type commandOutputType = RequireType(LegCommandOutputTypeName);
            Type recoverySituationType = RequireType(RecoverySituationTypeName);
            MethodInfo disabledMethod = RequireStaticMethod(commandOutputType, "Disabled", legType);
            object leftLeg = Enum.Parse(legType, "Left");
            object original = disabledMethod.Invoke(null, new[] { leftLeg });

            // Act
            MethodInfo withRecoveryMethod = commandOutputType.GetMethod(
                "WithRecoveryContext",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object stumble = Enum.Parse(recoverySituationType, "Stumble");
            object stamped = withRecoveryMethod.Invoke(original, new object[] { stumble, 0.5f });

            // Assert — original fields must survive the copy.
            object leg = GetPropertyValue<object>(stamped, "Leg");
            object mode = GetPropertyValue<object>(stamped, "Mode");
            float cyclePhase = GetPropertyValue<float>(stamped, "CyclePhase");
            float blendWeight = GetPropertyValue<float>(stamped, "BlendWeight");
            Assert.That(leg, Is.EqualTo(leftLeg));
            Assert.That(mode, Is.EqualTo(Enum.Parse(commandModeType, "Disabled")));
            Assert.That(cyclePhase, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(blendWeight, Is.EqualTo(0f).Within(0.0001f));
        }

        // ── StepPlanner tests (C4.2) ──────────────────────────────────────────

        [Test]
        public void StepPlanner_ComputeSwingTarget_SwingLeg_ReturnsValidTarget()
        {
            // Arrange — a left leg in Swing state walking forward at moderate speed.
            object planner = CreateStepPlannerInstance();
            object[] args = BuildSwingTargetArgs(
                leg: "Left",
                legPhase: 0.5f,
                legState: "Swing",
                moveInput: new Vector2(0, 1),
                moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward,
                velocity: new Vector3(0, 0, 3f),
                hipsPosition: Vector3.up,
                gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f,
                supportQuality: 0.8f);

            // Act
            object result = InvokeComputeSwingTarget(planner, args);

            // Assert
            bool isValid = GetPropertyValue<bool>(result, "IsValid");
            Assert.That(isValid, Is.True, "Swing-phase left leg should produce a valid step target.");

            Vector3 landing = GetPropertyValue<Vector3>(result, "LandingPosition");
            Assert.That(landing.z, Is.GreaterThan(0.3f),
                "Landing position should be ahead of hips when walking forward.");

            float confidence = GetPropertyValue<float>(result, "Confidence");
            Assert.That(confidence, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f),
                "Confidence should be positive and in [0,1].");
        }

        [Test]
        public void StepPlanner_ComputeSwingTarget_StanceLeg_ReturnsInvalid()
        {
            // Arrange — a left leg in Stance state (weight-bearing, no planning needed).
            object planner = CreateStepPlannerInstance();
            object[] args = BuildSwingTargetArgs(
                leg: "Left",
                legPhase: Mathf.PI + 0.5f,
                legState: "Stance",
                moveInput: new Vector2(0, 1),
                moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward,
                velocity: new Vector3(0, 0, 3f),
                hipsPosition: Vector3.up,
                gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f,
                supportQuality: 0.8f);

            // Act
            object result = InvokeComputeSwingTarget(planner, args);

            // Assert
            bool isValid = GetPropertyValue<bool>(result, "IsValid");
            Assert.That(isValid, Is.False, "Stance-phase leg should not produce a step target.");
        }

        [Test]
        public void StepPlanner_ComputeSwingTarget_FasterSpeed_LongerStride()
        {
            // Arrange — compare stride at 1 m/s vs 4 m/s.
            object planner = CreateStepPlannerInstance();
            object[] slowArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 1f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f);
            object[] fastArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 4f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f);

            // Act
            object slowResult = InvokeComputeSwingTarget(planner, slowArgs);
            object fastResult = InvokeComputeSwingTarget(planner, fastArgs);

            // Assert
            Vector3 slowLanding = GetPropertyValue<Vector3>(slowResult, "LandingPosition");
            Vector3 fastLanding = GetPropertyValue<Vector3>(fastResult, "LandingPosition");
            Assert.That(fastLanding.z, Is.GreaterThan(slowLanding.z),
                "Higher speed should produce a longer forward stride.");
        }

        [Test]
        public void StepPlanner_ComputeSwingTarget_LeftVsRight_LaterallyOpposite()
        {
            // Arrange — same state but opposite legs should land on opposite sides of hips.
            object planner = CreateStepPlannerInstance();
            object[] leftArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 3f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f);
            object[] rightArgs = BuildSwingTargetArgs(
                leg: "Right", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 3f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f);

            // Act
            object leftResult = InvokeComputeSwingTarget(planner, leftArgs);
            object rightResult = InvokeComputeSwingTarget(planner, rightArgs);

            // Assert
            Vector3 leftLanding = GetPropertyValue<Vector3>(leftResult, "LandingPosition");
            Vector3 rightLanding = GetPropertyValue<Vector3>(rightResult, "LandingPosition");
            Assert.That(leftLanding.x, Is.LessThan(0f),
                "Left leg should land to the left (negative X) of hips.");
            Assert.That(rightLanding.x, Is.GreaterThan(0f),
                "Right leg should land to the right (positive X) of hips.");
        }

        [Test]
        public void StepPlanner_ComputeSwingTarget_LowSupportQuality_LowConfidence()
        {
            // Arrange — very poor support quality should reduce confidence.
            object planner = CreateStepPlannerInstance();
            object[] goodArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 3f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 1f);
            object[] poorArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 3f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.1f);

            // Act
            object goodResult = InvokeComputeSwingTarget(planner, goodArgs);
            object poorResult = InvokeComputeSwingTarget(planner, poorArgs);

            // Assert
            float goodConfidence = GetPropertyValue<float>(goodResult, "Confidence");
            float poorConfidence = GetPropertyValue<float>(poorResult, "Confidence");
            Assert.That(poorConfidence, Is.LessThan(goodConfidence),
                "Lower support quality should produce lower step confidence.");
        }

        [Test]
        public void StepPlanner_ComputeSwingTarget_CatchStep_ReturnsValidTarget()
        {
            // Arrange — a CatchStep-state leg should also produce a valid target.
            object planner = CreateStepPlannerInstance();
            object[] args = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.3f, legState: "CatchStep",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 3f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.5f);

            // Act
            object result = InvokeComputeSwingTarget(planner, args);

            // Assert
            bool isValid = GetPropertyValue<bool>(result, "IsValid");
            Assert.That(isValid, Is.True, "CatchStep leg should produce a valid step target.");
        }

        [Test]
        public void StepPlanner_ComputeSwingTarget_DesiredTiming_IsPositive()
        {
            // Arrange
            object planner = CreateStepPlannerInstance();
            object[] args = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 3f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f);

            // Act
            object result = InvokeComputeSwingTarget(planner, args);

            // Assert
            float timing = GetPropertyValue<float>(result, "DesiredTiming");
            Assert.That(timing, Is.GreaterThan(0f), "Desired timing should be positive for a mid-swing leg.");
        }

        // ── StepPlanner turn-specific tests (C4.3) ────────────────────────────

        [Test]
        public void StepPlanner_TurnSupport_OutsideLeg_LongerStrideThanDefault()
        {
            // Arrange — compare default cadence vs TurnSupport at high turn severity.
            object planner = CreateStepPlannerInstance();
            object[] defaultArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 3f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f,
                transitionReason: "DefaultCadence", turnSeverity: 0.8f);
            object[] turnSupportArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 3f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f,
                transitionReason: "TurnSupport", turnSeverity: 0.8f);

            // Act
            object defaultResult = InvokeComputeSwingTarget(planner, defaultArgs);
            object turnResult = InvokeComputeSwingTarget(planner, turnSupportArgs);

            // Assert
            Vector3 defaultLanding = GetPropertyValue<Vector3>(defaultResult, "LandingPosition");
            Vector3 turnLanding = GetPropertyValue<Vector3>(turnResult, "LandingPosition");
            Assert.That(turnLanding.z, Is.GreaterThan(defaultLanding.z),
                "Outside turn leg (TurnSupport) should have a longer stride than default cadence.");
        }

        [Test]
        public void StepPlanner_SpeedUp_InsideLeg_ShorterStrideThanDefault()
        {
            // Arrange — inside turn leg (SpeedUp with turnSeverity) should shorten stride.
            object planner = CreateStepPlannerInstance();
            object[] defaultArgs = BuildSwingTargetArgs(
                leg: "Right", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 3f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f,
                transitionReason: "DefaultCadence", turnSeverity: 0.8f);
            object[] insideArgs = BuildSwingTargetArgs(
                leg: "Right", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 3f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f,
                transitionReason: "SpeedUp", turnSeverity: 0.8f);

            // Act
            object defaultResult = InvokeComputeSwingTarget(planner, defaultArgs);
            object insideResult = InvokeComputeSwingTarget(planner, insideArgs);

            // Assert
            Vector3 defaultLanding = GetPropertyValue<Vector3>(defaultResult, "LandingPosition");
            Vector3 insideLanding = GetPropertyValue<Vector3>(insideResult, "LandingPosition");
            Assert.That(insideLanding.z, Is.LessThan(defaultLanding.z),
                "Inside turn leg (SpeedUp during turn) should have a shorter stride than default.");
        }

        [Test]
        public void StepPlanner_TurnSupport_OutsideLeg_LongerTimingThanDefault()
        {
            // Arrange — outside turn leg should get extended swing timing.
            object planner = CreateStepPlannerInstance();
            object[] defaultArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 3f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f,
                transitionReason: "DefaultCadence", turnSeverity: 0.8f);
            object[] turnArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 3f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f,
                transitionReason: "TurnSupport", turnSeverity: 0.8f);

            // Act
            object defaultResult = InvokeComputeSwingTarget(planner, defaultArgs);
            object turnResult = InvokeComputeSwingTarget(planner, turnArgs);

            // Assert
            float defaultTiming = GetPropertyValue<float>(defaultResult, "DesiredTiming");
            float turnTiming = GetPropertyValue<float>(turnResult, "DesiredTiming");
            Assert.That(turnTiming, Is.GreaterThan(defaultTiming),
                "Outside turn leg should have longer desired timing for the wider arc.");
        }

        [Test]
        public void StepPlanner_InsideLeg_LowTurnSeverity_NoStrideShorteningApplied()
        {
            // Arrange — SpeedUp with very low turn severity should not shorten stride.
            object planner = CreateStepPlannerInstance();
            object[] defaultArgs = BuildSwingTargetArgs(
                leg: "Right", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 3f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f,
                transitionReason: "DefaultCadence", turnSeverity: 0.05f);
            object[] insideArgs = BuildSwingTargetArgs(
                leg: "Right", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 3f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f,
                transitionReason: "SpeedUp", turnSeverity: 0.05f);

            // Act
            object defaultResult = InvokeComputeSwingTarget(planner, defaultArgs);
            object insideResult = InvokeComputeSwingTarget(planner, insideArgs);

            // Assert
            Vector3 defaultLanding = GetPropertyValue<Vector3>(defaultResult, "LandingPosition");
            Vector3 insideLanding = GetPropertyValue<Vector3>(insideResult, "LandingPosition");
            Assert.That(insideLanding.z, Is.EqualTo(defaultLanding.z).Within(0.001f),
                "Low turn severity should not trigger inside-leg stride shortening.");
        }

        // ── StepPlanner braking step tests (C4.4) ────────────────────────────

        [Test]
        public void StepPlanner_Braking_ShorterStrideThanDefault()
        {
            // Arrange — braking leg (no move intent, body moving) should plant closer.
            object planner = CreateStepPlannerInstance();
            object[] defaultArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 2.5f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f,
                transitionReason: "DefaultCadence", turnSeverity: 0f);
            object[] brakingArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: Vector2.zero, moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 2.5f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f,
                transitionReason: "Braking", turnSeverity: 0f);

            // Act
            object defaultResult = InvokeComputeSwingTarget(planner, defaultArgs);
            object brakingResult = InvokeComputeSwingTarget(planner, brakingArgs);

            // Assert
            Vector3 defaultLanding = GetPropertyValue<Vector3>(defaultResult, "LandingPosition");
            Vector3 brakingLanding = GetPropertyValue<Vector3>(brakingResult, "LandingPosition");
            Assert.That(brakingLanding.z, Is.LessThan(defaultLanding.z),
                "Braking leg should have a shorter stride than default cadence.");
        }

        [Test]
        public void StepPlanner_Braking_ShorterTimingThanDefault()
        {
            // Arrange — braking leg should touch down faster (shorter timing).
            object planner = CreateStepPlannerInstance();
            object[] defaultArgs = BuildSwingTargetArgs(
                leg: "Right", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 2.5f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f,
                transitionReason: "DefaultCadence", turnSeverity: 0f);
            object[] brakingArgs = BuildSwingTargetArgs(
                leg: "Right", legPhase: 0.5f, legState: "Swing",
                moveInput: Vector2.zero, moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 2.5f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f,
                transitionReason: "Braking", turnSeverity: 0f);

            // Act
            object defaultResult = InvokeComputeSwingTarget(planner, defaultArgs);
            object brakingResult = InvokeComputeSwingTarget(planner, brakingArgs);

            // Assert
            float defaultTiming = GetPropertyValue<float>(defaultResult, "DesiredTiming");
            float brakingTiming = GetPropertyValue<float>(brakingResult, "DesiredTiming");
            Assert.That(brakingTiming, Is.LessThan(defaultTiming),
                "Braking leg should have shorter desired timing for quicker plant.");
        }

        [Test]
        public void StepPlanner_Braking_HigherSpeedScalesStronger()
        {
            // Arrange — at higher residual speed, the braking shortening ratio should be
            //          stronger. Compare each braking stride against its own same-speed
            //          default-cadence baseline to isolate the braking effect from the
            //          base-stride speed scaling.
            object planner = CreateStepPlannerInstance();

            // Slow pair: DefaultCadence and Braking both at v=0.5
            object[] slowDefaultArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 0.5f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f,
                transitionReason: "DefaultCadence", turnSeverity: 0f);
            object[] slowBrakingArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: Vector2.zero, moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 0.5f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f,
                transitionReason: "Braking", turnSeverity: 0f);

            // Fast pair: DefaultCadence and Braking both at v=2.5
            object[] fastDefaultArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 2.5f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f,
                transitionReason: "DefaultCadence", turnSeverity: 0f);
            object[] fastBrakingArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: Vector2.zero, moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 2.5f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f,
                transitionReason: "Braking", turnSeverity: 0f);

            // Act
            object slowDefault = InvokeComputeSwingTarget(planner, slowDefaultArgs);
            object slowBraking = InvokeComputeSwingTarget(planner, slowBrakingArgs);
            object fastDefault = InvokeComputeSwingTarget(planner, fastDefaultArgs);
            object fastBraking = InvokeComputeSwingTarget(planner, fastBrakingArgs);

            // Assert — the braking shortening fraction at high speed should exceed the fraction at low speed.
            float slowDefaultZ = GetPropertyValue<Vector3>(slowDefault, "LandingPosition").z;
            float slowBrakingZ = GetPropertyValue<Vector3>(slowBraking, "LandingPosition").z;
            float fastDefaultZ = GetPropertyValue<Vector3>(fastDefault, "LandingPosition").z;
            float fastBrakingZ = GetPropertyValue<Vector3>(fastBraking, "LandingPosition").z;

            float slowFraction = (slowDefaultZ - slowBrakingZ) / Mathf.Max(slowDefaultZ, 0.001f);
            float fastFraction = (fastDefaultZ - fastBrakingZ) / Mathf.Max(fastDefaultZ, 0.001f);
            Assert.That(fastFraction, Is.GreaterThan(slowFraction),
                "Higher residual speed should produce a stronger braking shortening ratio.");
        }

        [Test]
        public void StepPlanner_DefaultCadence_NoBrakingAdjustment()
        {
            // Arrange — DefaultCadence at same speed should not trigger braking adjustment.
            object planner = CreateStepPlannerInstance();
            // Two identical calls with DefaultCadence to confirm no braking path activates.
            object[] args = BuildSwingTargetArgs(
                leg: "Right", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 2.5f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.8f,
                transitionReason: "DefaultCadence", turnSeverity: 0f);

            // Act
            object result = InvokeComputeSwingTarget(planner, args);

            // Assert — stride should be at the default value (no shortening).
            // Base stride = 0.45 + 2.5 * 0.12 = 0.75. Hips at y=1, z=0 → landing z = 0 + 0.75.
            Vector3 landing = GetPropertyValue<Vector3>(result, "LandingPosition");
            Assert.That(landing.z, Is.GreaterThan(0.7f),
                "DefaultCadence at 2.5 m/s should produce a full-length stride with no braking shortening.");
        }

        // ── C4.5 Catch-step planning tests ──────────────────────────────────────

        [Test]
        public void StepPlanner_CatchStep_LongerStrideThanDefaultCadence()
        {
            // Arrange — StumbleRecovery catch-step at low support quality should produce
            //           a longer stride than DefaultCadence at the same speed and quality.
            object planner = CreateStepPlannerInstance();
            float lowSupport = 0.3f;
            object[] defaultArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 2f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: lowSupport,
                transitionReason: "DefaultCadence", turnSeverity: 0f);
            object[] catchArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "CatchStep",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 2f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: lowSupport,
                transitionReason: "StumbleRecovery", turnSeverity: 0f);

            // Act
            object defaultResult = InvokeComputeSwingTarget(planner, defaultArgs);
            object catchResult = InvokeComputeSwingTarget(planner, catchArgs);

            // Assert
            float defaultZ = GetPropertyValue<Vector3>(defaultResult, "LandingPosition").z;
            float catchZ = GetPropertyValue<Vector3>(catchResult, "LandingPosition").z;
            Assert.That(catchZ, Is.GreaterThan(defaultZ),
                "Catch-step stride should extend beyond default cadence to recapture support.");
        }

        [Test]
        public void StepPlanner_CatchStep_WiderLateralThanDefaultCadence()
        {
            // Arrange — StumbleRecovery catch-step should widen the lateral offset
            //           compared to DefaultCadence at the same support quality.
            object planner = CreateStepPlannerInstance();
            float lowSupport = 0.3f;
            object[] defaultArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 2f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: lowSupport,
                transitionReason: "DefaultCadence", turnSeverity: 0f);
            object[] catchArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "CatchStep",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 2f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: lowSupport,
                transitionReason: "StumbleRecovery", turnSeverity: 0f);

            // Act
            object defaultResult = InvokeComputeSwingTarget(planner, defaultArgs);
            object catchResult = InvokeComputeSwingTarget(planner, catchArgs);

            // Assert — Left leg has negative X lateral offset; catch-step should be more negative (wider out).
            float defaultX = GetPropertyValue<Vector3>(defaultResult, "LandingPosition").x;
            float catchX = GetPropertyValue<Vector3>(catchResult, "LandingPosition").x;
            Assert.That(catchX, Is.LessThan(defaultX),
                "Left-leg catch-step should step wider (more negative X) than default cadence.");
        }

        [Test]
        public void StepPlanner_CatchStep_ShorterTimingThanDefaultCadence()
        {
            // Arrange — StumbleRecovery catch-step should shorten landing timing
            //           compared to DefaultCadence to anchor the foot sooner.
            object planner = CreateStepPlannerInstance();
            float lowSupport = 0.3f;
            object[] defaultArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "Swing",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 2f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: lowSupport,
                transitionReason: "DefaultCadence", turnSeverity: 0f);
            object[] catchArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "CatchStep",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 2f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: lowSupport,
                transitionReason: "StumbleRecovery", turnSeverity: 0f);

            // Act
            object defaultResult = InvokeComputeSwingTarget(planner, defaultArgs);
            object catchResult = InvokeComputeSwingTarget(planner, catchArgs);

            // Assert
            float defaultTiming = GetPropertyValue<float>(defaultResult, "DesiredTiming");
            float catchTiming = GetPropertyValue<float>(catchResult, "DesiredTiming");
            Assert.That(catchTiming, Is.LessThan(defaultTiming),
                "Catch-step timing should be shorter than default cadence to anchor sooner.");
        }

        [Test]
        public void StepPlanner_CatchStep_LowerSupportQualityProducesMoreAggressiveStep()
        {
            // Arrange — at lower support quality the catch-step stride and lateral
            //           adjustments should be more aggressive (wider, farther).
            object planner = CreateStepPlannerInstance();
            object[] mildArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "CatchStep",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 2f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.7f,
                transitionReason: "StumbleRecovery", turnSeverity: 0f);
            object[] severArgs = BuildSwingTargetArgs(
                leg: "Left", legPhase: 0.5f, legState: "CatchStep",
                moveInput: new Vector2(0, 1), moveWorldDirection: Vector3.forward,
                facingDirection: Vector3.forward, velocity: new Vector3(0, 0, 2f),
                hipsPosition: Vector3.up, gaitReferenceDirection: Vector3.forward,
                stepFrequency: 2f, supportQuality: 0.2f,
                transitionReason: "StumbleRecovery", turnSeverity: 0f);

            // Act
            object mildResult = InvokeComputeSwingTarget(planner, mildArgs);
            object severResult = InvokeComputeSwingTarget(planner, severArgs);

            // Assert — lower support quality → larger stride and wider lateral
            Vector3 mildLanding = GetPropertyValue<Vector3>(mildResult, "LandingPosition");
            Vector3 severLanding = GetPropertyValue<Vector3>(severResult, "LandingPosition");
            Assert.That(severLanding.z, Is.GreaterThan(mildLanding.z),
                "Lower support quality should produce a farther catch-step stride.");
            Assert.That(severLanding.x, Is.LessThan(mildLanding.x),
                "Lower support quality should produce a wider (more negative X for left leg) catch-step.");
        }

        // ── StepPlanner test helpers ────────────────────────────────────────────

        private static object CreateStepPlannerInstance()
        {
            Type plannerType = RequireType(StepPlannerTypeName);
            return Activator.CreateInstance(plannerType, nonPublic: true);
        }

        private static object[] BuildSwingTargetArgs(
            string leg,
            float legPhase,
            string legState,
            Vector2 moveInput,
            Vector3 moveWorldDirection,
            Vector3 facingDirection,
            Vector3 velocity,
            Vector3 hipsPosition,
            Vector3 gaitReferenceDirection,
            float stepFrequency,
            float supportQuality,
            string transitionReason = "DefaultCadence",
            float turnSeverity = 0f)
        {
            Type legType = RequireType(LocomotionLegTypeName);
            Type legStateTypeType = RequireType(LegStateTypeTypeName);
            Type transitionReasonType = RequireType(LegStateTransitionReasonTypeName);
            Type desiredInputType = RequireType(DesiredInputTypeName);
            Type observationType = RequireType(LocomotionObservationTypeName);
            Type footObsType = RequireType(FootContactObservationTypeName);
            Type supportObsType = RequireType(SupportObservationTypeName);

            object legEnum = Enum.Parse(legType, leg);
            object legStateEnum = Enum.Parse(legStateTypeType, legState);
            object transitionReasonEnum = Enum.Parse(transitionReasonType, transitionReason);

            object desiredInput = CreateInstance(
                desiredInputType,
                moveInput,
                moveWorldDirection,
                facingDirection,
                false);

            // Build left/right foot observations with default grounded state.
            object leftFoot = CreateInstance(
                footObsType,
                Enum.Parse(legType, "Left"),
                true,            // isGrounded
                1f,              // contactConfidence
                supportQuality,  // plantedConfidence
                1f - supportQuality); // slipEstimate
            object rightFoot = CreateInstance(
                footObsType,
                Enum.Parse(legType, "Right"),
                true,
                1f,
                supportQuality,
                1f - supportQuality);
            // Pass supportQuality directly into SupportObservation so the planner sees it.
            object support = CreateInstance(
                supportObsType,
                leftFoot,
                rightFoot,
                supportQuality,  // supportQuality — explicitly controlled
                1f,              // contactConfidence
                supportQuality,  // plantedFootConfidence
                1f - supportQuality, // slipEstimate
                false);          // isComOutsideSupport

            // Build an observation with the specified velocity and support quality.
            object observation = CreateInstance(
                observationType,
                (object)CharacterStateType.Moving,
                true,   // isGrounded
                false,  // isFallen
                false,  // isLocomotionCollapsed
                false,  // isInSnapRecovery
                0f,     // uprightAngleDegrees
                velocity,
                Vector3.zero, // angularVelocity
                Vector3.forward, // bodyForward
                Vector3.up,      // bodyUp
                support,
                turnSeverity);   // turnSeverity — C4.3 controlled

            return new object[]
            {
                legEnum,
                legPhase,
                legStateEnum,
                transitionReasonEnum,
                desiredInput,
                observation,
                hipsPosition,
                gaitReferenceDirection,
                stepFrequency,
            };
        }

        // ── C6.1: RecoverySituation enum tests ──────────────────────────

        [Test]
        public void RecoverySituation_EnumValues_ContainExpectedSituationsInPriorityOrder()
        {
            // Arrange
            Type situationType = RequireType(RecoverySituationTypeName);

            // Act
            string[] names = Enum.GetNames(situationType);
            Array values = Enum.GetValues(situationType);

            // Assert — six named situations in ascending priority order.
            Assert.That(names, Has.Length.EqualTo(6));
            Assert.That(names, Is.EquivalentTo(new[] { "None", "HardTurn", "Reversal", "Slip", "NearFall", "Stumble" }));
            // Verify ascending integer order matches declared priority.
            int previousValue = -1;
            foreach (object val in values)
            {
                int intVal = (int)val;
                Assert.That(intVal, Is.GreaterThan(previousValue),
                    $"RecoverySituation values should be in ascending priority order, but {val} ({intVal}) is not > {previousValue}.");
                previousValue = intVal;
            }
        }

        // ── C6.1: RecoveryState struct tests ─────────────────────────────

        [Test]
        public void RecoveryState_Inactive_IsNotActiveAndHasZeroBlend()
        {
            // Arrange
            Type stateType = RequireType(RecoveryStateTypeName);
            FieldInfo inactiveField = stateType.GetField("Inactive",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(inactiveField, Is.Not.Null, "RecoveryState should expose a static Inactive field.");

            // Act
            object inactive = inactiveField.GetValue(null);

            // Assert
            bool isActive = GetPropertyValue<bool>(inactive, "IsActive");
            float blend = GetPropertyValue<float>(inactive, "Blend");
            Assert.That(isActive, Is.False, "Inactive state should not be active.");
            Assert.That(blend, Is.EqualTo(0f).Within(0.001f), "Inactive state should have zero blend.");
        }

        [Test]
        public void RecoveryState_Tick_DecrementsFramesAndExpiresAtZero()
        {
            // Arrange
            Type stateType = RequireType(RecoveryStateTypeName);
            Type situationType = RequireType(RecoverySituationTypeName);
            object hardTurn = Enum.Parse(situationType, "HardTurn");

            // Create a recovery state with 2 frames remaining.
            object state = CreateInstance(stateType, hardTurn, 2, 10, 0.5f, 0.3f);

            // Act — tick once.
            MethodInfo tickMethod = RequireInstanceMethod(stateType, "Tick");
            object ticked = tickMethod.Invoke(state, null);

            // Assert — 1 frame remaining, still active.
            int framesRemaining = GetPropertyValue<int>(ticked, "FramesRemaining");
            bool isActive = GetPropertyValue<bool>(ticked, "IsActive");
            Assert.That(framesRemaining, Is.EqualTo(1));
            Assert.That(isActive, Is.True);

            // Act — tick again to 0.
            object expired = tickMethod.Invoke(ticked, null);

            // Assert — should be Inactive.
            bool expiredActive = GetPropertyValue<bool>(expired, "IsActive");
            Assert.That(expiredActive, Is.False, "RecoveryState should become inactive after ticking to 0.");
        }

        [Test]
        public void RecoveryState_Enter_HigherPriorityUpgradesCurrentSituation()
        {
            // Arrange
            Type stateType = RequireType(RecoveryStateTypeName);
            Type situationType = RequireType(RecoverySituationTypeName);
            object hardTurn = Enum.Parse(situationType, "HardTurn");
            object nearFall = Enum.Parse(situationType, "NearFall");

            // Start with a HardTurn recovery at 5 frames remaining.
            object current = CreateInstance(stateType, hardTurn, 5, 10, 0.3f, 0.4f);

            // Act — enter with a higher priority NearFall.
            MethodInfo enterMethod = stateType.GetMethod("Enter",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(enterMethod, Is.Not.Null, "RecoveryState should expose an Enter method.");
            object upgraded = enterMethod.Invoke(current, new object[] { nearFall, 15, 0.8f, 0.6f });

            // Assert — situation should be NearFall with the new duration.
            object situation = GetPropertyValue<object>(upgraded, "Situation");
            int frames = GetPropertyValue<int>(upgraded, "FramesRemaining");
            Assert.That(situation.ToString(), Is.EqualTo("NearFall"));
            Assert.That(frames, Is.EqualTo(15));
        }

        [Test]
        public void RecoveryState_Enter_LowerPriorityDoesNotDowngrade()
        {
            // Arrange
            Type stateType = RequireType(RecoveryStateTypeName);
            Type situationType = RequireType(RecoverySituationTypeName);
            object nearFall = Enum.Parse(situationType, "NearFall");
            object hardTurn = Enum.Parse(situationType, "HardTurn");

            // Start with a NearFall recovery at 8 frames remaining.
            object current = CreateInstance(stateType, nearFall, 8, 15, 0.7f, 0.5f);

            // Act — try to downgrade with a lower priority HardTurn.
            MethodInfo enterMethod = stateType.GetMethod("Enter",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object unchanged = enterMethod.Invoke(current, new object[] { hardTurn, 20, 0.3f, 0.4f });

            // Assert — should keep NearFall with original frames.
            object situation = GetPropertyValue<object>(unchanged, "Situation");
            int frames = GetPropertyValue<int>(unchanged, "FramesRemaining");
            Assert.That(situation.ToString(), Is.EqualTo("NearFall"));
            Assert.That(frames, Is.EqualTo(8));
        }

        [Test]
        public void RecoveryState_Enter_SameSituationExtendsIfLongerWindow()
        {
            // Arrange
            Type stateType = RequireType(RecoveryStateTypeName);
            Type situationType = RequireType(RecoverySituationTypeName);
            object slip = Enum.Parse(situationType, "Slip");

            // Start with a Slip recovery at 3 frames remaining out of 10.
            object current = CreateInstance(stateType, slip, 3, 10, 0.5f, 0.2f);

            // Act — re-enter same Slip with 8 frames.
            MethodInfo enterMethod = stateType.GetMethod("Enter",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            object extended = enterMethod.Invoke(current, new object[] { slip, 8, 0.6f, 0.3f });

            // Assert — should extend to 8 frames with new severity.
            int frames = GetPropertyValue<int>(extended, "FramesRemaining");
            float severity = GetPropertyValue<float>(extended, "EntrySeverity");
            Assert.That(frames, Is.EqualTo(8));
            Assert.That(severity, Is.EqualTo(0.6f).Within(0.001f));
        }

        [Test]
        public void RecoveryState_Blend_RampsCorrectlyOverDuration()
        {
            // Arrange
            Type stateType = RequireType(RecoveryStateTypeName);
            Type situationType = RequireType(RecoverySituationTypeName);
            object stumble = Enum.Parse(situationType, "Stumble");

            // Create with 10 frames remaining out of 10 total.
            object state = CreateInstance(stateType, stumble, 10, 10, 0.9f, 0.8f);

            // Act / Assert — blend should be 1.0 at start.
            float blendFull = GetPropertyValue<float>(state, "Blend");
            Assert.That(blendFull, Is.EqualTo(1f).Within(0.01f));

            // Tick down to 5 frames remaining.
            MethodInfo tickMethod = RequireInstanceMethod(stateType, "Tick");
            object halfway = state;
            for (int i = 0; i < 5; i++)
            {
                halfway = tickMethod.Invoke(halfway, null);
            }

            float blendHalf = GetPropertyValue<float>(halfway, "Blend");
            Assert.That(blendHalf, Is.EqualTo(0.5f).Within(0.01f));
        }

        // ── C6.1: BodySupportCommand RecoverySituation propagation ──────

        [Test]
        public void BodySupportCommand_RecoverySituation_DefaultsToNone()
        {
            // Arrange / Act
            Type commandType = RequireType(BodySupportCommandTypeName);

            Type situationType = RequireType(RecoverySituationTypeName);
            object noneEnum = Enum.Parse(situationType, "None");

            // Create with explicit None recovery situation (reflection requires all params).
            object command = CreateInstance(
                commandType,
                Vector3.forward,   // facingDirection
                Vector3.up,        // uprightDirection
                Vector3.forward,   // travelDirection
                0f,                // desiredLeanDegrees
                1f,                // uprightStrengthScale
                1f,                // yawStrengthScale
                1f,                // stabilizationStrengthScale
                0f,                // recoveryBlend
                0f,                // recoveryKdBlend
                1f,                // heightMaintenanceScale
                noneEnum);         // recoverySituation

            // Assert
            object situation = GetPropertyValue<object>(command, "RecoverySituation");
            Assert.That(situation.ToString(), Is.EqualTo("None"));
        }

        [Test]
        public void BodySupportCommand_RecoverySituation_PropagatesWhenSet()
        {
            // Arrange
            Type commandType = RequireType(BodySupportCommandTypeName);
            Type situationType = RequireType(RecoverySituationTypeName);
            object nearFall = Enum.Parse(situationType, "NearFall");

            // Act
            object command = CreateInstance(
                commandType,
                Vector3.forward,   // facingDirection
                Vector3.up,        // uprightDirection
                Vector3.forward,   // travelDirection
                0f,                // desiredLeanDegrees
                1f,                // uprightStrengthScale
                1f,                // yawStrengthScale
                1f,                // stabilizationStrengthScale
                0.5f,              // recoveryBlend
                0.3f,              // recoveryKdBlend
                1f,                // heightMaintenanceScale
                nearFall);         // recoverySituation

            // Assert
            object situation = GetPropertyValue<object>(command, "RecoverySituation");
            Assert.That(situation.ToString(), Is.EqualTo("NearFall"));
        }

        // --- RecoveryResponseProfile tests (C6.2) ---

        [Test]
        public void RecoveryResponseProfile_Neutral_HasIdentityMultipliers()
        {
            // Arrange
            Type profileType = RequireType(RecoveryResponseProfileTypeName);
            PropertyInfo neutralProp = profileType.GetProperty("Neutral",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(neutralProp, Is.Not.Null);

            // Act
            object neutral = neutralProp.GetValue(null);

            // Assert
            Assert.That(GetFieldValue<float>(neutral, "UprightBoostMultiplier"), Is.EqualTo(1f));
            Assert.That(GetFieldValue<float>(neutral, "MinYawStrengthScale"), Is.EqualTo(0f));
            Assert.That(GetFieldValue<float>(neutral, "StabilizationBoostMultiplier"), Is.EqualTo(1f));
            Assert.That(GetFieldValue<float>(neutral, "LeanDegreesMultiplier"), Is.EqualTo(1f));
        }

        [Test]
        public void RecoveryResponseProfile_ForNone_ReturnsNeutral()
        {
            // Arrange
            Type profileType = RequireType(RecoveryResponseProfileTypeName);
            Type situationType = RequireType(RecoverySituationTypeName);
            MethodInfo forMethod = profileType.GetMethod("For",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(forMethod, Is.Not.Null);

            // Act
            object profile = forMethod.Invoke(null, new[] { Enum.Parse(situationType, "None") });

            // Assert
            Assert.That(GetFieldValue<float>(profile, "UprightBoostMultiplier"), Is.EqualTo(1f));
            Assert.That(GetFieldValue<float>(profile, "StabilizationBoostMultiplier"), Is.EqualTo(1f));
            Assert.That(GetFieldValue<float>(profile, "LeanDegreesMultiplier"), Is.EqualTo(1f));
        }

        [Test]
        public void RecoveryResponseProfile_HigherSeverity_HasStrongerUprightBoost()
        {
            // Arrange — compare HardTurn (lowest non-None) vs Stumble (highest)
            Type profileType = RequireType(RecoveryResponseProfileTypeName);
            Type situationType = RequireType(RecoverySituationTypeName);
            MethodInfo forMethod = profileType.GetMethod("For",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            // Act
            object hardTurnProfile = forMethod.Invoke(null, new[] { Enum.Parse(situationType, "HardTurn") });
            object stumbleProfile = forMethod.Invoke(null, new[] { Enum.Parse(situationType, "Stumble") });

            // Assert
            float hardTurnUpright = GetFieldValue<float>(hardTurnProfile, "UprightBoostMultiplier");
            float stumbleUpright = GetFieldValue<float>(stumbleProfile, "UprightBoostMultiplier");
            Assert.That(stumbleUpright, Is.GreaterThan(hardTurnUpright),
                "Stumble should have a stronger upright boost multiplier than HardTurn.");
        }

        [Test]
        public void RecoveryResponseProfile_Stumble_SuppressesLeanCompletely()
        {
            // Arrange
            Type profileType = RequireType(RecoveryResponseProfileTypeName);
            Type situationType = RequireType(RecoverySituationTypeName);
            MethodInfo forMethod = profileType.GetMethod("For",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            // Act
            object stumbleProfile = forMethod.Invoke(null, new[] { Enum.Parse(situationType, "Stumble") });

            // Assert
            float leanMul = GetFieldValue<float>(stumbleProfile, "LeanDegreesMultiplier");
            Assert.That(leanMul, Is.EqualTo(0f), "Stumble recovery should fully suppress lean.");
        }

        [Test]
        public void RecoveryResponseProfile_HardTurn_IncreasesLean()
        {
            // Arrange
            Type profileType = RequireType(RecoveryResponseProfileTypeName);
            Type situationType = RequireType(RecoverySituationTypeName);
            MethodInfo forMethod = profileType.GetMethod("For",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            // Act
            object hardTurnProfile = forMethod.Invoke(null, new[] { Enum.Parse(situationType, "HardTurn") });

            // Assert
            float leanMul = GetFieldValue<float>(hardTurnProfile, "LeanDegreesMultiplier");
            Assert.That(leanMul, Is.GreaterThan(1f),
                "HardTurn recovery should amplify lean to shift COM into the turn.");
        }

        [Test]
        public void RecoveryResponseProfile_AllSituations_HaveValidMultipliers()
        {
            // Arrange
            Type profileType = RequireType(RecoveryResponseProfileTypeName);
            Type situationType = RequireType(RecoverySituationTypeName);
            MethodInfo forMethod = profileType.GetMethod("For",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            // Act / Assert
            foreach (object situationValue in Enum.GetValues(situationType))
            {
                object profile = forMethod.Invoke(null, new[] { situationValue });
                float upright = GetFieldValue<float>(profile, "UprightBoostMultiplier");
                float yaw = GetFieldValue<float>(profile, "MinYawStrengthScale");
                float stab = GetFieldValue<float>(profile, "StabilizationBoostMultiplier");
                float lean = GetFieldValue<float>(profile, "LeanDegreesMultiplier");

                Assert.That(upright, Is.GreaterThanOrEqualTo(0f),
                    $"{situationValue}: UprightBoostMultiplier must be non-negative.");
                Assert.That(yaw, Is.InRange(0f, 1f),
                    $"{situationValue}: MinYawStrengthScale must be in [0,1].");
                Assert.That(stab, Is.GreaterThanOrEqualTo(0f),
                    $"{situationValue}: StabilizationBoostMultiplier must be non-negative.");
                Assert.That(lean, Is.GreaterThanOrEqualTo(0f),
                    $"{situationValue}: LeanDegreesMultiplier must be non-negative.");
            }
        }

        private static object InvokeComputeSwingTarget(object planner, object[] args)
        {
            Type plannerType = planner.GetType();
            Type legType = RequireType(LocomotionLegTypeName);
            Type legStateTypeType = RequireType(LegStateTypeTypeName);
            Type transitionReasonType = RequireType(LegStateTransitionReasonTypeName);
            Type desiredInputType = RequireType(DesiredInputTypeName);
            Type observationType = RequireType(LocomotionObservationTypeName);

            MethodInfo method = plannerType.GetMethod(
                "ComputeSwingTarget",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: new[]
                {
                    legType,
                    typeof(float),
                    legStateTypeType,
                    transitionReasonType,
                    desiredInputType,
                    observationType,
                    typeof(Vector3),
                    typeof(Vector3),
                    typeof(float),
                },
                modifiers: null);

            Assert.That(method, Is.Not.Null,
                "StepPlanner should expose a ComputeSwingTarget method with the expected signature.");

            return method.Invoke(planner, args);
        }

        private static Type RequireType(string typeName)
        {
            Type contractType = CharacterAssembly.GetType(typeName);
            Assert.That(contractType, Is.Not.Null, $"Expected type '{typeName}' to exist in the Character assembly.");
            return contractType;
        }

        private static object CreateInstance(Type type, params object[] args)
        {
            Type[] argumentTypes = Array.ConvertAll(args, argument => argument.GetType());
            ConstructorInfo constructor = type.GetConstructor(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                argumentTypes,
                modifiers: null);

            Assert.That(constructor, Is.Not.Null,
                $"Expected type '{type.FullName}' to expose a constructor matching ({DescribeTypes(argumentTypes)}).");

            return constructor.Invoke(args);
        }

        private static void AssertVector3Equal(Vector3 actual, Vector3 expected, string propertyName)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.0001f), $"{propertyName}.x mismatch.");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.0001f), $"{propertyName}.y mismatch.");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.0001f), $"{propertyName}.z mismatch.");
        }

        private static string DescribeTypes(Type[] types)
        {
            return string.Join(", ", Array.ConvertAll(types, type => type.Name));
        }

        private static object CreateSupportObservation(
            Type footObservationType,
            Type supportObservationType,
            Type legType,
            float leftPlantedConfidence,
            float rightPlantedConfidence,
            bool leftGrounded = true,
            bool rightGrounded = true)
        {
            float leftClampedConfidence = leftGrounded ? Mathf.Clamp01(leftPlantedConfidence) : 0f;
            float rightClampedConfidence = rightGrounded ? Mathf.Clamp01(rightPlantedConfidence) : 0f;
            float leftContactConfidence = leftGrounded ? 1f : 0f;
            float rightContactConfidence = rightGrounded ? 1f : 0f;

            object leftFoot = CreateInstance(
                footObservationType,
                Enum.Parse(legType, "Left"),
                leftGrounded,
                leftContactConfidence,
                leftClampedConfidence,
                leftGrounded ? 1f - leftClampedConfidence : 0f);
            object rightFoot = CreateInstance(
                footObservationType,
                Enum.Parse(legType, "Right"),
                rightGrounded,
                rightContactConfidence,
                rightClampedConfidence,
                rightGrounded ? 1f - rightClampedConfidence : 0f);

            float supportQuality = leftGrounded && rightGrounded
                ? 1f
                : leftGrounded || rightGrounded
                    ? 0.5f
                    : 0f;
            float contactConfidence = 0.5f * (leftContactConfidence + rightContactConfidence);
            float plantedFootConfidence = Mathf.Max(leftClampedConfidence, rightClampedConfidence);
            float slipEstimate = Mathf.Max(
                leftGrounded ? 1f - leftClampedConfidence : 0f,
                rightGrounded ? 1f - rightClampedConfidence : 0f);

            return CreateInstance(
                supportObservationType,
                leftFoot,
                rightFoot,
                supportQuality,
                contactConfidence,
                plantedFootConfidence,
                slipEstimate,
                false);
        }

        private static MethodInfo RequireStaticMethod(Type type, string methodName, params Type[] argumentTypes)
        {
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: argumentTypes,
                modifiers: null);

            Assert.That(method, Is.Not.Null,
                $"Expected type '{type.FullName}' to expose static method '{methodName}'.");

            return method;
        }

        private static MethodInfo RequireInstanceMethod(Type type, string methodName, params Type[] argumentTypes)
        {
            MethodInfo method = type.GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                binder: null,
                types: argumentTypes,
                modifiers: null);

            Assert.That(method, Is.Not.Null,
                $"Expected type '{type.FullName}' to expose instance method '{methodName}'.");

            return method;
        }

        private static T GetPropertyValue<T>(object instance, string propertyName)
        {
            PropertyInfo property = instance.GetType().GetProperty(
                propertyName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(property, Is.Not.Null,
                $"Expected type '{instance.GetType().FullName}' to expose property '{propertyName}'.");

            object value = property.GetValue(instance);
            Assert.That(value, Is.Not.Null, $"Property '{propertyName}' should not be null.");
            return (T)value;
        }

        private static T GetFieldValue<T>(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(field, Is.Not.Null,
                $"Expected type '{instance.GetType().FullName}' to expose field '{fieldName}'.");

            return (T)field.GetValue(instance);
        }

        // ── C6.3 — Recovery Transition Guard Tests ──────────────────────────

        /// <summary>Creates a boxed RecoveryTransitionGuard via default(T) since it's a struct.</summary>
        private object CreateTransitionGuard()
        {
            Type guardType = RequireType(RecoveryTransitionGuardTypeName);
            return Activator.CreateInstance(guardType);
        }

        private bool InvokeGuardShouldEnter(object guard, int situationValue, int debounceFrames, int cooldownFrames)
        {
            Type guardType = guard.GetType();
            Type situationType = RequireType(RecoverySituationTypeName);
            MethodInfo shouldEnter = guardType.GetMethod("ShouldEnter",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(shouldEnter, Is.Not.Null, "RecoveryTransitionGuard must expose ShouldEnter.");

            object situationEnum = Enum.ToObject(situationType, situationValue);
            return (bool)shouldEnter.Invoke(guard, new object[] { situationEnum, debounceFrames, cooldownFrames });
        }

        private void InvokeGuardOnRecoveryExpired(object guard, int situationValue, int cooldownFrames)
        {
            Type guardType = guard.GetType();
            Type situationType = RequireType(RecoverySituationTypeName);
            MethodInfo expired = guardType.GetMethod("OnRecoveryExpired",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(expired, Is.Not.Null, "RecoveryTransitionGuard must expose OnRecoveryExpired.");

            object situationEnum = Enum.ToObject(situationType, situationValue);
            expired.Invoke(guard, new object[] { situationEnum, cooldownFrames });
        }

        private void InvokeGuardTickRampIn(object guard)
        {
            MethodInfo tick = guard.GetType().GetMethod("TickRampIn",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(tick, Is.Not.Null, "RecoveryTransitionGuard must expose TickRampIn.");
            tick.Invoke(guard, null);
        }

        private float InvokeGuardComputeRampInBlend(object guard, int rampInFrames)
        {
            MethodInfo compute = guard.GetType().GetMethod("ComputeRampInBlend",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(compute, Is.Not.Null, "RecoveryTransitionGuard must expose ComputeRampInBlend.");
            return (float)compute.Invoke(guard, new object[] { rampInFrames });
        }

        private void InvokeGuardReset(object guard)
        {
            MethodInfo reset = guard.GetType().GetMethod("Reset",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(reset, Is.Not.Null, "RecoveryTransitionGuard must expose Reset.");
            reset.Invoke(guard, null);
        }

        [Test]
        public void RecoveryTransitionGuard_SingleFrameClassification_DoesNotEnterBeforeDebounce()
        {
            // Arrange
            object guard = CreateTransitionGuard();
            int hardTurn = 1;
            int debounce = 3;
            int cooldown = 20;

            // Act — single frame only
            bool entered = InvokeGuardShouldEnter(guard, hardTurn, debounce, cooldown);

            // Assert
            Assert.That(entered, Is.False, "Debounce should prevent entry on the first frame.");
        }

        [Test]
        public void RecoveryTransitionGuard_SufficientDebounceFrames_AllowsEntry()
        {
            // Arrange
            object guard = CreateTransitionGuard();
            int hardTurn = 1;
            int debounce = 3;
            int cooldown = 20;

            // Act — advance past debounce
            bool enteredEarly = false;
            for (int i = 0; i < debounce - 1; i++)
            {
                enteredEarly |= InvokeGuardShouldEnter(guard, hardTurn, debounce, cooldown);
            }
            bool enteredOnThreshold = InvokeGuardShouldEnter(guard, hardTurn, debounce, cooldown);

            // Assert
            Assert.That(enteredEarly, Is.False, "Should not enter before debounce threshold.");
            Assert.That(enteredOnThreshold, Is.True, "Should enter once debounce threshold is reached.");
        }

        [Test]
        public void RecoveryTransitionGuard_HighPrioritySituation_DebouncesHalfAsLong()
        {
            // Arrange
            object guard = CreateTransitionGuard();
            int nearFall = 4; // NearFall — high priority
            int debounce = 4;
            int cooldown = 20;
            int effectiveDebounce = debounce / 2; // Should be 2

            // Act
            bool enteredEarly = false;
            for (int i = 0; i < effectiveDebounce - 1; i++)
            {
                enteredEarly |= InvokeGuardShouldEnter(guard, nearFall, debounce, cooldown);
            }
            bool enteredOnThreshold = InvokeGuardShouldEnter(guard, nearFall, debounce, cooldown);

            // Assert
            Assert.That(enteredEarly, Is.False);
            Assert.That(enteredOnThreshold, Is.True, "NearFall should enter at half the debounce frames.");
        }

        [Test]
        public void RecoveryTransitionGuard_ExitCooldown_BlocksSameOrLowerPriority()
        {
            // Arrange
            object guard = CreateTransitionGuard();
            int hardTurn = 1;
            int cooldown = 10;

            // Arm the cooldown
            InvokeGuardOnRecoveryExpired(guard, hardTurn, cooldown);

            // Act — try to enter HardTurn (same priority) while in cooldown
            // Fast-track past debounce by repeating
            bool entered = false;
            for (int i = 0; i < 5; i++)
            {
                entered |= InvokeGuardShouldEnter(guard, hardTurn, 1, cooldown);
            }

            // Assert
            Assert.That(entered, Is.False, "Same-priority situation should be blocked during cooldown.");
        }

        [Test]
        public void RecoveryTransitionGuard_ExitCooldown_AllowsHigherPriority()
        {
            // Arrange
            object guard = CreateTransitionGuard();
            int hardTurn = 1;
            int nearFall = 4; // Higher priority
            int cooldown = 20;

            // Arm cooldown for HardTurn
            InvokeGuardOnRecoveryExpired(guard, hardTurn, cooldown);

            // Act — try to enter NearFall (higher priority = bypasses cooldown)
            // With half debounce for NearFall: debounce=4 → effective=2
            bool entered = false;
            for (int i = 0; i < 3; i++)
            {
                entered |= InvokeGuardShouldEnter(guard, nearFall, 4, cooldown);
            }

            // Assert
            Assert.That(entered, Is.True, "Higher-priority situation should bypass exit cooldown.");
        }

        [Test]
        public void RecoveryTransitionGuard_ExitCooldown_ExpiresAfterDuration()
        {
            // Arrange
            object guard = CreateTransitionGuard();
            int hardTurn = 1;
            int cooldown = 5;

            InvokeGuardOnRecoveryExpired(guard, hardTurn, cooldown);

            // Act — tick through cooldown by calling ShouldEnter with None
            for (int i = 0; i < cooldown; i++)
            {
                InvokeGuardShouldEnter(guard, 0, 1, cooldown); // None = 0
            }

            // Now try to enter HardTurn again — debounce of 1
            bool entered = InvokeGuardShouldEnter(guard, hardTurn, 1, cooldown);

            // Assert
            Assert.That(entered, Is.True, "Should allow re-entry after cooldown expires.");
        }

        [Test]
        public void RecoveryTransitionGuard_RampInBlend_IncreasesOverFrames()
        {
            // Arrange
            object guard = CreateTransitionGuard();
            int rampInFrames = 8;

            // Act — ramp-in starts at 0
            float blendStart = InvokeGuardComputeRampInBlend(guard, rampInFrames);

            // Tick a few frames
            for (int i = 0; i < 4; i++)
            {
                InvokeGuardTickRampIn(guard);
            }
            float blendMid = InvokeGuardComputeRampInBlend(guard, rampInFrames);

            // Tick to completion
            for (int i = 0; i < 4; i++)
            {
                InvokeGuardTickRampIn(guard);
            }
            float blendEnd = InvokeGuardComputeRampInBlend(guard, rampInFrames);

            // Assert
            Assert.That(blendStart, Is.EqualTo(0f), "Ramp-in should start at 0.");
            Assert.That(blendMid, Is.EqualTo(0.5f).Within(0.01f), "Ramp-in should be 0.5 at midpoint.");
            Assert.That(blendEnd, Is.EqualTo(1f), "Ramp-in should reach 1.0 at completion.");
        }

        [Test]
        public void RecoveryTransitionGuard_Reset_ClearsSituationAndCooldown()
        {
            // Arrange
            object guard = CreateTransitionGuard();
            InvokeGuardOnRecoveryExpired(guard, 3, 20); // Arm cooldown

            // Act
            InvokeGuardReset(guard);

            // Should now be able to enter even at debounce=1
            bool entered = InvokeGuardShouldEnter(guard, 1, 1, 20); // HardTurn with debounce=1

            // Assert
            Assert.That(entered, Is.True, "Reset should clear cooldown and allow immediate entry.");
        }
    }
}