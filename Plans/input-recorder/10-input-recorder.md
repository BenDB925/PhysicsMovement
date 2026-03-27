# Plan 10 — Input Recorder & Playback Tests

## Goal

Replace the failing GhostDriver-steered navigation tests with deterministic input playback.
Benny records a real play-through once; tests replay it frame-for-frame via `SetMoveInputForTest`.
Physics-deterministic: same inputs → same outcome every time. No steering algorithm to maintain.

---

## How it works

1. **InputRecorder** — an in-editor MonoBehaviour that captures `moveInput` + `jumpInput` per
   FixedUpdate frame while you play normally. Saves to a JSON file.
2. **InputPlayback** — a test utility that loads a recording and feeds it through
   `SetMoveInputForTest` / `SetJumpInputForTest` frame-by-frame.
3. **Tests** — assert that playback finishes without entering Fallen state (and optionally
   within a frame budget).

---

## Branch

Create branch `plan/10-input-recorder` from `master`.

---

## Stage 1 — InputRecorder MonoBehaviour

### File: `Assets/Scripts/Debug/InputRecorder.cs`

```csharp
// InputRecorder: attach to the player GameObject in the Editor.
// Press Record button in the Inspector (or set _recording = true in Awake for auto-start).
// On Stop, writes a JSON file to Assets/Tests/PlayMode/Recordings/<_recordingName>.json
```

Fields:
- `[SerializeField] private string _recordingName = "recording"` — output filename (no extension)
- `[SerializeField] private bool _autoStart = false` — start recording immediately on play

The component reads input each `FixedUpdate` via Unity's Input System:
- `moveInput: Vector2` — from the Player/Move action
- `jumpInput: bool` — from the Player/Jump action  
- `sprintInput: bool` — always false (auto-sprint handles this now)

Each frame appends one entry. On Stop (or `OnApplicationQuit`), serializes to:
`Assets/Tests/PlayMode/Recordings/<_recordingName>.json`

Format:
```json
{
  "fixedDeltaTime": 0.01,
  "frames": [
    { "move": [0.0, 1.0], "jump": false },
    { "move": [0.0, 1.0], "jump": false },
    ...
  ]
}
```

Add a simple Editor Inspector button (`[ContextMenu("Stop Recording")]`) to trigger save.

### File: `Assets/Scripts/Debug/InputRecorder.cs.meta`
Unity will generate this automatically.

---

## Stage 2 — InputPlayback test utility

### File: `Assets/Tests/PlayMode/Utilities/InputPlayback.cs`

```csharp
public class InputPlayback
{
    public int FrameCount { get; }
    public float RecordedFixedDeltaTime { get; }

    public static InputPlayback Load(string recordingName)
    // Loads from Assets/Tests/PlayMode/Recordings/<recordingName>.json

    public void ApplyFrame(int frameIndex, PlayerMovement movement)
    // Calls SetMoveInputForTest + SetJumpInputForTest for the given frame index
}
```

Use `System.IO.File.ReadAllText` to load. Use `JsonUtility` to deserialize.
Path: `Application.dataPath + "/Tests/PlayMode/Recordings/" + recordingName + ".json"`

---

## Stage 3 — Recordings directory + placeholder

### File: `Assets/Tests/PlayMode/Recordings/.gitkeep`

Create this directory and add a `.gitkeep` so git tracks it.
Actual recording JSON files will be added by Benny after recording.

---

## Stage 4 — Replace MovementQualityTests

### File: `Assets/Tests/PlayMode/Character/MovementQualityTests.cs`

Replace `WalkStraight_NoFalls` and `TurnAndWalk_CornerRecovery` with two new tests:

#### `WalkStraight_RecordedPlayback_NoFalls`

```
Load recording "walk-straight"
If recording file doesn't exist: skip test with Assert.Ignore("No walk-straight recording yet.")
Warm up 30 frames
Play back all frames via InputPlayback.ApplyFrame
Assert: character never entered Fallen state during playback
Assert: playback completed all frames (no timeout)
```

#### `TurnAndWalk_RecordedPlayback_NoFalls`

```
Load recording "turn-and-walk"  
If recording file doesn't exist: skip test with Assert.Ignore("No turn-and-walk recording yet.")
Warm up 30 frames
Play back all frames
Assert: character never entered Fallen state during playback
```

Keep `LapCourseTests.cs` separate — it uses `CompleteLap` with the full 24-gate course.
Replace `CompleteLap_WithinTimeLimit_NoFalls` the same way: load recording "complete-lap",
skip gracefully if no recording exists.

Keep all existing test infrastructure (`SpawnAndConfigurePlayer`, `SetUp`/`TearDown`, etc).
Remove `GhostDriver` usage from these tests — it's no longer needed for navigation.
Remove `WaypointCourseRunner` usage from these tests.

**Do NOT delete** `GhostDriver.cs` or `WaypointCourseRunner.cs` — they may be useful later.

---

## Stage 5 — Update Write-TestSummary.ps1

Remove `WalkStraight_NoFalls` and `TurnAndWalk_CornerRecovery` from
`$knownPreExistingPatterns` — they're replaced by new tests that skip gracefully
when no recording exists.

Keep `LapCourseTests.CompleteLap_WithinTimeLimit_NoFalls` — that test is also being
replaced but keep it classified until the new recording test exists.

---

## What NOT to change

- Do NOT modify `Run-UnityTests.ps1`
- Do NOT delete `GhostDriver.cs` or `WaypointCourseRunner.cs`
- Do NOT touch any other passing test files
- Do NOT try to create the recording JSON files — those come from Benny recording

---

## Commits

1. `feat(10): add InputRecorder MonoBehaviour for capturing playthrough inputs`
2. `feat(10): add InputPlayback test utility`
3. `test(10): replace GhostDriver navigation tests with recorded playback tests`
4. `chore(10): update Write-TestSummary, add Recordings directory`

---

## On completion

Write `agent-slice-status.json`:
```json
{"slice":"10-input-recorder","status":"pass","branch":"plan/10-input-recorder","notes":"InputRecorder + playback tests in place; recordings pending Benny's play-through"}
```

Send Telegram:
```powershell
$body = @{ chat_id = "8630971080"; text = "[plan-10] Done. InputRecorder ready to use. Attach to player, press play, walk the course, stop — recording saved. Then re-run tests." } | ConvertTo-Json
Invoke-RestMethod -Uri "https://api.telegram.org/bot8733821405:AAHgYbzmTD7SiIrdkh_rVs7ujTxQtKdfpDg/sendMessage" -Method Post -Body $body -ContentType "application/json"
```
