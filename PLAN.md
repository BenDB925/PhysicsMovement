# Unified Locomotion Roadmap (Actionable for This Codebase)

## Why This Rewrite Exists
The original roadmap has strong direction but is too broad to execute week to week.
This version keeps the same 9-chapter structure and turns each chapter into concrete work packages tied to the runtime scripts and tests that already exist in this repo.

The core diagnosis is accurate: locomotion intent is split across multiple systems.
Current conflicts are most visible around hard turns, reversals, stop-start transitions, and near-fall recovery, where:
- PlayerMovement chooses travel force and facing pressure.
- BalanceController applies posture and yaw corrections.
- LegAnimator drives gait timing.
- CharacterState and LocomotionCollapseDetector can still force late overrides.

The plan below moves the project toward one clear authority for locomotion intent while preserving existing regressions as safety rails.

## Target Runtime Authority Model
Input -> LocomotionDirector -> LegStateMachine + StepPlanner -> Actuators -> Safety Layer

1. Input says what the player wants.
2. LocomotionDirector decides what movement solution to run.
3. LegStateMachine and StepPlanner decide what each leg should do.
4. Actuators execute that decision:
   - LegAnimator executes leg targets.
   - BalanceController stabilizes body support.
   - ArmAnimator adds supportive counter motion.
5. Safety layer steps in only when the plan is failing:
   - LocomotionCollapseDetector
   - CharacterState fall/get-up transitions

## Work Rules For Every Chapter
1. Start from current behavior and record a baseline before refactoring.
2. Make authority changes in thin slices, not one large rewrite.
3. Keep feature-scoped verification green after each slice.
4. Update ARCHITECTURE.md, TASK_ROUTING.md, and .copilot-instructions.md when ownership boundaries change.
5. Run Unity tests sequentially through Tools/Run-UnityTests.ps1.

Suggested focused command template:
powershell -NoProfile -ExecutionPolicy Bypass -File "H:/Work/PhysicsDrivenMovementDemo/Tools/Run-UnityTests.ps1" -ProjectPath "H:/Work/PhysicsDrivenMovementDemo" -Platform PlayMode -TestFilter "<suite list>" -MaxAttemptsPerPlatform 2 -Unattended

---

## Chapter 1: Define The Single Voice (Authority Shift)
Objective:
Create one locomotion decision owner without changing final feel yet.

Primary touchpoints:
- Assets/Scripts/Character/PlayerMovement.cs
- Assets/Scripts/Character/BalanceController.cs
- Assets/Scripts/Character/CharacterState.cs
- Assets/Scripts/Character/LegAnimator.cs
- Assets/Scripts/Character/LocomotionCollapseDetector.cs

Work packages:
1. C1.1 Baseline lock:
   - Capture current outcome metrics from GaitOutcomeTests, HardSnapRecoveryTests, SpinRecoveryTests, MovementQualityTests.
   - Save a short baseline summary in DEBUGGING.md or a new docs section in this plan.

2. C1.2 Introduce locomotion contracts:
   - Add lightweight data contracts for DesiredInput, LocomotionObservation, BodySupportCommand, and per-leg command output.
   - Keep contracts internal to Character assembly first.

3. C1.3 Add LocomotionDirector skeleton:
   - New runtime coordinator on Hips that reads desired input plus observations and emits commands.
   - Initially run in pass-through mode so behavior stays unchanged.

4. C1.4 Rewire ownership boundaries:
   - PlayerMovement stops deciding gait intent; it only reports desired movement and jump request.
   - LegAnimator consumes explicit leg intent instead of deriving all decisions from smoothed input alone.
   - BalanceController consumes support targets from director instead of introducing independent locomotion heuristics.

5. C1.5 Safety role cleanup:
   - LocomotionCollapseDetector remains watchdog only.
   - CharacterState remains the authority for high-level state labels, but no longer produces gait strategy.

6. C1.6 Regression gate:
   - Keep behavior parity before enabling new logic paths.

Verification gate:
- Assets/Tests/PlayMode/Character/PlayerMovementTests.cs
- Assets/Tests/PlayMode/Character/CharacterStateTests.cs
- Assets/Tests/PlayMode/Character/LegAnimatorTests.cs
- Assets/Tests/PlayMode/Character/BalanceControllerTests.cs
- Assets/Tests/PlayMode/Character/BalanceControllerTurningTests.cs
- Assets/Tests/PlayMode/Character/FullStackSanityTests.cs

Exit criteria:
- One script (LocomotionDirector) can be named as locomotion intent authority.
- Existing baseline tests remain green.

---

## Chapter 2: Build A Better World Model
Objective:
Promote raw physics data into locomotion-meaningful observations.

Primary touchpoints:
- Assets/Scripts/Character/GroundSensor.cs
- Assets/Scripts/Character/BalanceController.cs
- Assets/Scripts/Character/LocomotionCollapseDetector.cs
- Assets/Scripts/Character/PlayerMovement.cs
- New observation helpers under Assets/Scripts/Character/

Work packages:
1. C2.1 Observation schema:
   - Define support quality, contact confidence, planted foot confidence, slip estimate, turn severity, and COM-outside-support indicator.

2. C2.2 Sensor aggregation:
   - Build one aggregator that collects foot contacts, hip velocity, yaw rate, and support geometry each FixedUpdate.

3. C2.3 Confidence and hysteresis:
   - Add temporal filtering so planted and unplanted does not flicker frame to frame.
   - Keep thresholds serialized and documented.

4. C2.4 Debug visibility:
   - Add optional debug draw and log path for support polygon, predicted drift direction, and active confidence values.

5. C2.5 Integrate with director:
   - Director decisions switch to observation model instead of ad hoc readings from multiple classes.

Verification gate:
- Assets/Tests/PlayMode/Character/BalanceControllerIntegrationTests.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs
- Assets/Tests/PlayMode/Character/HardSnapRecoveryTests.cs
- Assets/Tests/PlayMode/Character/StumbleStutterRegressionTests.cs

Exit criteria:
- Locomotion decisions can be explained in locomotion language, not only raw velocity or tilt.

---

## Chapter 3: Replace Cycle-Only Gait With Leg States
Objective:
Move from pure phase-offset gait to explicit per-leg state roles.

Primary touchpoints:
- Assets/Scripts/Character/LegAnimator.cs
- Assets/Scripts/Character/CharacterState.cs
- New leg state files under Assets/Scripts/Character/

Work packages:
1. C3.1 Leg state model:
   - Add explicit states: Stance, Swing, Plant, RecoveryStep, CatchStep.
   - Add per-leg transition reasons (speed-up, braking, turn support, stumble recovery).

2. C3.2 Per-leg controller:
   - Implement left and right state machines instead of symmetric phase-only mirror.

3. C3.3 Animator bridge:
   - LegAnimator executes state-driven targets and timing windows.
   - Keep current sinusoidal path as fallback during migration.

4. C3.4 State-aware asymmetry:
   - Allow outside and inside legs to diverge in sharp turns.
   - Allow recovery leg to override standard cadence.

5. C3.5 Failure handling:
   - If state machine confidence is low, degrade gracefully to stable fallback gait rather than hard snapping.

Verification gate:
- Assets/Tests/PlayMode/Character/LegAnimatorTests.cs
- Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs
- Assets/Tests/PlayMode/Character/StumbleStutterRegressionTests.cs

Exit criteria:
- At runtime, each leg has an explicit state and transition reason that can be logged.

---

## Chapter 4: Add Step Planning And Foot Placement
Objective:
Decide where each step should land based on movement goals and support needs.

Primary touchpoints:
- Assets/Scripts/Character/LegAnimator.cs
- Assets/Scripts/Character/BalanceController.cs
- New step planning files under Assets/Scripts/Character/

Work packages:
1. C4.1 Step target contract:
   - Add world-space step target data: landing position, desired timing, width bias, braking bias, and confidence.

2. C4.2 Basic planner:
   - Compute target from desired speed, heading, current COM drift, and turn severity.

3. C4.3 Turn-specific planning:
   - Differentiate inside and outside leg step length, width, and timing.

4. C4.4 Braking and reversal steps:
   - Add explicit braking step logic for stop and reversal entries.

5. C4.5 Catch-step planning:
   - When support quality drops, prioritize wider or farther catch-step instead of normal cadence.

6. C4.6 Visual debug:
   - Draw planned footholds and accepted footholds in scene debug mode.

Verification gate:
- Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs
- Assets/Tests/PlayMode/Character/LapCourseTests.cs
- Assets/Tests/PlayMode/Character/ForwardRunDiagnosticTests.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs

Exit criteria:
- Step placement is purposeful and scenario-dependent, not purely decorative swing.

---

## Chapter 5: Recast Balance As Body Support
Objective:
BalanceController supports the locomotion plan instead of competing with it.

Primary touchpoints:
- Assets/Scripts/Character/BalanceController.cs
- Assets/Scripts/Character/PlayerMovement.cs
- Assets/Scripts/Character/LocomotionCollapseDetector.cs

Work packages:
1. C5.1 Support command interface:
   - BalanceController accepts support targets from director (upright target, yaw intent, lean envelope, stabilization strength).

2. C5.2 Remove locomotion ownership from balance:
   - Eliminate independent gait and turn heuristics that conflict with director intent.

3. C5.3 COM support behavior:
   - Stabilize torso and hips relative to active support plan and planned step.

4. C5.4 Simplify override layering:
   - Keep only one clear precedence order for support commands and emergency overrides.

5. C5.5 Tuning cleanup:
   - Group and rename serialized fields by role (posture, yaw, damping, recovery assist) to reduce tuning confusion.

Verification gate:
- Assets/Tests/PlayMode/Character/BalanceControllerTests.cs
- Assets/Tests/PlayMode/Character/BalanceControllerTurningTests.cs
- Assets/Tests/PlayMode/Character/BalanceControllerIntegrationTests.cs
- Assets/Tests/PlayMode/Character/HardSnapRecoveryTests.cs

Exit criteria:
- BalanceController no longer introduces independent locomotion strategy.
- Turning and recovery remain stable under regression tests.

---

## Chapter 6: Turn Recovery, Stumbles, And Catch Steps
Objective:
Make hard locomotion cases first-class behaviors, not threshold accidents.

Primary touchpoints:
- Assets/Scripts/Character/LocomotionCollapseDetector.cs
- Assets/Scripts/Character/CharacterState.cs
- Assets/Scripts/Character/LegAnimator.cs
- New recovery strategy files under Assets/Scripts/Character/

Work packages:
1. C6.1 Situation classifier:
   - Add explicit situations: HardTurn, Reversal, Slip, NearFall, CatchStepNeeded, and Stumble.

2. C6.2 Dedicated responses:
   - Map each situation to a recovery strategy and timeout window.

3. C6.3 Recovery transitions:
   - Define clean entry and exit rules so recovery does not oscillate every few frames.

4. C6.4 Collapse boundary:
   - Keep LocomotionCollapseDetector as last resort when strategy fails, not first-line controller.

5. C6.5 Expressive outcomes:
   - Ensure visible problem-solving behavior before falling back to Fallen and GetUp.

Verification gate:
- Assets/Tests/PlayMode/Character/HardSnapRecoveryTests.cs
- Assets/Tests/PlayMode/Character/SpinRecoveryTests.cs
- Assets/Tests/PlayMode/Character/StumbleStutterRegressionTests.cs
- Assets/Tests/PlayMode/Character/GetUpReliabilityTests.cs

Exit criteria:
- Hard turns and stumble events show deterministic, test-backed recovery paths.

---

## Chapter 7: Terrain And Contact Robustness
Objective:
Keep the same locomotion logic stable on non-ideal ground and contact.

Primary touchpoints:
- Assets/Scripts/Character/GroundSensor.cs
- Assets/Scripts/Character/LegAnimator.cs
- Assets/Scripts/Editor/SceneBuilder.cs
- Assets/Scripts/Editor/ArenaBuilder.cs
- Assets/Scripts/Environment/ArenaRoom.cs
- Assets/Scenes/Arena_01.unity
- Assets/Scenes/Museum_01.unity

Work packages:
1. C7.1 Terrain scenarios:
   - Add controlled slope, step-up, step-down, uneven patches, and low-obstacle lanes to test scenes.

2. C7.2 Contact-aware planning:
   - Feed slope normal and contact confidence into step timing and landing targets.

3. C7.3 Partial contact and slip handling:
   - Detect unstable support and shift to wider and bracing step plans.

4. C7.4 Recovery on terrain:
   - Validate stumble and catch-step behavior still works on non-flat surfaces.

5. C7.5 Builder alignment:
   - If scene generation changes are required, keep editor builders and runtime metadata aligned.

Verification gate:
- Assets/Tests/PlayMode/Character/Arena01BalanceStabilityTests.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs
- Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs
- Assets/Tests/PlayMode/Character/JumpTests.cs

Exit criteria:
- Locomotion remains coherent and recoverable across terrain variants.

---

## Chapter 8: Expressive Motion And Feel
Objective:
Layer character identity only after control architecture is stable.

Primary touchpoints:
- Assets/Scripts/Character/ArmAnimator.cs
- Assets/Scripts/Character/LegAnimator.cs
- Assets/Scripts/Character/BalanceController.cs
- Optional style profile assets under Assets/ScriptableObjects/

Work packages:
1. C8.1 Style profile:
   - Define serialized style presets (heavy, athletic, scrappy) for stride, lean, sway, and recovery aggression.

2. C8.2 Pelvis and torso expression:
   - Add controlled pelvis and torso offsets driven by planned movement state.

3. C8.3 Accel and decel body language:
   - Add start, stop, and reversal posture signatures tied to locomotion situation tags.

4. C8.4 Arm-leg coordination:
   - Expand ArmAnimator contribution from phase mirror to state-aware support and expression.

5. C8.5 Protect readability:
   - Keep expressive layers bounded so they never hide true locomotion intent or destabilize physics.

Verification gate:
- Assets/Tests/PlayMode/Character/ArmAnimatorPlayModeTests.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs
- Assets/Tests/PlayMode/Character/FullStackSanityTests.cs

Exit criteria:
- Motion style is recognizable, but control reliability remains intact.

---

## Chapter 9: Validation, Debugging, And Tuning Infrastructure
Objective:
Prevent the unified system from becoming a black box.

Primary touchpoints:
- Assets/Scripts/Character/FallPoseRecorder.cs
- Assets/Scripts/Character/LapDemoRunner.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs
- Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs
- Assets/Tests/PlayMode/Character/HardSnapRecoveryTests.cs
- parse_results.ps1
- summary.ps1
- AGENT_TEST_RUNNING.md

Work packages:
1. C9.1 Scenario matrix:
   - Define named scenarios: start, stop, reversal, hard turn, stumble, terrain, and long-run fatigue.

2. C9.2 Decision telemetry:
   - Log why the director chose each step and recovery mode, not only final motion metrics.

3. C9.3 Outcome dashboards:
   - Expand script output so regressions show displacement, recovery time, fall rate, and step confidence trends.

4. C9.4 Continuous regression slices:
   - Maintain stable focused test filters per subsystem to speed iteration.

5. C9.5 Failure triage workflow:
   - When tests fail, identify decision-layer cause first, then actuator effect.

Exit criteria:
- Every major locomotion failure can be traced to a specific decision and observation snapshot.

---

## Recommended Execution Order (Concrete)
1. Chapter 1 only until authority boundaries are clean.
2. Chapters 2 and 3 in parallel slices (observations plus leg states).
3. Chapter 4 for real step planning.
4. Chapters 5 and 6 for support and recovery integration.
5. Chapter 7 terrain hardening.
6. Chapter 8 expression pass.
7. Chapter 9 runs continuously and scales with each chapter.

## First Actionable Sprint (Start Here)
1. Add LocomotionDirector in pass-through mode.
2. Add observation and command contracts without changing behavior.
3. Rewire PlayerMovement to emit desired intent only.
4. Keep LegAnimator and BalanceController as executors.
5. Prove parity with:
   - LegAnimatorTests
   - BalanceControllerTurningTests
   - HardSnapRecoveryTests
   - SpinRecoveryTests
   - MovementQualityTests

If this sprint does not preserve parity, stop and fix ownership boundaries before any feel tuning.