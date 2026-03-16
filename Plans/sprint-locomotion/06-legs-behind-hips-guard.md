# WP-6: Legs-Behind-Hips Regression Guard

## Goal
Add dedicated tests that detect legs dragging behind the hips — the most important visual regression to guard against. These tests run at both walk and sprint speed and should be added **early** (immediately after WP-1) so they protect every subsequent work package.

## Current status
- State: Not started
- Current next step: Define foot-position-relative-to-hips measurement
- Blockers: None (walk-speed tests can be added now; sprint tests need WP-1)

## Scope

### 1. Measurement: foot-to-hips lag
- Each frame during sustained locomotion, measure the signed distance of each foot along the hips' forward axis relative to the hips position: `Vector3.Dot(footPos - hipsPos, hipsForward)`.
- A healthy gait has feet alternating ahead/behind the hips. Legs "dragging behind" means **both feet** stay persistently behind the hips (negative dot product) for many consecutive frames.

### 2. Thresholds
- **Max consecutive behind-frames**: If both feet are behind the hips for more than 30 consecutive frames (0.3 s at 100 Hz), the test fails.
- **Mean foot position**: Over a 3 s window of steady locomotion, the mean foot-forward-offset should be ≈ 0 (within ±0.15 m). A strongly negative mean indicates systematic drag.

### 3. Test variants

#### Walk speed
- Guard existing walk quality — proves the current gait doesn't regress as sprint code is added.

#### Sprint speed
- Same assertions at sprint speed — proves higher force/speed doesn't push the character's body ahead of its feet.

### 4. When to add
- Walk-speed guard: immediately (can land before WP-1).
- Sprint-speed guard: as soon as WP-1 merges.
- Both guards run for every subsequent WP.

## Tests — outcome-based

### T6-1: Walk_FeetNotDraggingBehindHips
- **Setup**: Arena_01, settle 1 s, walk forward 5 s.
- **Assert**:
  - No window of 30+ consecutive frames where both feet are behind hips.
  - Mean foot-forward-offset over frames 200–500 is ≥ −0.15 m.

### T6-2: Sprint_FeetNotDraggingBehindHips
- **Setup**: Arena_01, settle 1 s, sprint forward 5 s.
- **Assert**: Same thresholds as T6-1.

### T6-3: SprintToWalk_FeetRecoverForwardPosition
- **Setup**: Sprint 3 s, release sprint, walk 2 s.
- **Assert**: During the walk phase (last 1.5 s), mean foot-forward-offset ≥ −0.10 m — feet are not permanently stuck behind after slowing down.

### T6-4: Walk_LegsAlternateAroundHips
- **Setup**: Walk forward 5 s.
- **Assert**: Over any 1 s window, the left foot is ahead of hips for at least 30% of frames and behind for at least 30% of frames (symmetric alternation, not one-sided drag).

## Decisions

## Artifacts

## Progress notes
