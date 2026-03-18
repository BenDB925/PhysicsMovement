# Document Surface Audit

## Goal

Decide which documents are genuinely useful as durable context, which ones should be thinned into entry points or tables of contents, and which ones should be archived or demoted so they stop consuming routine agent attention.

## Current status
- State: Proposed
- Current next step: Use this audit to drive a focused documentation cleanup instead of broad deletion.
- Blockers: The current workflow still treats several heavyweight docs as mandatory first reads, which magnifies the cost of otherwise reasonable documentation.

## Core conclusion

The repo does **not** primarily suffer from having too many documents.

The bigger problem is that too many heavyweight documents are treated as default entry points. That creates avoidable context tax even when the underlying files are useful.

The right shape is:

- keep a small set of thin entry-point docs
- move detailed procedures into skills, appendices, or narrowly scoped references
- move execution history and experiment logs into plan child docs, bug sheets, or GitHub issues
- archive or delete completed implementation plans once their durable outcomes are promoted

## Decision rules

### Keep as thin entry points
These are worth keeping, but should stay short and navigational.

- `TASK_ROUTING.md`
- `PLAN.md`
- `.copilot-instructions.md`
- `Plans/README.md`
- `.github/instructions/*.instructions.md`

### Keep as durable references, but slim
These should remain in-repo because they hold stable knowledge, but they should stop carrying low-frequency detail.

- `CODING_STANDARDS.md`
- `ARCHITECTURE.md`
- `AGENT_TEST_RUNNING.md`
- `DEBUGGING.md`
- `LOCOMOTION_BASELINES.md`

### Archive, split, or demote
These are useful as records, but they should not stay in the main attention path once complete or once their durable outcomes have been promoted.

- `AGENT_MOVEMENT_MIGRATION_PLAN.md`
- completed or oversized plan docs under `Plans/`
- long baseline history inside `LOCOMOTION_BASELINES.md`
- completed implementation-planning docs such as `Plans/sprint-jump-stability-tests.md`

## Recommended actions by file

### Keep as-is or nearly as-is

#### `TASK_ROUTING.md`
- Keep.
- Current size is good and it already acts like a true fast-start index.
- This is one of the highest-value documents in the repo.

#### `PLAN.md`
- Keep.
- It is already a thin roadmap index and does not need major work.

#### `CONCEPT.md`
- Keep, but keep it optional.
- It is useful for environment, game-loop, and direction-setting work, but should not be part of any default reading path for routine locomotion or debugging tasks.

#### `Plans/README.md`
- Keep.
- This is the canonical task-record contract and should remain a durable reference.
- Minor trimming is optional, but it is not a priority problem.

### Slim aggressively

#### `.copilot-instructions.md`
- Keep, but shrink aggressively.
- Current issue: it duplicates `ARCHITECTURE.md`, `TASK_ROUTING.md`, `CODING_STANDARDS.md`, roadmap status, and test guidance.
- Recommended target: around 60-90 lines.
- Keep only:
  - one-paragraph repo summary
  - context-budget rules
  - reading order logic
  - repo-specific guardrails not stated better elsewhere
- Remove or demote:
  - long systems tables that duplicate `ARCHITECTURE.md`
  - architecture snapshot diagram duplicated elsewhere
  - large phase tracker / status inventory
  - detailed workflow text already owned by `CODING_STANDARDS.md`

#### `CODING_STANDARDS.md`
- Keep, but slim from a full tutorial into a rulebook.
- Current issue: it mixes mandatory rules, workflow, testing philosophy, style guide, and explanatory material.
- Recommended target: around 150-220 lines.
- Keep only:
  - A-F workflow
  - self-review checklist
  - naming/style rules that actually change behavior
  - class-size and architecture guardrails
- Move or demote:
  - long test-run instructions already owned by `AGENT_TEST_RUNNING.md`
  - extended explanatory text that belongs in skills
  - broad tutorial content that is not needed on every task

#### `AGENT_TEST_RUNNING.md`
- Keep, but slim heavily.
- Current issue: the repo-primary path is simple, but the document spends many lines on raw Unity CLI fallback and executable lookup.
- Recommended target: around 100-140 lines.
- Keep only:
  - primary `Tools/Run-UnityTests.ps1` usage
  - focused verification examples
  - exit-code meaning
  - artifact interpretation
  - a short troubleshooting section
- Move or demote:
  - long raw Unity CLI walkthroughs
  - detailed Unity executable discovery instructions
  - secondary fallback recipes that almost never matter

#### `DEBUGGING.md`
- Keep, but consider trimming slightly and aligning it with the debugging skill.
- Current size is acceptable, but much of its repeated process language can move into the skill while the file stays as a compact playbook and pattern index.
- Recommended target: around 100-130 lines.

#### `LOCOMOTION_BASELINES.md`
- Keep only as a current baseline index, not a history dump.
- Current issue: historical baseline sections accumulate and become artifact archaeology.
- Recommended target: under 80-100 lines.
- Keep only:
  - current baseline reference
  - known active reds
  - artifact links
- Move or demote:
  - old chapter snapshots
  - long metric tables for completed chapters
  - historical comparison prose once the chapter is complete

### Review, but do not treat as default reads

#### `ARCHITECTURE.md`
- Keep.
- It is the correct home for subsystem boundaries and ownership.
- The better fix is not deleting architecture detail, but removing its duplication from `.copilot-instructions.md` and other documents.
- Optional improvement: split rarely used detail into appendix sections or narrower architecture references if the file continues growing.

### Archive or demote out of the main surface

#### `AGENT_MOVEMENT_MIGRATION_PLAN.md`
- Archive or replace with a short pointer to the canonical plan/issue surface.
- At 472 lines, it is a context sink and clearly violates the repo's own plan hygiene rules.
- Its durable outcomes should live in the owning plan tree or bug sheets, not in a giant root-level migration narrative.

#### `Plans/sprint-jump-stability-tests.md`
- Archive.
- At 461 lines, this is no longer a good active plan surface.
- Keep a 10-20 line summary in the parent plan or issue, and move the rest to archive if still needed historically.

#### `Plans/comedic-knockdown-overhaul.plan.md`
- Slim the parent.
- At 241 lines, it is too large for a parent plan.
- Keep the parent to status, quick resume, blockers, next step, and links; move detail into child docs or bug sheets.

#### Active chapter docs over ~150 lines
- Split when they cross that size because they stop being fast restart surfaces.
- Current likely targets:
  - `Plans/unified-locomotion-roadmap/09-validation-debugging-and-tuning.md`
  - `Plans/unified-locomotion-roadmap/07-terrain-and-contact-robustness.md`
  - `Plans/unified-locomotion-roadmap/08-expressive-motion-and-feel.md`
- These are still useful, but they should roll detail into linked child docs earlier.

## Mandatory-reading problem

The biggest context mistake is not the number of files. It is the effective policy of loading too many heavyweight docs before task work begins.

### Current problem pattern
- `.copilot-instructions.md` points agents to a long mandatory-reading list.
- Several of those docs are themselves large and partially overlapping.
- This creates a high fixed context cost even for narrow tasks.

### Better pattern
- One thin workspace instruction file
- One fast routing file
- One active parent plan or issue
- Everything else loaded by task type, not by default

## Thin-entry-point strategy

For every heavyweight durable doc, prefer this shape:

1. Quick Load
   - 3-6 bullets
2. Read More When
   - explicit conditions
3. Short core rules or map
4. Link to appendix, skill, issue, or child doc for infrequent detail

This keeps the knowledge available without forcing it into every agent's starting context.

## Recommended cleanup order

1. Shrink `.copilot-instructions.md` so it stops duplicating other docs.
2. Slim `AGENT_TEST_RUNNING.md` to the repo-primary path and move fallback detail out.
3. Slim `CODING_STANDARDS.md` into a true rulebook.
4. Collapse `LOCOMOTION_BASELINES.md` into a current-baseline index.
5. Archive or summarize `AGENT_MOVEMENT_MIGRATION_PLAN.md` and `Plans/sprint-jump-stability-tests.md`.
6. Reduce oversized parent plans so active plans become fast restart points again.

## Bottom line

Yes, the current documentation surface is too heavy for routine agent work.

But the fix is **not** to delete most documents.

The fix is to:

- keep a few thin entry points
- stop duplicating the same rules across files
- move implementation history and debugging attempts out of durable docs
- archive oversized finished plans
- let skills carry multi-step procedure where that is more efficient than a long root document