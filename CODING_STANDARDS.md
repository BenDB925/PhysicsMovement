# Coding Standards — C# / Unity (Agent-Optimised)

> **Audience:** AI coding agents (Claude model via GitHub Copilot).
> **Purpose:** Defines mandatory workflow, style, testing, documentation, and review rules for every code change in any Unity project that references this file.
> **Authority:** This file supersedes ad-hoc conventions. If a rule here conflicts with a prompt, follow this file unless the user explicitly overrides it.

---

## 1 — Development Workflow (Mandatory Sequence)

Every task — bug fix, feature, refactor — MUST follow these phases **in order**. Do NOT skip phases.

### Phase A: Understand the Problem

1. State the problem or feature in one sentence.
2. Identify the **acceptance criteria** — what does "done" look like?
3. List affected classes/systems. If unsure, search the codebase first.
4. Read existing class-level `<summary>` XML docs and any `// DESIGN:` comments before touching code.

### Phase B: Comment-First Design

1. In each class that will change, write `// STEP n:` comments describing the **logical flow** of the solution before writing any implementation code.
2. Comments must be detailed enough that another agent could implement the code from them alone.
3. If the solution introduces a new class, create the file with only:
   - The `<summary>` XML doc describing purpose, responsibilities, and collaborators.
   - The `// STEP n:` skeleton.
   - Public interface signatures (methods, properties) with XML docs — bodies can be `throw new NotImplementedException();`.
4. Commit nothing yet — this is design only.

### Phase C: Write Tests First

1. For every behaviour described in the Step comments, write at least one **unit test** (EditMode) or **integration test** (PlayMode) that asserts the expected outcome.
2. Tests MUST compile but MUST FAIL (red) because the implementation doesn't exist yet.
3. Also write **regression tests** for any existing behaviour that could break.
4. Follow the naming convention: `MethodName_Condition_ExpectedResult`.
5. Tests live in `Assets/Tests/EditMode/` or `Assets/Tests/PlayMode/` mirroring the source folder structure.
6. **Run tests from the terminal** to confirm they fail. See [`AGENT_TEST_RUNNING.md`](AGENT_TEST_RUNNING.md) §2–3 for exact commands. Parse the NUnit XML results to verify `failed > 0`.

### Phase D: Implement to Pass Tests

1. Write the minimum code required to make all tests from Phase C pass.
2. Do not add untested behaviour in this phase.
3. Run all tests (not just new ones) — zero failures required before proceeding. Use the terminal commands in [`AGENT_TEST_RUNNING.md`](AGENT_TEST_RUNNING.md) §4 to run EditMode + PlayMode, then parse the XML to confirm `result = "Passed"`.

### Phase E: Verify Feature / Fix

1. Confirm the acceptance criteria from Phase A are met.
2. If the feature requires runtime verification (physics feel, visual result, network sync), state what to check and how.
3. If acceptance criteria are NOT met → return to Phase B and iterate. Write new Step comments, new tests, new code.

### Phase F: Pre-Commit Self-Review

Before presenting work as ready, run **every item** in this checklist. If any item fails, fix it before committing.

| #  | Check | Action if failed |
|----|-------|-----------------|
| F1 | **Dead code** — Are there any methods, fields, classes, or `using` statements that are no longer referenced? | Remove them. |
| F2 | **Redundancy** — Is there duplicated logic that should be extracted into a shared method or utility? | Refactor to single source of truth. |
| F3 | **Test coverage** — Does every public method and every branch in new/changed code have a corresponding test? | Write missing tests. |
| F4 | **Simplicity vs flexibility** — Could the solution be simpler WITHOUT sacrificing extensibility? Are interfaces and abstractions justified by current or clearly anticipated use? | Refactor, but do not remove extensibility points that serve a known future need. |
| F5 | **Naming** — Do all new symbols follow the naming conventions in §3? | Rename. |
| F6 | **XML docs** — Do all new/changed public and protected members have `<summary>` docs? Does every class have a class-level `<summary>`? | Add docs. |
| F7 | **No warnings** — Does the project compile with zero warnings? | Fix warnings. |
| F8 | **DESIGN comments** — Are `// STEP n:` comments from Phase B still accurate post-implementation? Were they converted to meaningful inline comments or removed if redundant? | Update or clean up. |
| F9 | **Agent context files** — If new systems/classes were added, was `.copilot-instructions.md` or `ARCHITECTURE.md` updated to reference them? | Update those files. |
| F10 | **Regression** — Do ALL existing tests still pass? Run all tests via [`AGENT_TEST_RUNNING.md`](AGENT_TEST_RUNNING.md) §6 and parse XML results. | Fix regressions before committing. |

**Output format for the self-review:** Present a table of F1–F10 with PASS/FAIL and a brief note. If any FAIL, describe the fix applied. The user should see the review results before they're committed.

---

## 2 — Testing Standards

> **Agents:** You can run tests directly from the terminal without user intervention. See [`AGENT_TEST_RUNNING.md`](AGENT_TEST_RUNNING.md) for complete instructions on batch-mode execution, result parsing, and troubleshooting.

### Framework

- **Unity Test Framework** (NUnit-based).
- **EditMode tests** for pure logic, data transforms, state machines, utility functions — anything that doesn't need a scene or MonoBehaviour lifecycle.
- **PlayMode tests** for physics interactions, coroutine/async flows, component integration, NetworkBehaviour, and anything requiring `Awake`/`Start`/`Update`.

### Rules

| Rule | Detail |
|------|--------|
| Naming | `MethodName_Condition_ExpectedResult` — e.g., `ApplyForce_WhenGrounded_IncreasesVelocity` |
| Arrange-Act-Assert | Every test must have clearly separated AAA sections, each marked with `// Arrange`, `// Act`, `// Assert` comments. |
| One assert per concept | Multiple `Assert` calls are fine if they test facets of the same concept. Do not test unrelated things in one test. |
| No test interdependency | Tests must not depend on execution order or shared mutable state. Use `[SetUp]`/`[TearDown]`. |
| Test file location | Mirror source path: `Assets/Scripts/Character/MuscleController.cs` → `Assets/Tests/EditMode/Character/MuscleControllerTests.cs` |
| Assembly definitions | Tests must have their own `.asmdef` referencing the source `.asmdef` and `nunit.framework`. |

---

## 3 — Naming & Style Conventions (Microsoft C#)

### Casing

| Symbol | Convention | Example |
|--------|-----------|---------|
| Namespace | PascalCase | `Character.Physics` |
| Class / Struct / Enum | PascalCase | `MuscleController` |
| Interface | `I` + PascalCase | `IGrabbable` |
| Public method | PascalCase | `ApplyForce()` |
| Public property | PascalCase | `IsGrounded` |
| Private field | `_camelCase` | `_rigidbody` |
| Local variable | camelCase | `currentForce` |
| Constant | PascalCase | `MaxHealth` |
| Enum member | PascalCase | `PlayerState.Ragdoll` |
| Event | PascalCase | `OnPlayerDied` |
| Type parameter | `T` + PascalCase (if named) | `TResult` |

### Layout

- One class per file. File name matches class name.
- `using` directives at top, outside namespace. System namespaces first, then Unity, then project.
- Member order within a class:
  1. Constants / static readonly
  2. Serialized fields (`[SerializeField] private`)
  3. Private fields
  4. Public properties
  5. Unity lifecycle methods (`Awake`, `OnEnable`, `Start`, `FixedUpdate`, `Update`, `LateUpdate`, `OnDisable`, `OnDestroy`) — in lifecycle order
  6. Public methods
  7. Private methods
  8. Nested types

### Formatting

- Allman braces (opening brace on new line).
- 4-space indentation (no tabs).
- Max line length: 120 characters (soft guideline).
- Always use braces for `if`/`else`/`for`/`while`/`foreach`, even single-line bodies.
- Use `var` only when the type is obvious from the right-hand side.
- Prefer expression-bodied members for trivial one-liners (`public float Speed => _speed;`).

### Unity-Specific Style

- **Never use public fields for serialization.** Use `[SerializeField] private` and expose via property if needed.
- Prefer `TryGetComponent` over `GetComponent` (avoids allocation on failure).
- Cache component references in `Awake()`, not in `Update()`.
- Use `CompareTag("tag")` instead of `== "tag"`.

---

## 4 — Architecture & Design

### Philosophy

Design for **future flexibility**. Use interfaces and abstraction proactively when a system is likely to have multiple implementations or when it isolates a subsystem for testing. Abstractions must be **justified** — "this will clearly need to vary" qualifies; "it might someday" does not.

Avoid god classes. Split functionality to avoid context-drain in agents. Keep classes under 400-500 lines.

### Principles

| Principle | Application |
|-----------|------------|
| Single Responsibility | Each class has one reason to change. MonoBehaviours should delegate logic to plain C# classes where possible. |
| Dependency Inversion | Depend on interfaces, not concretions. Pass dependencies via constructor (plain C#) or `[SerializeField]` / `Inject` (MonoBehaviour). |
| Composition over Inheritance | Prefer composing behaviours from small components over deep inheritance hierarchies. |
| Interface Segregation | Keep interfaces small and focused. A class should not be forced to implement unused members. |
| Open/Closed | Extend behaviour through new implementations, not modifying existing classes. Use events/delegates for extensibility points. |

### Dependency Injection

- No DI framework required. Use **constructor injection** for plain C# classes and **serialized field injection** for MonoBehaviours.
- If a project adopts Zenject/VContainer, prefer constructor injection everywhere and limit `[Inject]` on MonoBehaviours to the method-injection pattern.

### Patterns to Prefer

| Pattern | When |
|---------|------|
| ScriptableObject events/data | Shared config, data-driven design, decoupling. |
| Observer (C# events/delegates) | Decoupled communication between systems. |
| State pattern / state machine | Character states, game phases, UI flows. |
| Strategy pattern | Swappable algorithms (e.g., different movement models). |
| Factory | Object creation that needs flexibility or pooling. |
| Object pooling | Any frequently spawned/destroyed objects. |

---

## 5 — Unity-Specific Guidelines

### Physics

- Physics logic belongs in `FixedUpdate()`. Never apply forces in `Update()`.
- Input should be read in `Update()` and stored, then consumed in `FixedUpdate()`.
- Set Rigidbody interpolation to `Interpolate` for player-controlled bodies to avoid visual jitter.
- Prefer `Rigidbody.AddForce` with appropriate `ForceMode` over directly setting `velocity`.
- Use layers and the collision matrix for filtering — avoid checking tags in `OnCollision*`.

### Coroutines vs Async/Await

- Use **coroutines** for Unity-lifecycle-bound sequences (animations, timed gameplay events).
- Use **async/await** (UniTask preferred if available, otherwise `Awaitable` in Unity 2023+) for I/O, networking, or genuinely asynchronous operations.
- Never use `async void` except for Unity event methods that require it. Use `async UniTaskVoid` or fire-and-forget carefully.

### Memory & Performance

| Rule | Detail |
|------|--------|
| Avoid per-frame allocations | No `new` in `Update`/`FixedUpdate`/`LateUpdate`. Use caching, pooling, `NativeArray`, or `stackalloc`. |
| Cache component refs | Store `GetComponent` results in `Awake`. Never call `GetComponent` in a loop or per-frame. |
| Pool frequently spawned objects | Use an object pool for projectiles, effects, UI elements, etc. |
| String operations | Avoid string concatenation in hot paths. Use `StringBuilder` or `string.Create`. |
| Avoid LINQ in hot paths | LINQ allocates enumerators. Use `for`/`foreach` loops in performance-critical code. |
| Physics queries | Prefer `NonAlloc` variants (`RaycastNonAlloc`, `OverlapSphereNonAlloc`). Pre-allocate result arrays. |
| Use Profiler | Wrap suspect code in `Profiler.BeginSample` / `Profiler.EndSample` during investigation. Remove before committing unless intentionally kept. |

### Networking (Netcode for GameObjects)

- Physics simulation is host-authoritative. Clients send inputs; host applies forces and replicates state.
- Use `NetworkVariable` for replicated state. Use RPCs for events/actions.
- `[ServerRpc]` for client→host; `[ClientRpc]` for host→clients.
- Minimise data sent per tick — replicate transforms only for objects the client needs.

---

## 6 — Error Handling

| Situation | Approach |
|-----------|----------|
| Unrecoverable state (missing required component, null dependency) | `Debug.LogError` + `return` or `throw` in editor. Never silently continue. |
| Expected failure (network timeout, file not found) | Return a result type or `bool TryX(out T result)` pattern. Log at `Warning` level. |
| Development assertions | Use `Debug.Assert(condition, message)` for invariants. These are stripped from release builds. |
| Try/catch | Use only around genuinely unpredictable external operations (IO, network, third-party). Never use as flow control. |
| Null checks | Prefer null-coalescing and null-conditional operators. For Unity objects, remember that `UnityEngine.Object` overloads `==` — use the Unity-safe null check (`obj == null` or `obj != null`), not `is null` / `is not null`. |

---

## 7 — Documentation Rules (Agent-Context Optimised)

The goal is to give agents maximum context **without reading method bodies**. Every layer of documentation below is **mandatory**.

### Layer 1: Class-Level XML Doc (`<summary>`)

Every class/struct/interface MUST have a `<summary>` block that answers:

1. **What** — one-sentence purpose.
2. **Why** — what problem it solves or what system it belongs to.
3. **Collaborators** — which other classes/interfaces it depends on or communicates with.
4. **Lifecycle** — for MonoBehaviours, which Unity messages it uses and why.

```csharp
/// <summary>
/// Applies configurable muscle forces to character joints to maintain an upright posture.
/// Part of the Character/Physics system. Driven by <see cref="CharacterController"/>
/// which sets target poses. Uses FixedUpdate to apply joint forces each physics step.
/// Collaborators: <see cref="IJointConfig"/>, <see cref="CharacterController"/>.
/// </summary>
public class MuscleController : MonoBehaviour { }
```

### Layer 2: Member-Level XML Docs

All `public` and `protected` members must have `<summary>`. Include `<param>`, `<returns>`, `<exception>` where applicable.

### Layer 3: `// DESIGN:` Comments

For non-obvious design decisions, use `// DESIGN:` prefix so agents can grep for rationale:

```csharp
// DESIGN: We apply forces in local space because joint axes are defined locally.
```

### Layer 4: `.copilot-instructions.md` (Repo Root)

This file is auto-read by Copilot/Claude. It MUST contain:

1. A reference to this coding standards file.
2. A one-paragraph project summary.
3. A list of top-level systems/namespaces with one-line descriptions.
4. Any project-specific rules that override or extend this document.

**Update this file whenever a new system or namespace is added.**

### Layer 5: `ARCHITECTURE.md` (Repo Root)

A living document describing:

1. High-level system diagram (text-based, Mermaid or ASCII).
2. Data flow for core loops (input → physics → render, networking tick).
3. Key classes per system with one-line descriptions.
4. Integration points between systems.

**Update this file whenever architecture changes.**

### Layer 6: Assembly Definition Docs

Each `.asmdef` should have a corresponding section in `ARCHITECTURE.md` explaining what it contains and what it depends on.

---

## 8 — Folder Structure

```
Assets/
├── Materials/
├── Prefabs/
├── Scenes/
├── Scripts/
│   ├── Character/        # Player character systems (physics, muscles, state)
│   ├── Core/             # Game-wide singletons, managers, utilities
│   ├── Input/            # Input handling, action maps
│   ├── Networking/       # NGO components, network managers
│   └── UI/               # UI controllers, views
├── ScriptableObjects/    # SO assets (config, events, data)
├── Tests/
│   ├── EditMode/
│   │   ├── Character/
│   │   ├── Core/
│   │   ├── Input/
│   │   ├── Networking/
│   │   └── UI/
│   └── PlayMode/
│       ├── Character/
│       ├── Core/
│       ├── Input/
│       ├── Networking/
│       └── UI/
└── ThirdParty/           # External plugins, vendored code (do not modify)
```

- Mirror the `Scripts/` structure in `Tests/EditMode/` and `Tests/PlayMode/`.
- New systems get a new folder under `Scripts/` and corresponding test folders.
- Do not put test code in the main Scripts assembly.

---

## 9 — Agent Operating Rules

These rules govern agent behaviour directly.

| Rule | Detail |
|------|--------|
| **Read before write** | Always read the target file and its class-level docs before editing. Check related tests. |
| **One concern per commit** | Each commit should address exactly one bug, feature, or refactor. |
| **Never delete tests** | Tests may be updated if behaviour intentionally changes, but never silently removed. |
| **Explain trade-offs** | If multiple approaches exist, briefly state alternatives and why the chosen approach was selected. |
| **Flag uncertainty** | If unsure whether a change is safe, say so explicitly. Suggest how to verify. |
| **Preserve existing patterns** | When adding to an existing system, follow the patterns already in use there unless this document mandates otherwise. |
| **Update context files** | After any structural change (new class, new namespace, new system), update `.copilot-instructions.md` and `ARCHITECTURE.md`. |
| **No magic numbers** | Extract literals to named constants or ScriptableObject fields. |
| **Respect access modifiers** | Keep everything as private as possible. Only expose what is genuinely needed externally. |
| **Prefer pure functions** | Where possible, write static methods with no side effects for logic. This makes them trivially testable. |

---

## 10 — Quick Reference: Workflow at a Glance

```
TASK RECEIVED
     │
     ▼
 ┌─────────────────────┐
 │  A. Understand       │  Define problem, acceptance criteria, affected classes.
 └────────┬────────────┘
          ▼
 ┌─────────────────────┐
 │  B. Comment Design   │  Write // STEP comments in target classes.
 └────────┬────────────┘
          ▼
 ┌─────────────────────┐
 │  C. Write Tests      │  Red tests that assert expected behaviour.
 └────────┬────────────┘
          ▼
 ┌─────────────────────┐
 │  D. Implement        │  Write code to make tests green.
 └────────┬────────────┘
          ▼
 ┌─────────────────────┐
 │  E. Verify           │  Acceptance criteria met? All tests pass?
 └────────┬────────────┘
          │ NO ──► Loop back to B
          ▼ YES
 ┌─────────────────────┐
 │  F. Self-Review      │  Run F1–F10 checklist. Fix failures.
 └────────┬────────────┘
          ▼
     PRESENT TO USER
     (with review table)
```

---

*End of coding standards. This document is versioned alongside the project. Propose changes via the same workflow defined above.*
