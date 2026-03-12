# Chapter 4: Add Step Planning And Foot Placement

Back to parent plan: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.plan.md)

## Read this chapter when

- introducing explicit step target data or foothold planning
- making turns, braking, reversals, or catch steps choose purposeful landing locations
- adding visual debug for planned and accepted footholds

## Dependencies

- Read Chapters 2 and 3 first. Step planning depends on both the observation model and explicit leg roles.

## Objective

Decide where each step should land based on movement goals and support needs.

## Primary touchpoints

- Assets/Scripts/Character/LegAnimator.cs
- Assets/Scripts/Character/BalanceController.cs
- New step planning files under Assets/Scripts/Character/

## Work packages

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

## Verification gate

- Assets/Tests/PlayMode/Character/GaitOutcomeTests.cs
- Assets/Tests/PlayMode/Character/LapCourseTests.cs
- Assets/Tests/PlayMode/Character/ForwardRunDiagnosticTests.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs

## Exit criteria

- Step placement is purposeful and scenario-dependent, not purely decorative swing.