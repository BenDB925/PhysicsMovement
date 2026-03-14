# Chapter 8: Expressive Motion And Feel

Back to parent plan: [Unified Locomotion Roadmap](../unified-locomotion-roadmap.plan.md)

## Read this chapter when

- layering recognizable style onto already-stable locomotion control
- adding pelvis, torso, arm, and stride expression tied to movement state
- building style presets that change feel without destabilizing control

## Dependencies

- Read Chapters 5 through 7 first. Expression work only starts after control and terrain behavior are stable enough to protect readability.

## Objective

Layer character identity only after control architecture is stable.

## Primary touchpoints

- Assets/Scripts/Character/ArmAnimator.cs
- Assets/Scripts/Character/LegAnimator.cs
- Assets/Scripts/Character/BalanceController.cs
- Optional style profile assets under Assets/ScriptableObjects/

## Work packages

2. C8.1 Pelvis and torso expression:
   - Add controlled pelvis and torso offsets driven by planned movement state.
3. C8.2 Accel and decel body language:
   - Add start, stop, and reversal posture signatures tied to locomotion situation tags.
4. C8.3 Arm-leg coordination:
   - Expand ArmAnimator contribution from phase mirror to state-aware support and expression.
5. C8.4 Protect readability:
   - Keep expressive layers bounded so they never hide true locomotion intent or destabilize physics.

## Verification gate

- Assets/Tests/PlayMode/Character/ArmAnimatorPlayModeTests.cs
- Assets/Tests/PlayMode/Character/MovementQualityTests.cs
- Assets/Tests/PlayMode/Character/FullStackSanityTests.cs

## Exit criteria

- Motion style is recognizable, but control reliability remains intact.