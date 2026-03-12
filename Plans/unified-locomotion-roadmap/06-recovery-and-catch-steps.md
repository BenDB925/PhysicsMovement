# Chapter 6: Turn Recovery, Stumbles, And Catch Steps

Back to parent plan: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.plan.md)

## Read this chapter when

- making hard turns, reversals, slips, or near-falls first-class situations
- defining explicit recovery strategies and timeout windows
- deciding where the collapse boundary belongs after recovery strategies fail

## Dependencies

- Read Chapter 5 first so recovery behavior is layered on top of the support-command model.
- Read Chapter 4 too when recovery steps depend on explicit foothold planning.

## Objective

Make hard locomotion cases first-class behaviors, not threshold accidents.

## Primary touchpoints

- Assets/Scripts/Character/LocomotionCollapseDetector.cs
- Assets/Scripts/Character/CharacterState.cs
- Assets/Scripts/Character/LegAnimator.cs
- New recovery strategy files under Assets/Scripts/Character/

## Work packages

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

## Verification gate

- Assets/Tests/PlayMode/Character/HardSnapRecoveryTests.cs
- Assets/Tests/PlayMode/Character/SpinRecoveryTests.cs
- Assets/Tests/PlayMode/Character/StumbleStutterRegressionTests.cs
- Assets/Tests/PlayMode/Character/GetUpReliabilityTests.cs

## Exit criteria

- Hard turns and stumble events show deterministic, test-backed recovery paths.