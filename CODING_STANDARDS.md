# Coding Standards — C# / Unity (Agent-Optimised)

> Audience: AI coding agents working in this repo.
> Purpose: Define the mandatory workflow, testing expectations, style rules, documentation contract, and self-review gate for every change.
> Authority: Follow this file unless the user explicitly overrides a rule.

## Quick Load

- Follow Phases A-F in order. Do not skip straight to code.
- Treat the active parent plan or roadmap chapter as a live execution record and update it as soon as the task state changes.
- Write or update tests before implementation, confirm red when introducing new behavior, and finish with the smallest trustworthy green regression slice.
- Prefer outcome-based PlayMode verification for physics behavior, keep classes small, and extract collaborators before a class becomes a context sink.
- Finish every completed subtask with the Phase F self-review table and a focused commit.

## Read More When

- Continue into Section 1 when you need the full task workflow and closeout rules.
- Continue into Section 2 for test selection, verification scope, and artifact expectations.
- Continue into Sections 3-6 when touching C#, Unity runtime behavior, or public APIs.
- Continue into Section 7 when adding new systems, public members, or task-record detail.
- Use `AGENT_TEST_RUNNING.md` for exact unattended test commands and artifact triage. Use `Plans/README.md` for parent-plan, child-doc, and bug-sheet structure.

## 1 — Development Workflow (Mandatory Sequence)

Every task must follow these phases in order.

### Phase A: Understand the Problem

1. State the problem or feature in one sentence.
2. Define the acceptance criteria before designing the fix.
3. Identify the affected classes or systems. Search first if the owner is unclear.
4. Read class-level XML docs and relevant `// DESIGN:` comments before editing.
5. When a referenced doc offers `Quick Load` or `Read More When`, read those sections first and continue deeper only if the task needs it.
6. Prefer the active parent plan, roadmap chapter, or freshest artifact summary before raw logs, old child docs, or older chat context.
7. Read `LOCOMOTION_BASELINES.md` only for regression comparison or known-red validation.
8. Read only the roadmap chapters that own the task instead of loading the entire roadmap.
9. For test triage, start with `TestResults/latest-summary.md`, then the relevant XML, and only then the full Unity log if needed.
10. Identify the active parent plan or chapter before implementation and update it as soon as subtasks, blockers, hypotheses, next steps, or best resume artifacts change.

### Phase B: Comment-First Design

1. Before implementation, add `// STEP n:` comments to each changed class describing the intended logical flow.
2. For new classes, start with the class `<summary>`, the `// STEP n:` skeleton, and public signatures with XML docs.
3. Do not commit during design-only work.

### Phase C: Write Tests First

1. Cover every new or changed behavior with EditMode or PlayMode tests based on what the behavior actually needs.
2. New behavior tests must compile and fail red before implementation.
3. Add regression coverage for nearby behavior that could break.
4. Use the naming pattern `MethodName_Condition_ExpectedResult`.
5. Mirror source paths under `Assets/Tests/EditMode/` or `Assets/Tests/PlayMode/`.
6. Run focused red and green phases through `AGENT_TEST_RUNNING.md` and trust fresh `TestResults/` artifacts over terminal text alone.
7. If results XML is missing, treat that as infrastructure failure and rerun sequentially before trusting the outcome.

### Phase C+: Outcome Tests and Parameter Sweeps

- Prefer outcome-based PlayMode tests for physics or emergent behavior when unit tests can pass while the system is still wrong.
- Add parameter sweep or optimizer tests when numeric tuning would otherwise be guesswork. Write reports to `Logs/` and apply tuned values to prefabs, not C# defaults.
- If a threshold meaningfully defines working, capture it in a pass or fail outcome test.

### Phase D: Implement to Pass Tests

1. Write the minimum code needed to make the Phase C tests pass.
2. Do not add unrelated behavior in this phase.
3. Run the smallest relevant regression slice that covers the touched behavior and its nearest neighbors.
4. Escalate to broader coverage only for shared infrastructure, scene or bootstrap wiring, assembly-definition changes, or uncertain blast radius.
5. If reruns still produce no XML, keep the gate infrastructure-red instead of treating it as green.

### Phase E: Verify Feature / Fix

1. Confirm the Phase A acceptance criteria are met.
2. If runtime feel, visuals, or networking matter, state exactly what must be checked and how.
3. If acceptance criteria are not met, return to Phase B and iterate with updated Step comments, tests, and code.

### Phase F: Pre-Commit Self-Review

Run every check below before presenting the work as ready.

| # | Check | Action if failed |
|---|---|---|
| F1 | Dead code | Remove unused methods, fields, classes, or `using` statements. |
| F2 | Redundancy | Extract duplicated logic to a shared implementation. |
| F3 | Test coverage | Add missing tests for changed public methods and new branches. |
| F4 | Simplicity vs flexibility | Remove unjustified abstractions or overbuilt plumbing. |
| F5 | Naming | Rename symbols that break the conventions in Section 3. |
| F6 | XML docs | Add or update `<summary>` docs for new or changed public and protected members. |
| F7 | No warnings | Fix warnings before closing the subtask. |
| F8 | `// STEP` accuracy | Update or remove Step comments that no longer match the code. |
| F9 | Agent context files | Refresh plans, `.copilot-instructions.md`, or `ARCHITECTURE.md` when the task changes their truth. |
| F10 | Regression gate | Run the focused regression slice that matches the touched system and confirm it is green. |

Output the Phase F result as a PASS or FAIL table with a brief note for each row. If any row fails, fix it before committing.

Commit per completed subtask. Each commit should cover one bounded slice, and the parent plan or chapter must be updated in the same slice before you move on.

## 2 — Testing Standards

- Use `Tools/Run-UnityTests.ps1` as the primary unattended runner. `AGENT_TEST_RUNNING.md` owns the exact commands, exit codes, and troubleshooting flow.
- Use EditMode for pure logic, state transforms, data validation, and utility code.
- Use PlayMode for physics behavior, MonoBehaviour lifecycle, component integration, scene behavior, and networking.
- Run EditMode and PlayMode sequentially, never in parallel, and verify fresh XML under `TestResults/` before trusting the result.

| Rule | Detail |
|---|---|
| Naming | Use `MethodName_Condition_ExpectedResult`. |
| Arrange-Act-Assert | Mark AAA sections with `// Arrange`, `// Act`, and `// Assert` comments. |
| One concept per test | Multiple asserts are fine when they validate the same behavior. |
| No test interdependency | Use `[SetUp]` and `[TearDown]`; do not depend on execution order. |
| Test file location | Mirror the source path under `Assets/Tests/EditMode/` or `Assets/Tests/PlayMode/`. |
| Assembly definitions | Tests need their own `.asmdef` with only the dependencies they actually require. |
| Verification scope | Default to feature-scoped verification and widen only when the blast radius justifies it. |

## 3 — Naming & Style Conventions

### Casing

- Namespaces, classes, structs, enums, public methods, public properties, constants, enum members, and events use PascalCase.
- Interfaces use `I` plus PascalCase.
- Private fields use `_camelCase`.
- Local variables and parameters use camelCase.
- Type parameters use `T` plus PascalCase when named.

### Layout

- One class per file. File names match the main type.
- Place `using` directives at the top: System first, then Unity, then project namespaces.
- Member order within a class: constants and statics, serialized fields, private fields, public properties, Unity lifecycle methods, public methods, private methods, nested types.

### Formatting

- Use Allman braces and 4-space indentation.
- Treat 120 characters as the soft maximum line length.
- Always use braces for control-flow statements.
- Use `var` only when the right-hand side makes the type obvious.
- Prefer expression-bodied members only for trivial one-line members.

### Unity-Specific Style

- Never use public fields for serialization. Use `[SerializeField] private` and expose properties only when needed.
- Prefer `TryGetComponent` over `GetComponent` where failure is expected.
- Cache component references in `Awake`, not per frame.
- Use `CompareTag` instead of string equality for tags.

## 4 — Architecture & Design

### Philosophy

- Design for justified flexibility. Use interfaces and abstractions when the variation is real, not hypothetical.
- Avoid god classes. Target roughly 300-350 lines of executable logic, stay normally under 500 total lines, and treat 600 total lines as a hard refactor ceiling.
- Prefer focused collaborators over adding another subsystem responsibility to an already large MonoBehaviour.

### Principles

- Single Responsibility: each class should have one reason to change.
- Dependency Inversion: depend on abstractions when it improves testability or isolates volatility.
- Composition over Inheritance: favor small components over deep inheritance chains.
- Interface Segregation: keep interfaces small and focused.
- Open/Closed: extend behavior with new implementations or hooks instead of editing stable code unnecessarily.

### Dependency Injection

- Use constructor injection for plain C# classes.
- Use serialized-field injection for MonoBehaviours unless the project explicitly adopts a DI container.
- If Zenject or VContainer is added, keep constructor injection as the default and limit MonoBehaviour injection to the supported project pattern.

### Patterns to Prefer

Use ScriptableObject data and events, observer-style events, state machines, strategy objects, factories, and pooling when they solve a current or clearly anticipated variation point.

## 5 — Unity-Specific Guidelines

### Physics

- Put physics logic in `FixedUpdate`.
- Read input in `Update`, store it, and consume it in `FixedUpdate`.
- Use Rigidbody interpolation for player-controlled bodies that need smooth presentation.
- Prefer `Rigidbody.AddForce` and collision layers over directly setting velocity or filtering with ad hoc tag checks.

### Coroutines vs Async/Await

- Use coroutines for Unity-lifecycle-bound sequences such as timed gameplay events.
- Use `async` and `await` for I/O, networking, or genuinely asynchronous work. Prefer the project-standard async abstraction when present.
- Do not use `async void` except where Unity requires it.

### Memory & Performance

- Avoid per-frame allocations in `Update`, `FixedUpdate`, and `LateUpdate`.
- Cache frequently used references and pool frequently spawned objects.
- Avoid hot-path string concatenation, LINQ, and alloc-heavy physics queries when a simpler loop or `NonAlloc` API will do.
- Use profiler samples during investigation and remove them before commit unless they are intentionally permanent diagnostics.

### Networking

- Treat physics as host-authoritative. Clients send inputs; the host applies forces and replicates state.
- Use `NetworkVariable` for replicated state and RPCs for actions or events.
- Minimize data sent per tick to what the client actually needs.

## 6 — Error Handling

| Situation | Approach |
|---|---|
| Missing required component or unrecoverable state | `Debug.LogError` plus `return`, or `throw` in editor-only initialization paths. Do not silently continue. |
| Expected failure | Prefer result types or `bool TryX(out T result)` patterns and log at warning level only when useful. |
| Development invariant | Use `Debug.Assert` for developer-time checks. |
| Try/catch | Use only around genuinely unpredictable external operations such as I/O, networking, or third-party calls. |
| Null checks | Use Unity-safe null checks (`obj == null` and `obj != null`) for `UnityEngine.Object` references. |

## 7 — Documentation Rules (Agent-Context Optimised)

The goal is to give agents enough context without forcing every task to read method bodies or execution diaries.

### Layer 0: Task Record Tree

1. Use the user-specified folder if given. Otherwise default to `Plans/` and follow `Plans/README.md`.
2. Keep the parent plan or active chapter short: status, next step, blockers, verified artifacts, and links to child docs or bug sheets.
3. Update the active parent plan or chapter during the work, not only at pause or completion.
4. Split early into child docs or bug sheets when detail, logs, or multiple active hypotheses accumulate.

### Layer 1: Class-Level XML Doc (`<summary>`)

Every class, struct, and interface needs a `<summary>` that explains what it does, why it exists, its collaborators, and any MonoBehaviour lifecycle responsibilities.

### Layer 2: Member-Level XML Docs

All `public` and `protected` members need `<summary>` docs. Add `<param>`, `<returns>`, and `<exception>` tags where they add clarity.

### Layer 3: `// DESIGN:` Comments

Use `// DESIGN:` only for non-obvious rationale that a future agent should be able to grep quickly.

### Layer 4: `.copilot-instructions.md` (Repo Root)

Keep this file thin: repo summary, reading order, context-budget rules, implemented surface, and repo-specific overrides that are not better stated elsewhere.

### Layer 5: `ARCHITECTURE.md` (Repo Root)

Update `ARCHITECTURE.md` when subsystem ownership, runtime flow, or integration boundaries change.

### Layer 6: Assembly Definition Docs

Keep assembly-definition ownership and dependency notes in `ARCHITECTURE.md` rather than repeating them across multiple root docs.

## 8 — Project Structure Rules

- Mirror source folders under `Assets/Tests/`.
- Keep test code out of runtime assemblies.
- Use `TASK_ROUTING.md` and `ARCHITECTURE.md` for the current implemented surface instead of copying a large aspirational folder tree into this file.

## 9 — Agent Operating Rules

| Rule | Detail |
|---|---|
| Read before write | Read the target file, relevant docs, and nearby tests before editing. |
| One concern per commit | Each commit covers one bug, feature, refactor, or documentation slice. |
| Commit per subtask | When working from a plan chapter or child doc, run Phase F and commit after each completed subtask. |
| Never delete tests silently | Update tests when behavior intentionally changes, but do not remove coverage without replacing it. |
| Explain trade-offs | When multiple credible approaches exist, note why the chosen approach won. |
| Flag uncertainty | If a risk remains, say so and say how to verify it. |
| Preserve existing patterns | Match the surrounding system unless there is a clear repo-level reason to change direction. |
| Update context files | Refresh plans, `.copilot-instructions.md`, and `ARCHITECTURE.md` when their truth changes. |
| No magic numbers | Extract literals to named constants or serialized configuration where that improves clarity. |
| Respect access modifiers | Keep symbols as private as practical and expose only what the caller actually needs. |
| Prefer pure functions | Use side-effect-free helpers where possible to keep logic testable. |

End of coding standards. This document is versioned alongside the project and should stay a rulebook, not a tutorial or execution diary.