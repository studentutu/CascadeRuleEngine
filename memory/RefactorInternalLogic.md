# Refactor internal logic: sparse-set ECS and incremental dependency execution

Status: design proposal. This document does not change public contract.

Goal: to solve hidden ECS execution dependencies, support incremental reduction/incremental simulation and budget enforcement.

Issue:
Maintaining a second general-purpose storage engine is not optimal. Use a proven C# sparse-set ECS for entity/component mechanics; retain a small Cascade layer for facts, execution, and transactional publication. Ensure feature parity to EntitasECS (which already exists in the reduction loop).

This is the single active internal refactor plan. The [package README](../Assets/CascadeEngine/Readme.md) describes the current runtime contract; this document defines the target and migration. Preserve existing atomic publication, staged lifecycle, and exhaustive ownership cleanup through the refactor. Verify the target against the executable regression suite rather than historical progress reports.

## 1. Decisions and compatibility boundary

Implement the three user-requested changes first, before replacing storage or scheduling:

1. Expose the entity-pool ceiling through reduction-loop settings: `0` means no configured ceiling; a positive integer limits concurrent allocated slots.
2. Allow at most one accepted fact of each type per entity per full reduction loop, including all incremental calls. The user explicitly selected: identical repeats are no-ops; a different payload throws before acceptance.
3. Make resumable execution mandatory underneath every simulation entry point. Check the frame budget at system/invocation boundaries, and continue from the saved position next call.

Preserve existing public types, signatures, constructors, adapters, and supported callback interfaces. Public declarations under `Internal/` are included. Do not add mandatory members to interfaces that external adapters implement.

Public signature compatibility is not full behavioral compatibility: removing fact multiplicity is an explicitly requested behavior change. Document and migrate it instead of claiming an invisible refactor. The existing public read/write authority, mutation visibility, negative sealing, and cleanup rules remain unchanged unless a further decision explicitly changes them.

| Surface | Target behavior |
| --- | --- |
| `FactSimulation(feature)` and `FactSimulation(feature, settings)` | Retained; use the same executor and backend |
| `Emit<TFact>` | Same signature; one immutable value per entity/type/loop |
| `IEntityFactView.All<TFact>()` | Same borrowed span API; length is zero or one |
| `TryGetLatest<TFact>` | Same signature; returns the single accepted value |
| `State`, `GetState`, `TryGetState`, entity queries | Read the last published state |
| `CreateEntity`, `DestroyEntity`, `TryGetEntity` | Preserve generational handles and staged lifecycle semantics |
| `RunTickIncremental` | Primary execution mechanism; incomplete means resume the same loop |
| `RunTick` | Compatibility wrapper; closes within its supplied budget or fails as today |
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

### 2.3 Mandatory incremental execution

There is one execution state machine. Its continuation owns the current graph region, node, entity/work cursor, unfinished batch collection, commit-plan cursor, and loop identity. No coroutine, `Task`, thread-per-system, or enumerator allocation is required.

The ECS backend must expose individually invocable systems or storage/query access that lets Cascade invoke one system at a time. Reject a backend that only offers an opaque whole-frame runner. Native coroutine support inside the library is unnecessary if Cascade can retain the cursor outside it. Do not run an ECS scheduler and a Cascade scheduler over the same work.

```text
resume saved continuation
  -> check budget
  -> execute one existing callback unit or one bounded internal work unit
  -> save progress and charge counters
  -> check budget before starting another unit
  -> return incomplete, or finish publication and return complete
```

Keep the current callback units: immediate invocation, entity transaction/state invocation, and one existing batch callback. Entity enumeration and batch collection can pause before calling the callback. Splitting a batch callback changes its semantics and is not part of this refactor.

`MaxWorkItems` continues to count reducer invocations; collection, planning, validation, and cleanup do not silently become reducer work. Time checks cover resumable internal work as well. Suspension does not reset loop guardrails, fire markers, fact uniqueness, or consume a logical pass.

Keep full-call compatibility: `RunTick` uses the same stepping core but reports budget exhaustion as its existing failure behavior. It must not silently return a partial result, restart a fresh allowance repeatedly, or leave an undocumented open loop. Host incremental extensions must be audited so they do not silently use full execution for the concrete engine.

**Budget contract:** hard admission/count limits and a cooperatively enforced time deadline between atomic units. A 12 ms callback cannot be paused at 2 ms with the existing synchronous API. Atomic publication and resource cleanup also have non-preemptible tails. Record their durations and deadline overruns. An absolute frame-time ceiling would additionally require bounded/splittable callbacks and a different publication/cleanup protocol; this proposal does not claim it.

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
- Stable borrowed fact spans, or compatible validity boundaries, during a callback. Resizing or swap-back must not invalidate live views or saved continuation indexes.
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

Include edge reason/type, entity scope, output owner, closure-region membership, unknown edges, negative seals, and conflicts. A runtime snapshot adds current node/region, saved cursor, pending-work counts, budget stop reason, and longest atomic unit. Reuse existing numeric counters where possible.

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
Idle -> Reduce/Resume -> Terminal absence -> Close facts
     -> Prepare/Resume -> Validate/Resume -> Atomic apply
     -> Publish result/journal -> Exhaustive fact cleanup -> Idle
```

Preparation performs user callbacks, equality, capacity checks, entity-generation checks, final action deduplication, and all lifecycle deletion planning before writing durable components. A normal ECS command buffer is insufficient unless its playback has been proven to satisfy these conditions.

During apply, use already reserved ECS operations without callbacks, observer delivery, resizing, user equality, or recoverable errors. Finish state changes and slot release before consumers observe the result. The executor rejects reentrant input/disposal; this remains a single-threaded visibility contract, not a lock-free concurrent database.

If a candidate backend cannot support failure-free prepared application, do not hide partial mutations behind the adapter. Reject the candidate or bring a separate storage/publication design decision. Do not add rollback snapshots, a shadow world, or double-buffered components by default: they undermine the intended simplification and memory budget.

Pending-created entities are visible to reduction with their reserved handles. Pending-dead entities retain the existing closure visibility. Do not recycle or swap away a live iteration target behind a suspended cursor. Pre-apply failure rolls back staged lifecycle, invalidates abandoned new handles, preserves committed state, and cleans all accepted facts. Post-publication cleanup failure preserves published state, journal, and completed `LastResult`.

Clear the previous journal at the existing begin-loop boundary. Incomplete execution exposes no partial new journal. Facts/views remain borrowed; committers must not transfer borrowed fact resource ownership into output snapshots. Preserve the approved explicit `Dispose()` convention for allocation-free Mono facts. Do not change disposal of replaced/deleted/copied output states as a side effect of adopting an ECS.

## 8. Concrete simplification and memory targets

| Current implementation | Target | Removal gate |
| --- | --- | --- |
| Fact slabs, row counts, per-type multiplicity offsets/repacking | One transient component pool per fact type | Single-fact contract and ownership tests pass |
| State sparse storage plus separate entity-store mechanics | Backend component pools and one generation authority | Lifecycle/query/atomic-apply slice passes |
| `EntitySparseSet`, storage-only dense wrappers | Backend-owned mechanics; remaining scratch stays local to its owner | No remaining independent invariant requires them |
| Per-entity route-list objects and arrays | Static fact-to-node graph routing plus accepted-work bookkeeping | Equal continuation and cleanup behavior without route lists |
| Separate transactional/batch candidate machinery | One execution plan and shared work-selection mechanism with callback-specific cursors | Late input and batch boundary tests pass |
| Dense fired-marker matrices | One invocation-status representation selected from measured workload needs | No duplicate callbacks or dense replacement hidden in backend |
| Overlapping phase flags and reconciliation progress | One continuation with explicit phase-specific state | All suspension/failure paths covered |
| Typed prepared actions and mutation journal | Retained as one commit owner | Required by public atomicity and observation contract |

Do not delete all buffers as a numerical goal. A worklist, continuation, prepared changes, and journal are necessary. Do not turn every cursor into an ECS entity/component or wrap `List<T>` in a new general-purpose framework. ECS storage changes alone will not remove scheduler state.

Measure current and prototype memory with identical schema/reservations: empty legacy construction; empty bounded 512-slot construction; zero-live-entity production schema; 512 active entities; suspended loop; peak commit; churn; teardown. Separate retained managed memory, reserved element bytes, transient setup allocations, and native allocations. Do not report Unity process working set as engine heap size.

Report bytes and owner/object counts for component pools, sparse indexes, entity metadata, worklist, invocation guards, prepared actions, journals, and backend query caches. In particular, the current 512-entity/602-registration workload reserves 1,232,896 element bytes for fired markers. Do not remove that class while recreating the same matrix elsewhere and call it a memory improvement.

Single-fact storage removes the per-entity multiplicity multiplier. It does not remove sparse indexes, fact/state payloads, or snapshot journal costs. Do not promise a speedup or smaller heap before the prototype measures both.

Acceptance requires fewer independent storage/scheduling invariants and no duplicate worlds/schedulers. Count removed Cascade maintenance code against new adapter and dependency integration code, not merely moved files. If the backend adds more orchestration than it replaces, stop that migration and revise the choice.

## 9. Implementation sequence and verification gates

### P0 — Lock requested semantics on the current engine

Implement the three priority changes first with the smallest viable slice. Existing incremental behavior is the baseline to consolidate, not something to rebuild blindly. Add duplicate conflict tests and `MaxEntities == 0` tests; migrate all in-repo multiple-fact examples/tests to intentional single-fact inputs. Preserve the compile-only public contract fixture. No ECS dependency is needed to settle these rules.

Verify positive cap, reuse after destruction, pending cap exhaustion, unlimited logical mode with finite reservation, reservation exhaustion without partial acceptance, duplicate equality/no-op across pauses, unequal duplicates across producers, ownership on failure, and fixed-mode first-use/steady allocations. Old `All<T>()` and constructor call sites must still compile.

### P1 — Select backend and prove one vertical slice

Use one feature with two dependent reducers and one output: input -> accepted fact -> resolved fact -> state. Read another entity's published state; create a child, remove its output, destroy it, and reuse its slot in subsequent loops. Pause after each callback and during planning. Include a positive seeded cycle and a terminal absence branch as focused extensions to the same slice.

Inject unequal fact proposals, throwing equality, capacity exhaustion before publication, and resource cleanup failures. Print the actual dependency plan. Compare with the current engine after P0. The selected backend must pass lifecycle, borrow, allocation, and publication requirements before any broad migration.

### P2 — Migrate storage completely

Move fact/state/entity ownership into the chosen backend. Adapt public views and typed journals, then delete superseded slabs/sparse stores and route-list ownership. Keep one runtime source of truth. Verify full regression coverage and measured memory before changing scheduling semantics.

### P3 — Compile declarations and unify execution

Migrate repository features to typed read/produce/scope declarations. Compile/export topology, explicit closure regions, and terminal absence constraints. Move all callback kinds and reconciliation into the one incremental executor. Preserve opaque external compatibility with the same executor. Delete old phase/candidate paths only after parity tests pass.

Verify seeded cycles, invalid negative cycles, undeclared reads/emits in strict diagnostic mode, unrelated registration scaling, late required facts, late host input, stale plan invalidation, batch collection suspension, sealed input, and work-counter semantics. Access validation must not use runtime reflection or add hot-path allocations; mutable references hidden inside user values remain an explicit unsupported escape hatch for the strict guarantee.

### P4 — Validate the complete loop and document the boundary

Run public contract compilation, Unity import, full EditMode tests, Rider/MSBuild, and an IL2CPP/player smoke and profiling slice using the selected dependency. Use the positive-controlled Unity allocation recorder; the prior Mono per-thread byte counter is not valid evidence here. Measure first representative tick and steady 512-entity workloads, many unrelated registrations, churn, suspended execution, and commit/cleanup tails.

Publish the memory inventory, before/after maintenance inventory, graph examples, ownership rules, single-fact migration guide, capacity policy, and exact budget guarantee. No skipped required tests or unmeasured claims of Entitas throughput parity. Graph-documented ordering is not a substitute for confluence tests.

## 10. Decisions that must remain visible

| Issue | Position in this proposal |
| --- | --- |
| Duplicate value | User decided: identical no-op, unequal throws |
| Public signatures versus multiplicity | Keep signatures; explicitly migrate requested behavior change |
| Unlimited pool versus zero allocation | No logical ceiling; finite explicit reservation, no guaranteed in-loop growth |
| Hard deadline versus synchronous callbacks | Enforce between units; report overruns, no absolute ceiling claim |
| Direct writable ECS components | Internal capability; unrestricted public runtime writes would require a new authority contract |
| Missing producer/read metadata in old features | Add declarations, migrate repo; print external opaque regions honestly |
| Topology versus cycles/late collection readers | Closure regions and explicit limits; never silently reorder/reinvoke old callbacks |
| ECS library choice | Required measured selection gate; no dependency or custom replacement selected here |
| Commit stages | Multiple planning nodes, one atomic published-state boundary |

These are not reasons to continue polishing custom storage indefinitely. They are the boundaries the storage spike and execution refactor must satisfy. Complete the agreed portions without broadening the public contract implicitly.

## 11. Test suites and support matrix

The refactor is not complete when the old tests pass. The current tests mix three different concerns: the host-visible contract, implementation invariants, and allocation/profiling instrumentation. Split those concerns before migrating storage. A public-contract test must compile against the public package surface and must not inspect `Registry`, route caches, sparse-set rows, slab counts, candidate bytes, eligibility-check counters, or internal metrics. An internal test may use `InternalsVisibleTo` and the internal adapter, but it must not be used as evidence that the public API contract is preserved.

Keep `PublicContractFixture` as a compile-only fixture. It is the minimum source-compatibility check for `IFactSimulation`; it is not a runtime behavior test. The public suite must compile without relying on `Internal/` declarations. Add a separate internal test assembly/fixture for implementation tests instead of weakening the public boundary.

### 11.1 Public API and public-contract suite

These tests remain supported. Preserve their externally observable assertions and rewrite only the assertions that conflict with the approved single-fact or zero-ceiling decisions. Exact reducer-call counters are valid where they are exposed through `SimulationResult`; private reducer probe counters are only valid when they express a documented callback contract such as once-per-eligible-entity.

`OutputStateRouteTests`

- `SameOutputStateTypeUsesSeparateBucketsPerSimulation`
- `RegistrationPrioritySelectsSameWinnerRegardlessOfFactOrder`
- `EqualRegistrationPriorityThrowsBeforeDurableWrite`
- `CommittersReadOnePreviousStateSnapshotRegardlessOfOutputRegistrationOrder`
- `OutputWithoutReconcilesSetDeleteAndUnchangedOncePerEntity`

`HestiaLifecycleSliceTests`

- `CrossEntityCreationComponentRemovalAndDestructionRemainSeparateTransactions`
- `PreparationFailureRollsBackReducerCreatedChildAndCleansAcceptedResourceOnce`
- `FailedSlabPreparationLeavesRejectedResourceCallerOwned`

`HestiaGameContextTests`

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
- `AcceptedFactsAreDisposedWhenTickFactStoreClears`
- `DisposeDisposesQueuedFactsExactlyOnce`
- `DisposeAfterTickDoesNotDisposeFactsAgain`
- `DisposeDisposesCurrentOutputStateExactlyOnce`
- `DisposeIsTerminalAndRejectsPublicSimulationUse`
- `DisposingFeatureExternallyRejectsSimulationUse`

`DistinctAmmoFactsFoldIntoOneOutputMutation` is not supported after the approved contract change. Replace it with `DistinctAmmoFactsConflictBeforeAcceptance`: the second unequal value must throw during `Emit`, remain caller-owned, leave the first value present, and publish no partial state. Replace `DistinctMoveFactsConflictAndDoNotCommit` with the same admission-time assertion for the movement API. A commit-time conflict test remains useful only for distinct accepted fact types or incompatible output decisions; it must not depend on accepting two values of one entity/type.

The old `DisposeClearsFeatureRegistryAndDisposesRegistrations` test is split: its public part remains covered by `DisposeIsTerminalAndRejectsPublicSimulationUse`; registry counts, route-cache invalidation, and registration disposal move to the internal teardown suite below.

`FactSimulationTransactionalReducerTests`

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

`GenericAndExtendedTransactionalRegistrationStoresRequiredFacts` is internal-only: it tests the shape of registry arrays rather than the builder's public behavior. Replace it with public arity behavior tests that use two-, three-, four-, and `.And<TFact>()` registrations to produce the correct result.

`FactSimulationStateReducerTests`

- `CommittedActiveStateTriggersOnlyEligibleEntityAndCanQueryOtherEntity`
- `StateReducerRequiresItsTriggerStateToBeRegisteredAsOutput`
- `StateReducerCanBeRegisteredBeforeTriggerOutputInSeparateSubFeature`
- `StateCreatedAtCommitTriggersNextTickAndDeletedStateStopsFutureTicks`
- `WorkBudgetResumesStateReducersWithoutDuplicateInvocationOrPartialCommit`
- `WorkBudgetResumesRemainingReducersForAlreadyPoppedFact`
- `FailedFullTickRollsBackReducerSideDestruction`
- `IncrementalDestructionKeepsCommittedStateUntilClosureThenPublishesOneDelete`
- `EmittingDeadFactRunsLifecycleReducersBeforeDeletingDurableState`
- `UnifiedSettingsBoundConcurrentEntitiesAndReuseGenerationalIds`
- `UnifiedSettingsEnforceCumulativeTickWorkAcrossIncrementalSteps`
- `ReducerCreatedEntityCanReceiveFactsAndCommitStateInSameTick`
- `FailedTickInvalidatesReducerCreatedEntityGeneration`
- `WarmedStateTriggerPathAllocatesZeroBytesAtSteadyState`

`FactSimulationOwnershipTests`

- `AdmissionFailureLeavesPayloadCallerOwnedAndNextTickClean`
- `ThrowingFactDoesNotPreventOtherFactsFromClearingOrRepeatDisposal`
- `CleanupFailureAfterCommitPreservesStateAndMutationJournal`
- `FailedReductionPreservesOriginalErrorAndFinishesCleanup`
- `IncrementalPauseKeepsFactsAliveUntilTerminalDispose`
- `TerminalCleanupAttemptsEveryOwnerAndUnbindsStaticRoutesDespiteErrors`
- `FeatureTeardownVisitsEveryRegistrationAndChildAfterCallbackFailures`
- `ExternalFeatureDisposalCannotPreventSimulationFromUnbindingStateRoutes`
- `FailedCommitDisposesFactsAndDiscardsEarlierQueuedDecisions`
- `FactDisposerCannotEmitOrRecursivelyTickOrTearDownTheSimulation`
- `DuplicateAndRetiredEntityRejectionsDoNotAcquireAnotherResourceLease`
- `ResourceFactPipelineAllocatesZeroBytesFor512EntitiesAfterWarmup`

`FactSimulationOverhaulTests`

- `ReconciliationSuspendsWithoutPublicationAndReplansOnlyAcceptedInput`
- `FullTickReconciliationTimeFailurePreservesSnapshot`
- `NegativeSealingSurvivesReconciliationPause`
- `RoutedSchedulingIgnoresUnrelatedRulesAndResumesWithOneWorkItem` — retain the public assertions for relevant reducer/batch invocations and `ProcessedWorkItems`; remove `EligibilityChecks`, `CandidateReservedBytes`, and internal metric assertions from this test.
- `LateRequiredFactRequeuesIncompleteCandidateWithoutRefiringCompletedEntities`
- `SettingsLimitsCannotBeRelaxedByPerCallOverrides`
- `LegacyEntityGrowthPreservesPendingBatchRowsAndMembershipBoundary`
- `FixedCapacityEntityChurnAllocatesNothingAndKeepsRecycledSlotsBounded` — retain the black-box no-allocation and lifecycle assertions; move exact slot-index bounds to the internal suite.
- `Fixed512EntityRoutedPipelineHasZeroSteadyStateAllocation` — retain as a black-box performance acceptance test; move diagnostic metric/candidate-byte inspection to the internal suite.

`FactSimulationIncrementalTests`

- `IncrementalTickDoesNotCommitUntilReductionCloses`
- `FullTickBudgetFailureIncludesActionableContext`
- `CausalDepthFailureIncludesEmittedFactAndReducerContext`

`FactSimulationAtomicCommitTests`

- `EqualityFailurePreservesEveryOutputAndLifecycle`
- `ExactCapacityReplacementAndDeletionPublishReplayableFinalChanges`

`FactSimulationWarmupTests`

- `WarmupPreventsCapacityGrowthDuringRepresentativeTick` — retain the public warmup/no-allocation/result behavior; move exact capacity-snapshot field assertions to the internal suite.
- `WarmupMeasuresFirstUseAndSteadyStateAllocationsForRealisticEntityCount` — retain as the 512-entity black-box allocation gate. Update its input shape and expected counts for one fact per entity/type.

`CascadeTypeIdTests`

- `ValidTypeIdsRouteReducersAndCommitters`
- `DuplicateFactIdsFailDuringFeatureValidation`
- `DuplicateOutputIdsFailDuringFeatureValidation`

The current `NameTokensCreateDeterministicNonEmptyIntIds` test locks the hash/token implementation (`ToInt()` and nonzero representation), not the public identity contract. Replace its public coverage with equality/distinctness behavior if needed and move the exact token/hash assertions to the internal suite.

Required new public-contract cases for the approved P0 changes:

- `ZeroMaxEntitiesMeansNoLogicalCeilingWithFiniteInitialReservation`: construct with `MaxEntities == 0`, create entities beyond the initial reservation after an idle explicit reservation increase, and verify that zero does not mean preallocation of `int.MaxValue` or automatic in-loop growth.
- `PositiveMaxEntitiesRejectsConcurrentLiveAndPendingEntities`: the configured ceiling includes live entities and staged create/destroy reservations; rejection leaves lifecycle state, accepted facts, and ownership unchanged.
- `ReservationExhaustionIsRejectedBeforeFactAcceptanceOrPublication`: a finite reservation that is full rejects the operation before it can mutate the queue, durable state, lifecycle, or mutation journal.
- `RecycledEntitySlotDoesNotConsumeLifetimeQuota`: destroy and successfully close an entity, reuse its slot, and verify the new generation while stale handles remain rejected.
- `AllAndTryGetLatestExposeZeroOrOneAcceptedFact`: an absent fact returns an empty borrowed span/false; an accepted fact returns exactly one value; equal repeats do not add a second value.
- `EqualFactRepeatIsANoopAcrossIncrementalCalls`: repeat an equal fact after a pause and verify no new work, revision, plan invalidation, or second cleanup owner.
- `UnequalFactRepeatThrowsBeforeAcceptanceAcrossIncrementalCalls`: repeat an unequal fact after a pause and verify the accepted value remains unchanged and the proposed value remains caller-owned.
- `PublicMutationJournalIsReplayableAndClearsAtNextLoopBoundary`: incomplete execution exposes no partial new journal, completed mutations can be enumerated repeatedly, and the next loop clears the previous journal at its documented begin boundary.
- `RunTickAndRunTickIncrementalShareTheSameClosureAndPublicationContract`: the compatibility wrapper reaches the same final state as repeated incremental calls and fails closed when its supplied budget is exhausted.

### 11.2 Internal implementation suite

These tests are still valuable, but they must not be counted as public compatibility tests. Keep them in an internal assembly or under an explicitly named `Internal` test folder.

Existing tests to move or split:

- `EntitySparseSetTests.SparseMetadataReservationMatchesTheFormerThreeArrayLayout`
- `EntitySparseSetTests.SwapBackRemovalRepairsMovedMembershipAndRejectsOldGeneration`
- `EntitySparseSetTests.FailedPreparationDoesNotAcquireMembershipOrReplaceValues`
- `EntitySparseSetTests.MutationCapacityFailureAndStaleActionValidationPrecedeAnyWrite`
- `EntitySparseSetTests.StateCapacityFailureAfterEarlierPreparationLeavesBothBucketsUnchanged`
- `DenseEntityStorageTests.DenseEntitySetTracksEachEntityOnceAndClearsMembership`
- `DenseEntityStorageTests.DenseEntityCounterClearsOnlyTouchedEntities`
- `DenseEntityStorageTests.DenseEntityObjectStoreCreatesOnceAndRespectsPreCapacity`
- `DenseEntityStorageTests.EntityRefBufferRespectsPreCapacityAndCreatesQueryResultView`
- `DenseEntityStorageTests.FactBucketTypedSlabsPreserveSpansAcrossGrowthAndReuse`
- `DenseEntityStorageTests.FactBucketFixedSlabRejectsUnexpectedGrowthBeforeWrite`
- `AllocationProbeTests.AllocationRecorderDetectsKnownArrayAndIgnoresEmptyWork`
- `AllocationProbeTests.DisposalProbeDistinguishesInheritedAndExplicitNoOpImplementations`
- `AllocationProbeTests.PreparedStorageAllocationDiagnosticsSeparateCreateReplaceAndClear`
- `CascadeTypeIdTests.NameTokensCreateDeterministicNonEmptyIntIds`
- Registry counts and route-cache assertions removed from `HestiaGameContextTests.DisposeClearsFeatureRegistryAndDisposesRegistrations`
- Exact `CaptureCapacitySnapshot` assertions removed from `FactSimulationWarmupTests.WarmupPreventsCapacityGrowthDuringRepresentativeTick`
- `EligibilityChecks`, `CandidateReservedBytes`, exact slot-index bounds, and internal `Metrics` assertions removed from the public overload tests in `FactSimulationOverhaulTests`

Required new internal cases for the refactor:

1. **Entity backend and reservation**

   - Generational create/find/destroy, stale-handle rejection, generation increment, and retired-slot behavior.
   - Positive `MaxEntities` reservation rejects live, pending-created, and pending-destroyed capacity exhaustion before any ownership or lifecycle mutation.
   - `MaxEntities == 0` has no logical ceiling but starts with finite reservation; reservation exhaustion rejects before writing and reports the required capacity.
   - Explicit idle reservation growth succeeds; growth inside an open loop or reducer callback is rejected when fixed allocation is required.
   - Recycled slots do not consume a lifetime quota; sparse indexes and dense membership remain coherent after swap-back removal.
   - Queue, query, transaction, batch, commit, and journal reservation arithmetic detects overflow and does not use `MaxEntities * limit` when the logical ceiling is zero.

2. **Single-fact component storage and ownership**

   - One typed fact slot exists per entity/type/loop; `All<TFact>()` is always length zero or one.
   - Equal repeats across separate incremental calls are no-ops: no second slot, no new work, no input revision, and no plan invalidation.
   - Unequal repeats throw before acceptance regardless of producer/order; the accepted payload remains unchanged and the proposed payload remains caller-owned.
   - Uniqueness survives pauses, negative sealing, pending destruction, and plan invalidation.
   - Accepted payloads are cleaned once on success, failed reduction, failed preparation, failed apply, terminal disposal, and post-publication cleanup failure; one throwing disposer does not stop the remaining attempts.
   - Reference-containing fact values and throwing equality are covered without assuming hash identity or reference identity.

3. **Compiled plan and routing**

   - Stable node/type identity, producer-to-reader edges, output ownership, negative seals, closure regions, and opaque external nodes are exported correctly.
   - Registration order and independent input order do not change successful results; conflicting values fail in both orders before publication.
   - Positive cycles drain through the supported closure region; unsupported replacement/retraction cycles fail diagnostically.
   - Negative cycles fail during setup; a negative reducer cannot mutate another sealed negative condition.
   - Fact-routed candidate indexes visit only affected reducer/entity pairs; unrelated registrations produce zero candidate work and exact eligibility diagnostics remain internal.
   - Late required facts requeue only unfinished candidates; completed entity transactions do not refire.
   - Complete collection readers remain behind their declared closure barrier; opaque readers are not silently granted confluence.

4. **Continuation and budget state machine**

   - Every callback kind pauses and resumes from the saved node, entity, fact, batch, planning, validation, and commit cursor without skipping or duplicating work.
   - Budget counters are cumulative across incremental calls; `MaxWorkItems` counts reducer invocations only, while internal collection/planning/cleanup work is separately measurable.
   - Time checks happen between atomic units; callback, publication, and cleanup tails record deadline overruns without pretending they are preemptible.
   - `RunTick` uses the same continuation core and fails closed on budget exhaustion; it never restarts an allowance or leaves an undocumented open loop.
   - Reentrant emit/create/destroy/tick/warmup/dispose calls are rejected in every executor phase.

5. **Prepared publication and lifecycle mechanics**

   - Preparation validates all generations, equality, capacities, action deduplication, and lifecycle changes before the first durable write.
   - Prepared apply performs no callbacks, observers, resizing, user equality, or recoverable validation failures.
   - A failed preparation or apply leaves all output buckets, entity membership, lifecycle state, and mutation journals unchanged; accepted facts are still cleaned.
   - Pending-created entities can receive facts in the same loop; pending-destroyed entities remain visible through closure and are released only at finalization.
   - Journal records are replayable and non-consumptive; previous/next snapshots are not additional resource owners.
   - Feature teardown visits all registration nodes and sub-features after callback failures, clears maps/routes, and remains idempotent.

6. **Internal performance and memory evidence**

   - The allocation recorder has a positive control and reports first-use, steady-state, churn, suspended-loop, commit, cleanup, and teardown allocations separately.
   - The 512-entity routed workload records candidate storage, invocation guards, worklist, prepared actions, journals, backend pools, and query caches by owner/object count.
   - The migrated implementation does not recreate the former fired-marker matrix or a second authoritative entity/fact store under a different name.
   - Exact backend memory shapes are compared against the pre-migration baseline; no speedup or heap reduction is claimed from unit-test results alone.

### 11.3 Cases to delete, not migrate

The following cases validate behavior that is explicitly no longer supported or are duplicate implementation tests with no public value:

- `HestiaGameContextTests.DistinctAmmoFactsFoldIntoOneOutputMutation` — superseded by admission-time single-fact conflict behavior.
- `FactSimulationTransactionalReducerTests.NegativeReducerPreservesDistinctTriggerFactMultiplicity` — directly contradicts one accepted fact per entity/type/loop.
- Any duplicate test that only asserts internal registry ordering, slab row placement, route-cache contents, dense row indexes, candidate byte counts, or hash integer values in the public suite.

Do not preserve these under a compatibility category. If a historical behavior is useful for migration documentation, record it as a rejected old contract and add the new test instead.

### 11.4 Verification gates

- **P0:** public single-fact, entity-ceiling, ownership, and incremental compatibility tests pass; internal storage tests may still target the old backend.
- **P1:** the thin vertical slice passes both public lifecycle/atomicity tests and the internal backend/reservation/borrow tests before broad migration.
- **P2:** all public behavior tests pass with one runtime source of truth; old sparse/slab tests are either deleted or rewritten against the selected backend adapter's internal contract.
- **P3:** routing, topology, opaque boundaries, late input, closure, and continuation tests pass; public tests do not inspect scheduler internals.
- **P4:** compile-only public API verification, Unity import, both test suites, Rider/MSBuild, IL2CPP/player smoke, and the memory/allocation evidence gates pass. A skipped internal test is not evidence of parity.

## 12. Reference material

- [Bevy ECS storage](https://docs.rs/bevy_ecs/latest/bevy_ecs/storage/index.html): distinguishes table storage from sparse sets. Use as a mechanics reference, not evidence of C# allocation behavior.
- [Bevy schedule graph](https://docs.rs/bevy_ecs/latest/bevy_ecs/schedule/struct.ScheduleGraph.html): dependency topology and conflicting access are explicit schedule concepts. This does not establish confluence of user reducers.
- [Bevy schedule module](https://docs.rs/bevy_ecs/latest/bevy_ecs/schedule/): topologically ordered execution metadata; inspiration for compiling and inspecting the plan.
- [Entitas component semantics](https://github.com/sschmid/Entitas/wiki/Components): one component per entity/type with add/replace/remove APIs. Cascade deliberately disallows replacing an accepted fact during an open loop.
- [Current package contract](../Assets/CascadeEngine/Readme.md). The tests under `Assets/Tests` define executable compatibility and ownership checks.

External documentation is architectural reference material. Backend-specific compatibility, memory, stepping, and publication behavior must be verified locally before implementation is accepted.
