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
                2.5f);              // heightMaintenanceScale

            // Act — construct with negative heightMaintenanceScale (should clamp to 0)
            object clamped = CreateInstance(commandType,
                Vector3.forward, Vector3.up, Vector3.forward,
                0f, 1f, 1f, 1f, 0f, 0f,
                -1f);

            // Assert
            float boostedScale = GetPropertyValue<float>(boosted, "HeightMaintenanceScale");
            float clampedScale = GetPropertyValue<float>(clamped, "HeightMaintenanceScale");
            Assert.That(boostedScale, Is.EqualTo(2.5f).Within(0.0001f),
                "Explicit HeightMaintenanceScale should be preserved.");
            Assert.That(clampedScale, Is.EqualTo(0f).Within(0.0001f),
                "Negative HeightMaintenanceScale should be clamped to 0.");
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
    }
}