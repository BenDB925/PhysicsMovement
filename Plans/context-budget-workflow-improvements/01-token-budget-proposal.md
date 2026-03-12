# Token Budget Proposal

## Goal

Reduce routine Copilot context usage in this repo without introducing an external context service, embeddings pipeline, or retrieval stack.

## Current status
- State: Complete
- Completed rollout slices: Slice 1 layered summary headers, Slice 2 context-budget routing rules, Slice 3 compact Unity test digests, Slice 4 completion-time session digests.
- Current next step: Observe adoption and only tighten prompt matching if real pause or handoff language slips past the completion-review hook.
- Blockers: None.

## Why this proposal exists

This repo already uses the strongest part of the OpenViking philosophy: context is organized as files, instructions, plans, and artifacts rather than hidden behind a black-box vector store. The largest remaining waste is not missing retrieval technology. It is that agents still often read full long-form docs, raw XML, and large logs when a smaller, deterministic summary would have been enough.

The proposal below keeps the current workflow and aims for lower token use per task by making the cheap context path explicit.

## Proposal summary

Adopt four workflow changes in order:

1. Add layered summary headers to the heavy docs that agents open most often.
2. Make routing more exclusionary so agents read the minimum viable set of files first.
3. Generate compact test-result digests so agents stop rehydrating from raw XML and logs.
4. Extend completion-time documentation review into a tiny session digest workflow.

## Recommended rollout

### Slice 1: Layered summary headers on long-lived docs

#### What to change

Add two short sections near the top of the largest always-relevant docs:

- `Quick Load`: 3-6 bullets that give the minimum useful mental model.
- `Read More When`: bullets that tell the agent when it must continue deeper.

Recommended initial target files:

- `ARCHITECTURE.md`
- `DEBUGGING.md`
- `LOCOMOTION_BASELINES.md`
- `AGENT_TEST_RUNNING.md`
- `Plans/unified-locomotion-roadmap/01-single-voice.md`
- `Plans/unified-locomotion-roadmap/02-world-model.md`
- `Plans/unified-locomotion-roadmap/03-leg-states.md`

#### Why it saves tokens

These are the files agents are repeatedly told to read early. Right now, each one starts with useful content, but not with a short load-bearing summary that lets the agent stop early. Adding a top summary lets most tasks pay only for the first screen instead of the whole file.

#### Suggested format

```md
## Quick Load
- Character runtime authority currently flows: Input -> LocomotionDirector -> executors.
- Known Chapter 1 pre-existing reds: `WalkStraight_NoFalls`, `SustainedLocomotionCollapse_TransitionsIntoFallen`.
- For runtime ownership changes, verify with `LocomotionContractsTests`, `LocomotionDirectorEditModeTests`, and the focused PlayMode slice.

## Read More When
- Continue into the full architecture diagram if the task adds a new system or assembly.
- Continue into the detailed class sections if the task changes public responsibilities or collaborators.
```

#### Expected win

High. This lowers startup cost on nearly every movement or debugging task.

#### Risk

Low. This is documentation-only and does not alter runtime behavior.

### Slice 2: Context-budget routing rules

#### What to change

Add a small `Context Budget Rules` section to:

- `.copilot-instructions.md`
- `TASK_ROUTING.md`
- optionally `CODING_STANDARDS.md`

The rules should say:

- Read summary layers first when present.
- Prefer the parent plan plus the freshest artifact summary before opening raw logs.
- Do not read `LOCOMOTION_BASELINES.md` end to end unless the task is about regression comparison or known reds.
- Do not read full roadmap chapters unless the task matches that chapter's scope.
- For test work, read the compact test summary first, then XML, then the full Unity log only if the summary is insufficient.

#### Why it saves tokens

The current routing tells agents what to read, but not strongly enough what to skip. OpenViking's useful idea here is directory drill-down: get the smallest relevant context first, then continue only if needed. We can apply that deterministically with docs rather than semantic retrieval.

#### Expected win

Medium to high. This reduces unnecessary broad reads and makes agent behavior more predictable.

#### Risk

Low. This is an instruction and routing cleanup.

### Slice 3: Compact Unity test digests

#### What to change

Add a repo-local summary step after focused or full Unity test runs.

Recommended new artifact:

- `TestResults/latest-summary.md`

Recommended producer:

- A new script such as `Tools/Write-TestSummary.ps1`, or an extension of the existing `summary.ps1` / `parse_results.ps1` workflow.

Recommended digest fields:

- platform and timestamp
- total, passed, failed, skipped
- failed test names only
- one-line failure reason snippets
- whether failures are new, known pre-existing, or suspected order-sensitive
- freshest supporting log path

Recommended documentation updates:

- `AGENT_TEST_RUNNING.md`
- `TASK_ROUTING.md`

#### Why it saves tokens

Raw NUnit XML and Unity logs are some of the most expensive artifacts agents read. A compact digest avoids reopening the same large files repeatedly during a debugging loop.

#### Expected win

Very high for testing and debugging tasks.

#### Risk

Low to medium. The script logic needs to stay simple and trustworthy.

#### Suggested output shape

```md
# Latest Test Summary

- Platform: PlayMode
- Timestamp: 2026-03-12T15:24:10
- Result: 183 passed, 5 failed, 12 skipped
- Known pre-existing reds: `WalkStraight_NoFalls`, `SustainedLocomotionCollapse_TransitionsIntoFallen`
- Suspected order-sensitive reds: `HardSnap90_AtFullSpeed_CharacterRecoversAndMakesProgress`, `SpinRecoveryTests.AfterFullSpinThenForwardInput_DisplacementRecoveredWithin2s`
- Fresh log: `Logs/test_playmode_20260312_152410.log`
```

### Slice 4: Completion-time session digests

#### What to change

Keep the existing completion-review hook, but extend the workflow so the agent leaves behind a tiny durable summary at task end.

Current hook files:

- `.github/hooks/task-completion-doc-review.json`
- `.github/hooks/task-completion-doc-review.ps1`

Recommended behavioral change:

- The hook should continue reminding the agent to review docs before stopping.
- The docs workflow should explicitly require a short `Quick Resume` summary in the active parent plan when the task is complete or paused.

Recommended template additions in `Plans/README.md`:

- `Quick Resume`: one paragraph or 3 bullets with current truth.
- `Verified Artifacts`: short list of the one or two files worth opening first.

#### Why it saves tokens

This reduces dependence on chat history. A future agent can resume from a 10-line digest instead of replaying the whole previous conversation.

#### Expected win

High for multi-turn or multi-session work.

#### Risk

Low. This mainly changes templates and completion discipline.

## Proposed exact file touch order

If implemented, the lowest-risk order is:

1. `TASK_ROUTING.md`
2. `.copilot-instructions.md`
3. `ARCHITECTURE.md`
4. `DEBUGGING.md`
5. `LOCOMOTION_BASELINES.md`
6. `AGENT_TEST_RUNNING.md`
7. `Plans/README.md`
8. `.github/hooks/task-completion-doc-review.ps1`
9. `Tools/Write-TestSummary.ps1` or the existing summary scripts

## Recommended first implementation slice

Implement only Slices 1 and 2 first.

Why:

- They give immediate token savings.
- They are doc-only and low risk.
- They create the structure that later test-digest and session-digest work can plug into.

## Not recommended right now

Do not add an external context database or embedding-backed retrieval service yet.

Reasons:

- The repo already has a strong deterministic file-based context model.
- The main pain is over-reading, not lack of storage.
- Operating a new service would add complexity before the cheaper fixes are exhausted.

## Success criteria

Treat the workflow change as successful if, after implementation:

- agents usually read only the top section of the heavy docs during task startup
- test/debug tasks can understand the latest run from a compact digest without opening raw XML first
- parent plans become sufficient to resume paused work without reopening old chat transcripts
- routing docs begin to explicitly prevent unnecessary reads instead of only listing candidate files

## Artifacts

- `TASK_ROUTING.md`: current deterministic entrypoint that should gain exclusionary context-budget rules.
- `.copilot-instructions.md`: repo-wide agent rules that should gain summary-first reading behavior.
- `Plans/README.md`: now defines the parent-plan `Quick Resume` and `Verified Artifacts` digest sections for pause and completion handoffs.
- `.github/hooks/task-completion-doc-review.ps1`: current completion review hook, good foundation for session digests.
- `ARCHITECTURE.md`: heavy startup doc and a prime candidate for `Quick Load` / `Read More When` sections.
- `DEBUGGING.md`: heavy reusable workflow doc and a prime candidate for a short summary layer.
- `LOCOMOTION_BASELINES.md`: high-value but expensive artifact history that should become summary-first.
- `AGENT_TEST_RUNNING.md`: should point agents to compact result digests before raw XML and logs.