# Refactor internal logic: sparse-set ECS and incremental dependency execution

Status: design proposal. This document does not change public contract.

Goal: to solve hidden ECS execution dependencies, support incremental reduction/incremental simulation and budget enforcement.

Issue:
Maintaining a second general-purpose storage engine is not optimal. Use a proven C# sparse-set ECS for entity/component mechanics; retain a small Cascade layer for facts, execution, and transactional publication. Ensure feature parity to EntitasECS (which already exists in the reduction loop).

This is the single active internal refactor plan. The [package README](../Assets/CascadeEngine/Readme.md) describes the current runtime contract; this document defines the target and migration. Preserve existing atomic publication, staged lifecycle, and exhaustive ownership cleanup through the refactor. Verify the target against the executable regression suite rather than historical progress reports.

## 1. Decisions and compatibility boundary

Lock these three decisions before broad migration. Implement capacity/admission first and prove the system execution boundary in the thin ECS slice:

1. Expose the entity-pool ceiling through reduction-loop settings: `0` means no configured ceiling; a positive integer limits concurrent allocated slots.
2. Allow at most one accepted fact of each type per entity per full reduction loop, including all incremental calls. The user explicitly selected: identical repeats are no-ops; a different payload throws before acceptance.
3. Make resumable execution mandatory underneath every simulation entry point. Execute a complete internal ECS system, then check the frame budget; pause only between systems and resume with the next system.

Preserve existing public types, signatures, constructors, adapters, and supported callback interfaces. Public declarations under `Internal/` are included. Do not add mandatory members to interfaces that external adapters implement.

Public signature compatibility is not full behavioral compatibility: removing fact multiplicity and moving incremental yield points to completed-system boundaries are explicitly requested behavior changes. Document and migrate both instead of preserving callback-level stepping as compatibility scaffolding. The existing public read/write authority, mutation visibility, negative sealing, and cleanup rules remain unchanged unless a further decision explicitly changes them.

| Surface | Target behavior |
| --- | --- |
| `FactSimulation(feature)` and `FactSimulation(feature, settings)` | Retained; use the same executor and backend |
| `Emit<TFact>` | Same signature; one immutable value per entity/type/loop |
| `IEntityFactView.All<TFact>()` | Same borrowed span API; length is zero or one |
| `TryGetLatest<TFact>` | Same signature; returns the single accepted value |
| `State`, `GetState`, `TryGetState`, entity queries | Read the last published state |
| `CreateEntity`, `DestroyEntity`, `TryGetEntity` | Preserve generational handles and staged lifecycle semantics |
| `RunTickIncremental` | Primary execution mechanism; incomplete means resume the same loop |
| `RunTick` | Same system executor; pre-publication budget stop fails closed, without returning partial work |
| `ForEachMutation` | Replayable, non-consumptive journal access |
| Existing fact capacity settings/hints | Retained as compatibility inputs; cannot enable multiplicity |

No parallel old/new runtime survives the migration. Temporary comparison harnesses are test-only. New registration metadata and diagnostic entry points may be additive; existing callers must not need source edits merely to compile.

## 2. Priority zero: contract changes

### 2.1 Entity ceiling, reservation, and growth

Reuse the existing `CascadeSettings.MaxEntities` and constructor parameter. Do not introduce a competing `MaxEntityPool` property with the same meaning.

```csharp
// Proposed semantics. Both calls preserve the existing constructor signature.
var bounded = new CascadeSettings(512, 16, 1);
var unbounded = new CascadeSettings(0, 16, 1);
```

Rules:

- Negative `MaxEntities` is invalid. Positive values limit concurrent slots, including pending creations and pending destructions until publication releases them.
- Recycled slots do not consume a lifetime entity quota. Lookups validate generation; numeric slot IDs are not stable domain IDs.
- `0` means no configured entity ceiling, subject to address space, supported indexes, and independent loop guardrails. It does not mean preallocate `int.MaxValue`, unlimited per-loop work, or infinite physical memory.
- Logical maximum and currently reserved capacity are different. Use existing `WarmupCapacityHints.EntityCapacity`/`Warmup` for reservation; keep their role explicit.
- For a positive ceiling, reserve the required execution capacity during setup, as the bounded path currently does. For `0`, start with a finite documented reservation, initially the existing 64-slot default, and permit explicit reservation increases between loops.
- Recommended zero-allocation policy: never grow storage inside an open loop or reducer callback. If reservation is exhausted, reject before ownership/lifecycle mutation and report required capacity. The host can reserve more while idle and submit a fresh loop. Unlimited mode removes the configured ceiling; it does not promise automatic allocation-free growth.
- Retain legacy explicit grow-on-demand setup behavior where required by existing hints, but label its allocating path. Do not silently promote growth into the guaranteed zero-allocation execution path.
- Derive queue/scratch reservations from actual reserved capacity and independent fact/work limits, not `MaxEntities * limit` when `MaxEntities == 0`. Check arithmetic overflow before reservation.
- Distinguish logical-pool exhaustion, reserved-storage exhaustion, and work/depth exhaustion in diagnostics. Per-call options cannot relax the configured entity ceiling.

Unbounded automatic growth during callbacks, strict zero allocation, and fixed memory consumption cannot all be promised together. If automatic in-loop growth is required later, it is an explicit allocating policy change, not an implementation trick.

### 2.2 One immutable fact component per entity/type/loop

Treat accepted facts as transient ECS components with insert-only semantics for the open loop:

```text
Emit(entity, MoveRequested(12)) -> accepted
Emit(entity, MoveRequested(12)) -> no-op; no new work or input revision
Emit(entity, MoveRequested(24)) -> conflict; proposed payload not accepted
```

An identical repeat uses the existing typed equality contract. Do not switch to reference identity, hash-only comparisons, first-wins, last-wins, or in-place replacement. An unequal second value fails regardless of producer order; the error identifies entity/generation, fact type, loop, and producer when available. Formatting diagnostics may allocate on failure.

Preflight generation, equality, uniqueness, capacity, and work admission before transfer of ownership. Equal duplicates and rejected payloads remain caller-owned. Every accepted payload still receives one cleanup attempt. A conflict thrown inside a reducer follows existing failed-loop rollback and exhaustive cleanup; an idle host conflict rejects the proposed input without publishing anything. Do not invent a new ownership result for the unchanged `void Emit` API.

The fact remains present and immutable until full-loop closure, failure, or disposal. Pausing does not clear its uniqueness slot. Pending destruction does not permit a second value; physical removal/retraction is still forbidden. `DeadFact` follows the same single-value rule.

Keep the equality contract explicit: every domain field that distinguishes two requests must participate. A reference-containing payload needs a documented equality/ownership rule; a marker fact may treat all values as equal. The engine cannot detect unequal domain meaning hidden by a faulty `Equals` implementation.

Preserve `MaxFactsPerTypePerEntity` on settings/guardrails and `FactsPerEntityPerTypeCapacity` on hints for source compatibility. Positive historical values are accepted and normalized to one, documented as legacy inputs; zero/negative invalid values retain validation. Change defaults/examples to one. Remove the multiplier from physical fact storage and make `MaxFactsPerEntity` count accepted types. No option can restore multiplicity.

Migrate multi-event callers explicitly: aggregate into one immutable domain input before admission, or represent separate events as separate entities where that matches the domain. Multiple independent requests must not disappear into an engine-owned last-wins slot. Aggregation must not introduce hidden per-frame allocation.

### 2.3 Mandatory execution between complete systems

**User clarification (2026-09-07): an internal ECS system runs to completion; only then is the budget checked and execution allowed to pause.** This supersedes the earlier proposal for resumable entity enumeration, batch collection and per-action preparation/validation.

A system is a scheduled operation over its eligible work for that execution. An adapter may invoke an existing per-entity reducer callback many times inside one system; each callback is not automatically a system. A batch system collects its input and invokes the batch callback before returning. Preserve the callback's defined membership and once-only rules. Do not manufacture one system per entity, fact, candidate or commit action to preserve the old scheduler under a new name.

Use one system execution order and one continuation at that level. It records the next system/closure region, loop/pass identity and cumulative counters. Accepted facts, invocation guards, pending lifecycle and prepared decisions remain necessary loop data. Iteration indexes and collection scratch are local to the running system; they do not become saved yield cursors. Input revision is needed only to invalidate unpublished decisions after valid late input. No coroutine, async task, scheduler plugin framework or parallel legacy executor is required.

The backend must allow individual systems to be invoked, or provide storage/query access for thin system adapters. Cascade owns the single stepping order. Reject an opaque whole-frame runner that prevents this boundary; native coroutine support is irrelevant.

```text
resume at next system
  -> execute the whole system
  -> advance to the next system
  -> run the existing budget check
  -> continue, or return incomplete and resume here next call
return complete when the loop finishes
```

That is the entire budget integration. System execution does its work to the end; the existing budget check runs afterwards. Add no separate slow-system policy, timing bookkeeping, compensation logic or special execution path.

Entity iteration, fact dispatch, batch collection, planning and validation finish within their system. Final publication and exhaustive cleanup finish together. The continuation retains the next system and necessary loop state, not an unfinished inner loop.

Keep existing invocation counters and admission/loop guardrails. Do not redefine a reducer invocation as a system execution or retain exact historical per-call entity counts. `RunTick` uses the same executor and its existing full-call failure behavior; `RunTickIncremental` returns incomplete at the system boundary and resumes the next system.

## 3. Target architecture and ownership

```text
Existing public Cascade API
          |
Feature composition -> immutable dependency plan -> printable graph
          |                         |
          +---------- incremental executor
                                    |
                            sparse-set ECS world
                                    |
                       prepared commit transaction
                                    |
                  published state + typed mutation journal
```

Keep five responsibilities, not five competing frameworks:

| Owner | Owns | Must not own |
| --- | --- | --- |
| ECS world/backend | Entity identity, generation, component pools, membership, query mechanics | Cascade closure, fact ownership policy, external delivery |
| Compiled feature plan | Immutable registration metadata, dependency edges, fact routing, stable diagnostic names | Live entities, mutable progress, component copies |
| Loop executor | Accepted-work scheduling, one continuation, invocation guards, budgets, pending lifecycle intent | Another component store or a second whole-frame runner |
| Commit transaction | Prepared typed decisions, validation, publication, mutation records | Direct gameplay side effects or reads of partly published output |
| Existing cleanup policy | One-attempt fact cleanup, terminal teardown, error aggregation | Guessing ownership of copied output resources |

The public `FactSimulation` remains the wiring hub and adapter. The backend owns each accepted fact and committed component value once. Schedule metadata and pending changes are not a mirror ECS world. Journal previous/next values remain intentional API snapshots.

Use one physical world/entity identity where possible, with separate typed pools for fact and output components. Do not create a second live entity world just to distinguish lifetimes. If a backend cannot adopt the `EntityRef` generation contract, the prototype must prove a minimal mapping without two authoritative lifecycle allocators or per-entity wrapper objects.

A dependency-owned sparse pool still contains buffers. The simplification is that Cascade stops implementing their mechanics. Avoid adding a generic repository/pool/allocator facade over the ECS. A thin internal adapter for actual lifecycle, typed access, query, and reserve operations is sufficient; do not build interchangeable-backend infrastructure.

## 4. Backend selection and ECS capability boundary

Bevy is a storage/scheduling reference, not a proposed Rust dependency. Its ECS offers both table and sparse-set storage; this project targets sparse sets. Select one established C# backend compatible with C# 8, Unity 6, managed fact structs, and IL2CPP constraints before migrating production storage. Do not port Bevy or build a new general-purpose ECS to avoid selecting a dependency. No package is selected or installed by this document.

The selection spike must prove:

- Generational create/find/destroy; typed has/read/set/remove; dense iteration and component intersection queries.
- Managed/reference-containing structs without boxing every component operation, runtime reflection registration, or mandatory runtime code generation. Native-only/unmanaged-only storage cannot transparently hold all current facts/states.
- Reservation before execution, including structural changes, query bookkeeping, entity reuse, and publication. Pooling alone is not proof of zero allocation or failure-free apply.
- No callbacks, observers, allocation, or recoverable validation failures during prepared apply. Suppressing observer delivery must not queue hidden allocations.
- Stable borrowed fact spans, or compatible validity boundaries, during a callback. Resizing or swap-back must not invalidate active system iteration. No query enumerator or borrowed span is retained as a continuation across a system boundary.
- Step-controlled execution and deterministic query/iteration adaptation where existing behavior requires it.
- Exhaustive teardown and controllable fact disposal; the backend must not independently dispose the same resource again.
- License, package footprint, maintenance, Unity build, and IL2CPP verification. Dependency approval/selection is a gate before installation, not permission to substitute a handwritten ECS if a candidate fails.

ECS operations and Cascade authority are deliberately separate:

| Capability | Backend/internal access | Existing Cascade-facing access |
| --- | --- | --- |
| Find entity | Generation-checked lookup | Existing `TryGetEntity`/`EntityRef` semantics |
| Read component | Typed lookup; scoped read reference if safe | State copies and queries over published state |
| Write component directly | Internal writer during setup or prepared publication | Commit decisions remain the runtime durable-write path |
| Remove component | Internal writer during publication | `CommitDecision.Delete()` |
| Create/destroy | Allocator + deferred structural operations | Existing staged lifecycle methods |

The backend should have direct read/write component capability equivalent to the requested Entitas subset. Do not expose an unrestricted world or escaping writable reference to reducers/committers. That would bypass mutation journals, previous-state isolation, and failure atomicity.

If direct public runtime writes are intended as an additional API, that requires a separate authority/lifetime decision. A scoped idle/bootstrap editor could be additive, but its publication and journal behavior must be specified first. This proposal preserves the current public contract and implements direct mutation internally. It does not claim parity with all Entitas indexes, groups, listeners, or code generation.

## 5. Dependency compilation, topology, and inspection

### 5.1 Explicit dependencies, not inferred callback behavior

Current registration declares triggers/required facts and output ownership. It does not fully declare facts emitted, cross-entity fact reads, or arbitrary callback effects. Generic interfaces and topological sorting cannot discover those dependencies. Do not inspect IL or use runtime reflection.

Add optional, typed registration declarations for produced fact types and additional read dependencies, with entity scope: current entity, arbitrary entity, or complete collection. Preserve existing builder signatures; attach metadata to a registration identity, not merely a reducer class, because one class can have multiple registrations. Distinguish a fact read, a read of published state, and a lifecycle effect. These are authoring declarations, not runtime object references.

Migrate every repository feature to complete declarations. Existing external registrations without declarations remain supported as explicitly opaque nodes/regions in the same executor. Preserve their existing invocation rules and conservatively route them. Print their unknown dependencies and exclude them from an order-independence guarantee. Do not silently infer missing edges or reject all existing callers at construction.

This compatibility boundary cannot be eliminated by a better sorting algorithm. Requiring complete metadata from every external feature would be an additional public authoring-contract change. Do not maintain a separate legacy scheduler to accommodate it.

### 5.2 Compile once; execute only eligible work

Build the plan during registration/finalization:

1. Assign stable node identities and type identities; freeze declarations.
2. Connect fact producers to fact readers. Label negative dependencies separately. Connect closed fact inputs to their output committers.
3. Mark published-state reads as reads of `S_n`; they do not imply a dependency on this loop's producer of `S_(n+1)`.
4. Detect strongly connected positive regions. Topologically sort the resulting acyclic graph of regions; use stable names/IDs for presentation tie-breaking, never to resolve conflicting values.
5. Validate cycles, absence constraints, duplicate output ownership, missing services, and unsafe access. Build typed fact-to-node routing and compact adjacency once.
6. Export the exact executable plan and its declared/opaque boundaries. The printable plan is not separately authored documentation.

A node becoming topologically available does not mean its entity has the required facts. The executor still waits for its match and preserves fired status. Repeated/late facts must wake eligible unfinished work without refiring completed entity transactions. Do not replace routed work with a scan of every ECS system on every frame.

Positive cycles are not sortable as individual nodes. Preserve supported insert-only cycles as explicitly printed closure regions, drained with a worklist and existing invocation guards. Do not repeatedly run arbitrary once-per-loop callbacks to manufacture convergence. If a rule requires replacement, retraction, or rereading a growing collection to converge, the strict single-fact model cannot certify it; diagnose that limitation.

Cycles involving absence are setup errors for the declared strict graph. Preserve current terminal `Without` behavior for supported features: seal host input and condition types, evaluate negative rules, drain allowed consequences, then commit. No new general multi-stratum negation engine is required.

### 5.3 Graph output

Provide an explicit diagnostic export, such as an additive concrete-simulation method writing text/DOT to a supplied writer. Final spelling is an implementation choice; do not add required methods to existing interfaces. Export can allocate outside execution. Do not build strings every frame.

Example of the content, not a promise that node ordering alone guarantees correctness:

```text
Published S_n.Ammo ----------------------+
Host SpendRequested -> ValidateSpend ----+-> SpendAccepted
SpendAccepted -> ResolveShot -> ShotResolved

Positive closure barrier
Terminal absence region (only when registered)
Final fact closure
  SpendAccepted -> Plan AmmoState
  ShotResolved  -> Plan WeaponState
All plans -> Validate -> Atomic publish S_(n+1) -> Journal -> Cleanup
```

Include edge reason/type, entity scope, output owner, closure-region membership, unknown edges, negative seals, and conflicts. A runtime snapshot adds the last completed/next system, closure region/pass, pending-work counts, budget stop reason. It does not export per-entity continuation cursors. Reuse existing numeric counters where possible.

Committers that read each other's previous state have no same-loop write/read ordering dependency. They all read `S_n`. A supposed `Commit A -> Commit B reads new A` stage is incompatible with the current contract; express that intermediate derivation as facts before final commit. The graph has multiple planning nodes and one publication boundary, not partially visible commit stages.

## 6. What order independence and idempotence actually require

Topological order removes hidden declared dependencies. It does not make arbitrary code confluent. The guarantee must name its scope: same admitted input set and published state, legal schedules and pause boundaries, equivalent final state/lifecycle outcome, excluding external side effects and unspecified journal ordering.

Creation also needs an explicit comparison rule. Independent creations can receive different recycled slot numbers under different legal schedules. Compare their logical relationships modulo fresh-handle renaming, or constrain allocation ordering if exact handle reproducibility is required. Do not use numeric slot IDs as domain ordering/tie-break inputs and then claim schedule independence.

Strictly declared rules require immutable input facts, deterministic equality, deterministic callbacks, explicit dependencies, and compatible unique outputs. Positive membership only grows. A callback using optional missing facts, arbitrary live entity enumeration, an incomplete batch aggregate, randomness, time, or shared mutable objects is not automatically order-independent.

For example, reading `Has<B>() == false` before a producer emits `B` is an absence dependency, even when written inside a positive callback. It must use the declared closed absence semantics or remain outside the guarantee. Multiple producers may propose the same equal fact; unequal proposals fail the loop. Do not accept one by scheduling preference.

Preserve accepted late host input until the current negative seal, including plan invalidation. Stable topology alone cannot make previously fired readers of incomplete collections correct when more input arrives. A strict rule may read already-required immutable facts plus `S_n`; a rule needing the complete fact collection needs a closure barrier. Existing batch callbacks retain their phase/entity-set behavior and are not silently moved to final closure. Mark incompatible collection readers as opaque/order-sensitive until explicitly migrated. Do not silently seal input at the first incremental call or restart fired callbacks to hide this issue.

Commit guarantees are separate:

- Planning is a deterministic, side-effect-free function of `S_n` and closed facts. Replanning produces equivalent decisions.
- Applying a prepared plan is guarded by its loop/plan identity and happens once. It contains at most one final action per entity/output; setting an equal value or deleting absent state emits no mutation.
- Committers do not reread partially written state, invoke external delivery, or mutate references shared with `S_n`.
- Replaying mutation observation remains allowed; exactly-once external delivery is not provided.
- Re-emitting an input in a later loop is a new input. `ammo := previousAmmo - spend` is not cross-loop replay-idempotent. Request-ID deduplication is a domain/API extension, not something topology supplies.

Prove successful-rule confluence by varying registration order, independent input order, ready-node tie order, and pause boundaries. Also assert that conflicting fact orders both fail before publication. Keep tests for opaque compatibility behavior separate from tests claiming confluence.

## 7. Transaction and lifecycle over the ECS

Retain this observable sequence:

```text
Idle -> Complete reduction systems -> Terminal absence system -> Close facts
     -> Complete planning systems -> Complete validation system
     -> Atomic apply + publish result/journal + exhaustive fact cleanup -> Idle

Yield only after a whole system returns; never inside finalization.
```

Preparation performs user callbacks, equality, capacity checks, entity-generation checks, final action deduplication, and all lifecycle deletion planning before writing durable components. A normal ECS command buffer is insufficient unless its playback has been proven to satisfy these conditions.

During apply, use already reserved ECS operations without callbacks, observer delivery, resizing, user equality, or recoverable errors. Finish state changes and slot release before consumers observe the result. The executor rejects reentrant input/disposal; this remains a single-threaded visibility contract, not a lock-free concurrent database.

If a candidate backend cannot support failure-free prepared application, do not hide partial mutations behind the adapter. Reject the candidate or bring a separate storage/publication design decision. Do not add rollback snapshots, a shadow world, or double-buffered components by default: they undermine the intended simplification and memory budget.

Pending-created entities are visible to reduction with their reserved handles. Pending-dead entities retain the existing closure visibility. Do not recycle or swap away an active iteration target during a system. Across yields, retain generation-checked work identities rather than a suspended dense-row iterator. Pre-apply failure rolls back staged lifecycle, invalidates abandoned new handles, preserves committed state, and cleans all accepted facts. Post-publication cleanup failure preserves published state, journal, and completed `LastResult`.

Clear the previous journal at the existing begin-loop boundary. Incomplete execution exposes no partial new journal. Facts/views remain borrowed; committers must not transfer borrowed fact resource ownership into output snapshots. Preserve the approved explicit `Dispose()` convention for allocation-free Mono facts. Do not change disposal of replaced/deleted/copied output states as a side effect of adopting an ECS.

## 8. Concrete simplification and memory targets

| Current implementation | Target | Removal gate |
| --- | --- | --- |
| Fact slabs, row counts, per-type multiplicity offsets/repacking | One transient component pool per fact type | Single-fact contract and ownership tests pass |
| State sparse storage plus separate entity-store mechanics | Backend component pools and one generation authority | Lifecycle/query/atomic-apply slice passes |
| `EntitySparseSet`, storage-only dense wrappers | Backend-owned mechanics; remaining scratch stays local to its owner | No remaining independent invariant requires them |
| Per-entity route-list objects and arrays | Static fact-to-node graph routing plus accepted-work bookkeeping | Equal continuation and cleanup behavior without route lists |
| Separate transactional/batch candidate machinery | One system execution plan; collection and callback loops finish within their system | Late input and batch boundary tests pass |
| Dense fired-marker matrices | One invocation-status representation selected from measured workload needs | No duplicate callbacks or dense replacement hidden in backend |
| Overlapping phase flags and reconciliation progress | One continuation between systems with loop/closure progress | All suspension/failure paths covered |
| Typed prepared actions and mutation journal | Retained as one commit owner | Required by public atomicity and observation contract |

Do not delete all buffers as a numerical goal. A worklist, continuation, prepared changes, and journal are necessary. Do not turn iteration indexes into ECS continuation components or wrap `List<T>` in a new general-purpose framework. ECS storage changes alone will not remove scheduler state.

Measure current and prototype memory with identical schema/reservations: empty legacy construction; empty bounded 512-slot construction; zero-live-entity production schema; 512 active entities; suspended loop; peak commit; churn; teardown. Separate retained managed memory, reserved element bytes, transient setup allocations, and native allocations. Do not report Unity process working set as engine heap size.

Report bytes and owner/object counts for component pools, sparse indexes, entity metadata, worklist, invocation guards, prepared actions, journals, and backend query caches. In particular, the current 512-entity/602-registration workload reserves 1,232,896 element bytes for fired markers. Do not remove that class while recreating the same matrix elsewhere and call it a memory improvement.

Single-fact storage removes the per-entity multiplicity multiplier. It does not remove sparse indexes, fact/state payloads, or snapshot journal costs. Do not promise a speedup or smaller heap before the prototype measures both.

Acceptance requires fewer independent storage/scheduling invariants and no duplicate worlds/schedulers. Count removed Cascade maintenance code against new adapter and dependency integration code, not merely moved files. If the backend adds more orchestration than it replaces, stop that migration and revise the choice.

## 9. Implementation sequence and verification gates

### P0 — Lock requested semantics on the current engine

Lock admission/capacity behavior first with the smallest viable slice. Use the clarified system boundary as the execution acceptance rule; do not build a temporary callback-level continuation framework in P0 just to replace it during P1/P3. Existing final-state, lifecycle and ownership regressions are the baseline; exact old yield locations are not. Add duplicate conflict tests and `MaxEntities == 0` tests; migrate all in-repo multiple-fact examples/tests to intentional single-fact inputs. Preserve the compile-only public contract fixture. No ECS dependency is needed to settle these rules.

Verify positive cap, reuse after destruction, pending cap exhaustion, unlimited logical mode with finite reservation, reservation exhaustion without partial acceptance, duplicate equality/no-op across pauses, unequal duplicates across producers, ownership on failure, and fixed-mode first-use/steady allocations. Old `All<T>()` and constructor call sites must still compile.

### P1 — Select backend and prove one vertical slice

Use one feature with two dependent reducers and one output: input -> accepted fact -> resolved fact -> state. Read another entity's published state; create a child, remove its output, destroy it, and reuse its slot in subsequent loops. Execute each reducer system over multiple entities, then have the existing budget check stop after it returns. Prove the next call starts the next system and publication remains deferred; never require a pause partway through planning. Include a positive seeded cycle and a terminal absence branch as focused extensions to the same slice.

Inject unequal fact proposals, throwing equality, capacity exhaustion before publication, and resource cleanup failures. Print the actual dependency plan. Compare with the current engine after P0. The selected backend must pass lifecycle, borrow, allocation, and publication requirements before any broad migration.

### P2 — Migrate storage completely

Move fact/state/entity ownership into the chosen backend. Adapt public views and typed journals, then delete superseded slabs/sparse stores and route-list ownership. Keep one runtime source of truth. Verify retained public outcomes and measured memory. Old callback-level pause counts are migrated to the system boundary; they are not a storage-backend compatibility gate.

### P3 — Compile declarations and unify execution

Migrate repository features to typed read/produce/scope declarations. Compile/export topology, explicit closure regions, and terminal absence constraints. Move all callback kinds and reconciliation into the one incremental executor. Preserve opaque external compatibility with the same executor. Delete old phase/candidate paths once retained public outcomes and completed-system execution tests pass; do not demand parity of obsolete yield positions.

Verify seeded cycles, invalid negative cycles, undeclared reads/emits in strict diagnostic mode, unrelated registration scaling, late required facts, late host input, stale plan invalidation between systems, complete batch execution before yielding, sealed input, and invocation counters at system boundaries. Access validation must not use runtime reflection or add hot-path allocations; mutable references hidden inside user values remain an explicit unsupported escape hatch for the strict guarantee.

### P4 — Validate the complete loop and document the boundary

Run public contract compilation, Unity import, full EditMode tests, Rider/MSBuild, and an IL2CPP/player smoke and profiling slice using the selected dependency. Use the positive-controlled Unity allocation recorder; the prior Mono per-thread byte counter is not valid evidence here. Measure first representative tick and steady 512-entity workloads, many unrelated registrations, churn, suspended execution, and commit/cleanup tails.

Publish the memory inventory, before/after maintenance inventory, graph examples, ownership rules, single-fact migration guide, capacity policy, and exact budget guarantee. No skipped required tests or unmeasured claims of Entitas throughput parity. Graph-documented ordering is not a substitute for confluence tests.

## 10. Decisions that must remain visible

| Issue | Position in this proposal |
| --- | --- |
| Duplicate value | User decided: identical no-op, unequal throws |
| Public signatures versus multiplicity | Keep signatures; explicitly migrate requested behavior change |
| Unlimited pool versus zero allocation | No logical ceiling; finite explicit reservation, no guaranteed in-loop growth |
| Execution and budget boundary | Complete an ECS system, then check the slice budget |
| Direct writable ECS components | Internal capability; unrestricted public runtime writes would require a new authority contract |
| Missing producer/read metadata in old features | Add declarations, migrate repo; print external opaque regions honestly |
| Topology versus cycles/late collection readers | Closure regions and explicit limits; never silently reorder/reinvoke old callbacks |
| ECS library choice | Required measured selection gate; no dependency or custom replacement selected here |
| Commit stages | Multiple planning nodes, one atomic published-state boundary |

These are not reasons to continue polishing custom storage indefinitely. They are the boundaries the storage spike and execution refactor must satisfy. Complete the agreed portions without broadening the public contract implicitly.

## 11. Test suites and support matrix

Status: test cleanup implemented; P0/P1/P3 runtime changes remain pending. The completed-system clarification in section 2.3 supersedes old callback-level pause expectations in the inventory below. Passing the current suite does **not** prove the proposed single-fact, zero-ceiling, new backend, or dependency-plan behavior. The current runtime still accepts distinct values of one fact type where capacity permits. This pass removes obsolete requirements without implementing new runtime semantics or adding ignored placeholder tests.

### 11.1 Suite boundaries and selection rules

| Assembly / folder | Responsibility | Boundary |
| --- | --- | --- |
| `CascadeEngine.Tests.Public` / `Assets/Tests/Public` | Public API compilation, callback behavior, state, lifecycle, ownership, journal and allocation acceptance | No `InternalsVisibleTo`; cannot inspect package internals |
| `CascadeEngine.Tests.Internal` / `Assets/Tests/Internal` | Storage preflight, borrowed-view validity, routing efficiency, route release and allocation-instrument controls | The only friend assembly; rewrite/delete cases when their implementation owner disappears |
| `CascadeEngine.Tests.Support` / `Assets/Tests/Support` | Shared `GC.Alloc` event recorder | No tests, package-runtime reference, or friend access; neither suite references the other |

Public fixtures use the `PublicContract` category. Hestia fixtures use `SampleIntegration`: they exercise the package through public APIs, but ammo, epsilon and audio-cue rules belong to the example, not the reusable package. Internal fixtures use `Internal`. Unity Test Runner can select each assembly/category; the existing kiss-unity-mcp EditMode task runs both assemblies together.

`PublicContractFixture` is a **compile-only** consumer/`IFactSimulation` adapter in the public assembly. It is not a discovered NUnit test and is not complete ABI coverage. Keep old interface implementations compiling without new mandatory members. Publicly accessible builders, `FactGuardrails`, and callback interfaces physically stored under `Internal/` remain public API; filesystem location is not access control.

A test earns its place by identifying a supported caller outcome or a distinct internal corruption/leak/performance failure. Do not copy every public lifecycle/ownership case into the internal suite. Callback counters are legitimate for once-per-eligible-entity/batch contracts; scheduler cursor values and candidate counts are not public outcomes. Exact public `SimulationResult` counters remain meaningful. Allocation assertions count events, not bytes, and require an instrument positive control. Throughput and heap-layout measurements are separate evidence, not unit-test pass criteria.

### 11.2 Current executable public cases

All methods below exist after cleanup. Their supported outcomes are retained; exact old yield locations/counts are subject to the migration notes in section 11.3. Parameterized methods list one name; NUnit expands their declared cases. This is the current inventory, not a requirement to reproduce every assertion against the backend.

**[CascadeTypeIdTests](../Assets/Tests/Public/CascadeTypeIdTests.cs)** — Public identity routing, collision rejection, repeatability and integer round-trip. No particular hash integer or algorithm is prescribed.

- `ValidTypeIdsRouteReducersAndCommitters`
- `DuplicateFactIdsFailDuringFeatureValidation`
- `DuplicateOutputIdsFailDuringFeatureValidation`
- `TypeNamesProduceRepeatableDistinctIdentities`

**[FactSimulationAtomicCommitTests](../Assets/Tests/Public/FactSimulationAtomicCommitTests.cs)** — Failure atomicity across output/entity order, exact configured capacity, lifecycle and replayable mutation observations.

- `EqualityFailurePreservesEveryOutputAndLifecycle`
- `ExactCapacityReplacementAndDeletionPublishReplayableFinalChanges`

**[FactSimulationDisposalTests](../Assets/Tests/Public/FactSimulationDisposalTests.cs)** — Public fact/state/registration ownership on successful closure and disposal, terminal rejection, external feature disposal and composed-feature ownership. Exceptional cleanup is separately covered by `FactSimulationOwnershipTests`.

- `AcceptedFactsAreDisposedWhenTickFactStoreClears`
- `DisposeDisposesQueuedFactsExactlyOnce`
- `DisposeAfterTickDoesNotDisposeFactsAgain`
- `DisposeDisposesCurrentOutputStateExactlyOnce`
- `DisposeIsTerminalAndRejectsPublicSimulationUse`
- `DisposingFeatureExternallyRejectsSimulationUse`
- `ComposedFeatureTransfersOwnershipAndDisposesRegistrationsOnce`

**[FactSimulationIncrementalTests](../Assets/Tests/Public/FactSimulationIncrementalTests.cs)** — Closure visibility, cumulative progress and actionable budget/depth failure context.

- `IncrementalTickDoesNotCommitUntilReductionCloses`
- `FullTickBudgetFailureIncludesActionableContext`
- `CausalDepthFailureIncludesEmittedFactAndReducerContext`

**[FactSimulationOverhaulTests](../Assets/Tests/Public/FactSimulationOverhaulTests.cs)** — Continuation, valid late input, negative sealing, configured limits and allocation acceptance. These tests no longer inspect eligibility counters, candidate bytes, capacity snapshots or timing metrics. The legacy growth case covers the existing explicitly allocating constructor path; it is not evidence for zero-ceiling mode.

- `ReconciliationSuspendsWithoutPublicationAndReplansOnlyAcceptedInput`
- `FullTickReconciliationTimeFailurePreservesSnapshot`
- `NegativeSealingSurvivesReconciliationPause`
- `OnlyEligibleCallbacksRunWhenResumingWithOneWorkItem`
- `LateRequiredFactCompletesEligibilityWithoutRefiringCompletedEntities`
- `SettingsLimitsCannotBeRelaxedByPerCallOverrides`
- `LegacyEntityGrowthPreservesBatchEligibilityAndMembershipBoundary`
- `FixedCapacityEntityChurnAllocatesNothingAndRetiresHandles`
- `Fixed512EntityRoutedPipelineHasZeroSteadyStateAllocation`

**[FactSimulationOwnershipTests](../Assets/Tests/Public/FactSimulationOwnershipTests.cs)** — Distinct ownership boundaries: failed admission, reduction, planning, post-publication cleanup, suspended-loop disposal and registration teardown. Registration disposal is a public callback contract and stays here; static cache release is checked internally. Resource facts in separate entities avoid dependence on multiplicity.

- `AdmissionFailureLeavesPayloadCallerOwnedAndNextTickClean`
- `ThrowingFactDoesNotPreventOtherFactsFromClearingOrRepeatDisposal`
- `CleanupFailureAfterCommitPreservesStateAndMutationJournal`
- `FailedReductionPreservesOriginalErrorAndFinishesCleanup`
- `IncrementalPauseKeepsFactsAliveUntilTerminalDispose`
- `TerminalCleanupFailureStillDisposesOwnersAndRejectsFurtherUse`
- `FeatureTeardownVisitsEveryRegistrationAndChildAfterCallbackFailures`
- `ExternalFeatureDisposalStillAllowsTerminalSimulationDisposal`
- `FailedCommitDisposesFactsAndDiscardsEarlierQueuedDecisions`
- `FactDisposerCannotEmitOrRecursivelyTickOrTearDownTheSimulation`
- `DuplicateAndRetiredEntityRejectionsDoNotAcquireAnotherResourceLease`
- `ResourceFactPipelineHasZeroAllocationsFor512EntitiesAfterWarmup`

**[FactSimulationStateReducerTests](../Assets/Tests/Public/FactSimulationStateReducerTests.cs)** — Published-state eligibility/query visibility, deferred lifecycle, generation safety, resumed work and cumulative guardrails. Entity reuse checks handles through public operations, not reserved-array lengths.

- `CommittedActiveStateTriggersOnlyEligibleEntityAndCanQueryOtherEntity`
- `StateReducerRequiresItsTriggerStateToBeRegisteredAsOutput`
- `StateReducerCanBeRegisteredBeforeTriggerOutputInSeparateSubFeature`
- `StateCreatedAtCommitTriggersNextTickAndDeletedStateStopsFutureTicks`
- `WorkBudgetResumesStateReducersWithoutDuplicateInvocationOrPartialCommit`
- `WorkBudgetResumesRemainingReducersForTheSameFact`
- `FailedFullTickRollsBackReducerSideDestruction`
- `IncrementalDestructionKeepsCommittedStateUntilClosureThenPublishesOneDelete`
- `EmittingDeadFactRunsLifecycleReducersBeforeDeletingDurableState`
- `UnifiedSettingsBoundConcurrentEntitiesAndReuseGenerationalIds`
- `UnifiedSettingsEnforceCumulativeTickWorkAcrossIncrementalSteps`
- `ReducerCreatedEntityCanReceiveFactsAndCommitStateInSameTick`
- `FailedTickInvalidatesReducerCreatedEntityGeneration`
- `WarmedStateTriggerPathHasZeroSteadyStateAllocations`

**[FactSimulationTransactionalReducerTests](../Assets/Tests/Public/FactSimulationTransactionalReducerTests.cs)** — Required-fact arities, entity/batch eligibility and terminal absence. The arity case omits each of five required positions, then supplies all inputs; it exercises three/four/extended registrations for both callback types instead of asserting registry arrays. Existing pair tests cover two-input registrations.

- `GenericAndExtendedRegistrationsWaitForEveryRequiredFact`
- `EntityScopedTransactionalReducerRunsOnceWhenTwoRequiredFactsExist`
- `BatchTransactionalReducerReceivesOnlyEligibleEntities`
- `BatchTransactionalReducerFiresOncePerEntityWhenEntitiesBecomeEligibleOnDifferentPasses`
- `ForbiddenFactSuppressesNegativeReducerRegardlessOfArrivalOrder`
- `NegativeReducerRunsAfterPositiveClosureWhenForbiddenFactIsAbsent`
- `ForbiddenFactDerivedByLaterPositiveReducerSuppressesNegativeReducer`
- `IncrementalNegativePhaseSealsLateHostInputUntilClosure`
- `WorkBudgetResumesBetweenNegativeReducersWithoutDuplicateInvocation`
- `ContradictoryNegativeRegistrationFailsDuringFeatureConstruction`
- `StaleEntityCannotInjectForbiddenFactIntoReusedSlot`
- `NegativeReducerCannotMutateAnotherNegativeCondition`

**[FactSimulationWarmupTests](../Assets/Tests/Public/FactSimulationWarmupTests.cs)** — Two distinct setup paths: explicit Warmup hints and settings-based reservation. Both use a representative 512-entity pipeline; first-use, suspended execution and steady-state allocations are measured. Exact buffer shapes are deliberately not preserved.

- `ExplicitWarmupSupportsRepresentativeTickWithoutAllocations`
- `WarmupMeasuresFirstUseAndSteadyStateAllocationsForRealisticEntityCount`

**[HestiaGameContextTests](../Assets/Tests/Public/HestiaGameContextTests.cs)** — Example input-to-mutation and host-adapter behavior. Domain-independent disposal cases live in `FactSimulationDisposalTests`; each context test disposes its simulation.

- `FireWeaponReducesRequestAndCommitsAmmoOnce`
- `AmmoEmptyTransitionPublishesAmmoAndDryFireCueMutations`
- `DuplicateFactsAreDeduplicatedWithinOneTick`
- `MissingAmmoStateSkipsSpendAndCreatesNoDefaultState`
- `DeadFactSuppressesAmmoSpendRegardlessOfArrivalOrder`
- `SingleMovePublishesTypedMutation`
- `PositionWithinEpsilonDoesNotPublishMutation`
- `RelevantFootstepPublishesMarkerEachTick`
- `NonRelevantFootstepDoesNotEmitFactOrMutation`
- `DestroyedSlotIsReusedWithNewGenerationWithoutAcceptingStaleFacts`
- `ForeignOutputDescriptorIsRejected`
- `UnknownEntityFactsAreRejectedBeforeEnteringTheQueue`

**[HestiaLifecycleSliceTests](../Assets/Tests/Public/HestiaLifecycleSliceTests.cs)** — The thin vertical slice: cross-entity read/spawn, component deletion versus full destruction, rollback and caller-owned rejected resources. Fixed-capacity rejection is not yet proof of universal single-fact conflict rejection.

- `CrossEntityCreationComponentRemovalAndDestructionRemainSeparateTransactions`
- `PreparationFailureRollsBackReducerCreatedChildAndCleansAcceptedResourceOnce`
- `FixedFactCapacityRejectionLeavesPayloadCallerOwned`

**[OutputStateRouteTests](../Assets/Tests/Public/OutputStateRouteTests.cs)** — Independent simulations, output priority/ties between distinct fact types, one previous-state snapshot and absence-triggered Set/Delete/Unchanged behavior.

- `SameOutputStateTypeUsesSeparateBucketsPerSimulation`
- `RegistrationPrioritySelectsSameWinnerRegardlessOfFactOrder`
- `EqualRegistrationPriorityThrowsBeforeDurableWrite`
- `CommittersReadOnePreviousStateSnapshotRegardlessOfOutputRegistrationOrder`
- `OutputWithoutReconcilesSetDeleteAndUnchangedOncePerEntity`

### 11.3 Required public additions or extensions

These are **pending**, not passing tests. Extend an existing fixture when it already owns the behavior; do not add duplicate tests with a new migration name. Parameterize meaningful boundaries, not the Cartesian product of every entity, phase, reducer and option.

| Gate / case | Required observable assertions | Existing coverage to reuse |
| --- | --- | --- |
| P0 / logical ceiling and reservation | Negative maximum rejects setup; zero constructs with finite reservation and allows explicit idle reservation beyond the initial 64; positive maximum counts live and pending-created/pending-destroyed slots. Exhaustion leaves state/ownership/lifecycle unchanged. Released slots can be reused beyond the lifetime quota. Overrides cannot relax the ceiling. | Extend `UnifiedSettingsBoundConcurrentEntitiesAndReuseGenerationalIds`, fixed-capacity rejection and churn; only zero/reservation and pending boundaries need new coverage. Exact initial buffer sizes belong internally. |
| P0 / immutable fact admission | Absent `All`/`TryGetLatest` gives empty/false; accepted gives one immutable value; equal repeat adds no accepted work or cleanup owner; unequal repeat throws before acceptance and leaves the first payload intact. Cover idle input, an unsealed incremental pause and a reducer-origin conflict. A reducer failure rolls back the loop; an idle rejected input leaves the first accepted input available to finish. A new loop accepts a new value. | Extend deduplication, reconciliation and resource-ownership cases. Add the missing unequal-payload contract. |
| P0 / equality and ownership edge | Two independently owned reference-containing payloads that compare equal leave the duplicate caller-owned; unequal or throwing equality does not transfer ownership. Assert values, work and cleanup attempts, not hashes/reference identity. | Current duplicate resource test uses the same object; it does not cover a separate rejected lease or throwing equality. |
| P0 / legacy fact-capacity inputs | Settings, guardrails and hints accept historical positive multiplicity values but cannot enable a second value; zero/negative retain setup validation. `MaxFactsPerEntity` counts accepted types. Equal repeats do not consume it. | Extend configured-limit/admission fixtures; do not retain legacy multiplicity expectations. |
| P0 / admission versus negative seal | Uniqueness survives pauses and pending destruction. Host input remains forbidden after negative sealing, including equal input; a seal rejection is not evidence of duplicate handling. `DeadFact` remains idempotent and cannot permit slot reuse before publication. | Extend existing negative-seal and repeated-destruction tests; do not weaken the seal to test equality. |
| P1/P3 / full versus incremental execution | Run the same public workload through full and incremental entry points, including supported host adapters/extensions. Compare final state, lifecycle, journal contents and callback counts; full-call exhaustion fails closed and a fresh loop is usable. | Existing per-entry-point tests cover pieces; add the paired workload, not another implementation-core identity assertion. |
| Current contract / journal lifetime | Enumerate typed previous/next snapshots twice; begin the next loop and verify the old journal clears even if it pauses; no partial new changes appear. Equal Set and Delete of absent state produce no change. | Extend `ExactCapacityReplacementAndDeletionPublishReplayableFinalChanges` and reconciliation tests; replay counts alone do not verify snapshot contents. |
| Current contract / query and authority boundary | Exercise supported single/intersection state queries and fact queries, absent reads, foreign output rejection and stale-generation behavior. State queries read the published snapshot during pauses; fact queries expose accepted facts of the open loop. Reducers may emit/create/destroy through their context; commit/cleanup callbacks cannot escape durable-write authority or reenter tick/warmup/dispose. | Extend state/lifecycle and callback-guard fixtures only at missing boundaries. Do not test every public method in every phase when that method is not available there. |
| Current contract / output-resource ownership | Verify the documented ownership convention for current, replaced, deleted and journal-copied resource states; replay must not dispose snapshots. Do not make the engine own borrowed fact resources by copying them into output. | Current terminal-state disposal covers only the remaining current value. Add explicit replacement/deletion/journal cases under the existing convention before backend migration. |
| P3 / declared dependency behavior | For strictly declared deterministic features, vary registration/input order and pause boundaries; compare final state/lifecycle modulo newly allocated handle names. Seeded positive cycles terminate; invalid negative dependencies fail setup; complete-collection readers wait for closure. Missing metadata remains supported as opaque compatibility behavior. | Existing negative/input-order cases remain. Add these only when declarations/plan exist; do not infer confluence for opaque callbacks. |

Migration smoke cases: replace removed ammo-fold/move-conflict expectations with admission-time conflicts through `HestiaGameContext` when P0 lands. The general ownership/conflict matrix belongs in package public tests; the Hestia smoke only proves those input adapters propagate it.

The existing `SlowCommitter.Delay` tests cover failure and visibility with the current executor. Preserve those outcomes during migration; do not turn their delay mechanism into a new system timing policy or another test matrix.

#### System-boundary audit and test migration

The earlier matrix overconstrained the executor. The current `PartialSimulation` stores pending-fact/reducer, candidate-routing and batch-collection positions; `ReconciliationPlan.TryPrepare` checks time inside its entity/fact/output/action loops. These are current implementation facts, not requirements of the replacement. No ECS executor has been implemented by the test cleanup.

| Current case or assertion | Support decision for system execution |
| --- | --- |
| `WorkBudgetResumesStateReducersWithoutDuplicateInvocationOrPartialCommit` required exactly four calls for three entities | Removed that assertion in this audit. Keep final state, one invocation per eligible entity and no partial publication. All eligible entities of one system must run before it yields. |
| `IncrementalTickDoesNotCommitUntilReductionCloses` asserts exact processed-fact counts on each of three calls | Keep cumulative final counts and publication isolation; migrate per-call counts/stop messages to completed systems. A system may process more than one fact before the first yield. |
| `IncrementalPauseKeepsFactsAliveUntilTerminalDispose` uses two entities of one reducer to force a pause | At migration, use two actual systems with the existing budget check stopping after the first. Keep lifetime/cleanup assertions; do not preserve an intra-system yield to make this fixture pause. |
| `WorkBudgetResumesRemainingReducersForTheSameFact` and `WorkBudgetResumesBetweenNegativeReducersWithoutDuplicateInvocation` | Distinct registrations can be distinct systems, so between-system resume coverage remains useful. Use system completion and once-only callbacks as the oracle; do not promise an extra empty completion call or a fixed callback ordinal as a yield point. |
| Reconciliation time, late-input, lifecycle and negative-seal fixtures | Keep previous-state visibility, valid input/replanning, lifecycle ownership and sealing. Arrange pauses after real reduction/planning/validation systems; never force an inner planning cursor or preserve a seal's old incidental call number. |
| `LegacyEntityGrowthPreservesBatchEligibilityAndMembershipBoundary` requires four batch calls | Preserve each registration's declared membership boundary and once-per-entity behavior. Review the exact grouping against whole-system execution; neither batch grouping nor extra calls may be changed accidentally to manufacture pause points. |
| Routed workload and allocation tests use `MaxWorkItems = 1` | This is a stop threshold, not a promise that a slice processes exactly one entity. Keep cumulative callback counts, unrelated-work isolation and zero allocations; assert no exact slice count. |

Minimal executor evidence: system A finishes all of its work before the existing budget check runs. If that check stops execution, B has not started; the next call starts B without repeating A. Apply the same assertion to batch collection plus its callback. Reuse existing closure, failure, ownership, late-input and allocation tests. No timing-exception cases, extra timing instrumentation or inner-loop pause hooks are required.

Preserve published-state visibility and final results. Migrate historical per-call counts to the system boundary instead of adding machinery to reproduce them.

### 11.4 Current executable internal cases

Keep only the following implementation risks. These tests can be replaced with selected-backend adapter tests at P1/P2; old type names, row positions and capacities are not compatibility requirements.

**[AllocationProbeTests](../Assets/Tests/Internal/AllocationProbeTests.cs)** — Positive/negative instrument controls and actual reserved create/replace/publication/journal-clear work. No constructor-allocation accounting or claims about CLR default-interface dispatch.

- `AllocationRecorderDetectsKnownArrayAndIgnoresEmptyWork`
- `ReservedStatePublicationAndJournalClearAllocateNothing`

**[EntitySparseSetTests](../Assets/Tests/Internal/EntitySparseSetTests.cs)** — Swap-back/generation coherence and preflight rejection before durable writes. Removal is parameterized for first/middle/last. State and journal exhaustion are different failure points.

- `SwapBackRemovalRepairsMovedMembershipAndRejectsOldGeneration`
- `FailedPreparationDoesNotAcquireMembershipOrReplaceValues`
- `MutationCapacityFailureAndStaleActionValidationPrecedeAnyWrite`
- `StateCapacityFailureAfterEarlierPreparationLeavesBothBucketsUnchanged`

**[FactRoutingTests](../Assets/Tests/Internal/FactRoutingTests.cs)** — Compare eligibility work with zero versus 300 unrelated registrations, using a nonzero control; catches accidental global scans without freezing an exact counter value.

- `UnrelatedRegistrationsDoNotIncreaseEligibilityWork`

**[FactStorageTests](../Assets/Tests/Internal/FactStorageTests.cs)** — A view is borrowed before another entity forces growth and remains readable until closure. Frozen entity-capacity rejection acquires no membership. No multi-value slab behavior is required.

- `BorrowedFactViewSurvivesOtherEntityGrowthUntilClosure`
- `FrozenEntityCapacityRejectsBeforeAcquiringFactMembership`

**[RouteCleanupTests](../Assets/Tests/Internal/RouteCleanupTests.cs)** — Actual static fact/state bindings are released on terminal disposal, external feature disposal and a throwing payload cleanup. Public terminal rejection alone cannot prove this leak-prevention invariant.

- `TerminalDisposalUnbindsRoutesEvenAfterFeatureDisposal`
- `ThrowingFactCleanupStillUnbindsBothRoutes`

### 11.5 Required internal refactor cases

Add these with the owning implementation, not as speculative tests of an unselected ECS or a second copy of the public suite.

| Gate / owner | Internal invariant and failure being caught |
| --- | --- |
| P1 / backend adapter | Generational create/find/destroy, typed read/set/remove/intersection and swap-back iteration are coherent. Borrowed views survive supported callback operations; active iteration cannot skip a moved entity; the next system resolves retained handles against current generations, with no suspended row iterator. Reference-containing structs work without boxing/reflection. Existing backend unit tests need not be cloned unless the adapter changes the behavior. |
| P0–P1 / reservation | Zero starts with finite reserved capacity; reservation arithmetic detects overflow before changing membership/ownership. Queue/query/batch/prepared-action/journal space derives from actual reservation and work limits. Frozen execution does not resize. Test one boundary per independently reserving owner; avoid exact array-length snapshots. |
| P0 / admission bookkeeping | Accepted equality repeat changes neither revision nor work scheduling; conflict does not overwrite the existing slot. Closure, failure and disposal clear uniqueness for the next loop; suspension does not. Public ownership checks are reused, not duplicated. |
| P1 / prepared apply | Exercise preparation failures at generation, equality, state capacity and journal capacity before any writes. With a valid plan, instrument the adapter to prove apply does not call user equality/observers, allocate, resize or perform recoverable validation. Reject a backend lacking this property. Do not invent rollback from arbitrary halfway apply failure or OOM. |
| P3 / plan compilation and export | One test graph covers producer/read edges, published-state reads, positive closure regions, negative edges and opaque boundaries. Exported IDs/edges match the executable plan; no exact text ordering, private arrays or hash constants. Invalid declarations identify the offending registration. |
| P3 / candidate routing | Unrelated facts do not cause unrelated candidate scans. Late required input requeues only unfinished work; accepted late input invalidates prepared decisions; equal input does not. Keep an observable public callback regression plus one internal routing/invalidation check where necessary. |
| P3 / continuation | A system completes before the existing budget check. A stop leaves the next system unstarted; resuming starts it once without repeating completed work. Batch collection and callback finish together. Invocation guardrails remain cumulative; suspension alone does not consume passes. |
| P3 / phase authority | Allowed reducer emission/lifecycle commands still work. Forbidden callback reentrancy fails at the existing authority boundaries. Keep existing failure and cleanup checks; no additional system timing policy is required. |
| P2 / terminal release | After both normal and throwing cleanup, no backend-owned payload, static route or continuation retains a live owner; each acquired owner is attempted once. Verify adapter-owned references directly where possible; avoid nondeterministic GC/WeakReference collection deadlines. |

Performance evidence outside the unit suite: measure first-use and steady 512-entity ticks, unrelated registrations, churn, suspension, publication and cleanup on Unity/IL2CPP. Inventory backend pools, invocation guards, candidates, worklists, actions and journals by owner. Compare before/after heap and throughput with equivalent inputs; do not assert historical byte counts or derive Entitas parity from zero-allocation tests. Code review confirms there is one authoritative store and executor; counting class names cannot establish that.

### 11.6 Removed cases and rejected requirements

| Removed or rewritten check | Decision and replacement |
| --- | --- |
| `HestiaGameContextTests.DistinctAmmoFactsFoldIntoOneOutputMutation` | Delete: requires multiplicity explicitly removed by P0. Admission-time sample smoke is pending P0. |
| `HestiaGameContextTests.DistinctMoveFactsConflictAndDoNotCommit` | Delete: accepts unequal input and expects failure at commit. P0 moves rejection to admission. Distinct-fact-type output conflicts remain covered by output priority/tie tests. |
| `FactSimulationTransactionalReducerTests.NegativeReducerPreservesDistinctTriggerFactMultiplicity` | Delete: same obsolete multiplicity requirement in the negative phase. Keep absence/closure/once-only callback behavior. |
| `GenericAndExtendedTransactionalRegistrationStoresRequiredFacts` | Replace with missing-input/complete-input callback tests; do not move private array lengths into a permanent internal suite. |
| `EntitySparseSetTests.SparseMetadataReservationMatchesTheFormerThreeArrayLayout` | Delete: historical comparison only asserted capacities; printed arithmetic was not a heap measurement. |
| Four dense helper tests (`DenseEntitySetTracksEachEntityOnceAndClearsMembership`, `DenseEntityCounterClearsOnlyTouchedEntities`, `DenseEntityObjectStoreCreatesOnceAndRespectsPreCapacity`, `EntityRefBufferRespectsPreCapacityAndCreatesQueryResultView`) | Delete: mirror the old helper operations/reservation shape. Retained engine eligibility, query, warmup, churn and swap-back tests protect the relevant failures. |
| `FactBucketTypedSlabsPreserveSpansAcrossGrowthAndReuse` | Replace with a real outstanding-borrow test across other-entity growth. The old test acquired spans only after growth and required two distinct values. |
| `FactBucketFixedSlabRejectsUnexpectedGrowthBeforeWrite` | Replace with frozen entity-capacity preflight; public fixed fact-capacity rejection still covers caller ownership. Do not preserve multi-value slab capacity semantics. |
| `AllocationProbeTests.DisposalProbeDistinguishesInheritedAndExplicitNoOpImplementations` | Delete: logged inherited dispatch but only asserted explicit no-op allocation. It did not prove the distinction in its name. Keep full public resource pipeline allocation coverage. |
| `PreparedStorageAllocationDiagnosticsSeparateCreateReplaceAndClear` | Replace fragmented cold-constructor measurements with actual reserved create and replace apply plus journal clear. Assert both zero events and resulting state. |
| `NameTokensCreateDeterministicNonEmptyIntIds` | Keep repeatable/distinct identity, nonempty and public integer round-trip behavior under a better name. These are public operations; no need for a second hash test or a specific hash integer. |
| Registry counts, capacity snapshot fields, candidate bytes, exact slot bounds and internal timing logs in public fixtures | Remove. Preserve public ownership/result/allocation assertions, one internal routing comparison and actual route-release checks. Do not recreate every removed metric assertion internally. |
| Ownership/reconciliation scenarios using two unequal facts on one entity incidentally | Use separate entities. The failure, cleanup and late-input scenarios remain supported independently of multiplicity. |

The previous matrix also contained false requirements; do not carry them forward:

- **Reject emit/create/destroy in every executor phase:** reducers are explicitly allowed to perform them. Test the actual authority matrix.
- **Pause/resume inside a system, including collection, preparation, validation, apply or cleanup:** contradicts section 2.3. Only complete systems provide yield boundaries; finalization remains indivisible. Do not introduce per-action systems to work around this rule.
- **Rollback any failed apply:** section 7 requires failure-free prepared apply and rejects a backend that cannot provide it. Test preflight failures and absence of callbacks/allocations, not an unimplemented shadow transaction.
- **All registration/input permutations are confluent:** only fully declared deterministic rules qualify. Opaque callback order and raw newly allocated entity numbers are outside that guarantee.
- **Zero ceiling, revisions and buffer sizes are all public assertions:** zero ceiling is public behavior; exact reservation shape and revision invalidation are internal evidence.
- **Every new bullet needs another test:** extend the existing owner fixture when the failure is already represented. No exhaustive API-guard Cartesian products, schema snapshots or permanently retained migration comparison harness.

### 11.7 Verification gates

- Cleanup gate: compile the public assembly without friend access; import the changed assembly/asset layout; run both EditMode assemblies with no skipped/inconclusive cases. `PublicContractFixture` is included in compilation, not NUnit totals.
- P0 gate: add the pending fact and ceiling cases while implementing those semantics. Passing the cleaned current suite is not P0 completion.
- P1 gate: run the Hestia lifecycle thin slice, paired full/incremental system execution and backend reservation/borrow/preflight checks before broad migration.
- P2 gate: run all public cases and selected-adapter internal tests; delete old storage tests with their owners, without preserving a second runtime.
- P3 gate: add dependency/opaque/closure and completed-system continuation coverage. Migrate callback-level pause assertions; do not keep an old executor to satisfy them. Existing real-clock tests are not proof of system-level stepping.
- P4 gate: Unity import, both suites, compile-only adapter, Rider/MSBuild, IL2CPP/player smoke and measured memory/throughput evidence. No skipped test or printed diagnostic substitutes for evidence.

Cleanup verification (2026-09-07): baseline 111/111 passed; separated suite 110/110 passed (96 public/sample, 14 internal), zero skipped or inconclusive. Package changes in this pass are limited to the friend-assembly name. No backend, single-fact or zero-ceiling implementation was changed. Logs remain untracked under `Logs/kissunitymcp`.

System-boundary audit (2026-09-07): corrected the design and test migration requirements; removed the exact four-call state-reducer assertion. Fresh EditMode verification: 110/110 passed, no skipped/inconclusive cases. The runtime still uses the existing fine-grained executor; complete-system execution is a pending implementation requirement, not a result proven by this run.

## 12. Reference material

- [Bevy ECS storage](https://docs.rs/bevy_ecs/latest/bevy_ecs/storage/index.html): distinguishes table storage from sparse sets. Use as a mechanics reference, not evidence of C# allocation behavior.
- [Bevy schedule graph](https://docs.rs/bevy_ecs/latest/bevy_ecs/schedule/struct.ScheduleGraph.html): dependency topology and conflicting access are explicit schedule concepts. This does not establish confluence of user reducers.
- [Bevy schedule module](https://docs.rs/bevy_ecs/latest/bevy_ecs/schedule/): topologically ordered execution metadata; inspiration for compiling and inspecting the plan.
- [Entitas component semantics](https://github.com/sschmid/Entitas/wiki/Components): one component per entity/type with add/replace/remove APIs. Cascade deliberately disallows replacing an accepted fact during an open loop.
- [Current package contract](../Assets/CascadeEngine/Readme.md). The tests under `Assets/Tests` define executable compatibility and ownership checks.

External documentation is architectural reference material. Backend-specific compatibility, memory, stepping, and publication behavior must be verified locally before implementation is accepted.
