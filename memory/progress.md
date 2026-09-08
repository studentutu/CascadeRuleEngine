# Progress and decision history

This document records product decisions, developer rules, implementation constraints, and unresolved requirements. The [package README](../Assets/CascadeEngine/Readme.md) documents current usage. Keep public READMEs thin: overview, goal, usage, observable behavior, policies, and problems solved. Preserve necessary internal design and verification rules here; avoid chronological implementation changelogs and raw benchmark/test run histories.

## Established decisions

### Product purpose and public API

- Cascade replaces hidden gameplay execution order with facts, reduction to closure, committed state, and typed mutation observation. Incremental execution and explicit budgets are core requirements.
- Keep a small, portable package with gameplay examples outside it. Hestia demonstrates the complete input-to-output flow and remains the reference integration example.
- Preserve public signatures and adapters during refactoring. Explicitly approved behavior changes must be documented separately from source compatibility.
- All facts belong to an entity. Global facts are not part of the public model.
- Facts last for one full reduction loop. A continuing external condition must be emitted again for the next loop or projected into durable state.

### Reduction, state, and commit behavior

- Reducers read accumulated facts and the last committed state, including other entities' state. They emit facts and may request entity creation/destruction; they do not directly mutate durable state.
- Every committer reads the same previous committed state. Successful closure publishes the next state together; a failure before publication leaves the previous state unchanged.
- Accepted facts are immutable during an open loop. Cancellation/removal requests are additive facts; physical retraction is unsupported because derived consequences would remain.
- Output priority is declared per affected fact type and applies only to commit selection. Priority does not assign gameplay reducer execution order. Ambiguous winning inputs fail rather than silently choose by arrival order.
- Required-fact transactions run once per eligible entity per loop. Batch callbacks receive eligible entities. State-triggered reducers use committed membership; newly committed state becomes eligible on the next loop.
- Absence rules evaluate after positive closure. Host input is sealed when terminal negative evaluation starts. Output absence conditions use the final closed fact set.
- Mutation observation is replayable and non-consumptive. The previous journal clears when the next loop begins; incomplete execution exposes no partial new journal.
- Deterministic, side-effect-free committers and consistent domain rules remain caller responsibilities. Replaying observations is not exactly-once external delivery, and repeating input in another loop is not automatically idempotent.

### Entity lifecycle and ownership

- Entity handles include a generation. A numeric entity ID identifies a recyclable runtime slot, not a permanent domain identity.
- Entities created during reduction can receive facts in that loop. Destruction is requested through `DeadFact`; the entity remains visible through reduction and disappears at successful closure.
- Failed loops roll back requested destruction and invalidate handles created by the failed loop.
- The simulation owns a fact only after acceptance, through every incremental pause until closure, failure cleanup, or terminal disposal. Rejected and deduplicated payloads do not become additional simulation-owned resources.
- Each accepted fact receives one disposal attempt. A disposal failure does not prevent attempts to clean up other accepted resources. Cleanup failures after publication preserve the committed result and must not trigger blind input replay.
- `FactSimulation.Dispose()` is terminal and idempotent. Feature composition transfers ownership to the parent; an attached sub-feature is not an independent simulation root.
- Disposable facts are borrowed by readers. Their resources must not escape into durable output snapshots without a separate ownership arrangement.

### Budget and capacity contract

- `CascadeSettings` captures construction-time capacity and reduction policy. Parameterless tick calls reuse that policy; per-call options do not relax configured hard limits.
- `MaxWorkItems` counts reducer invocations, separately from fact counts. `ProcessedWorkItems` reports cumulative work for the open loop.
- Incremental calls resume the same loop. A pause does not publish incomplete state or release accepted facts. `RunTick` retains its full-closure-or-budget-failure contract.
- Elapsed-time limits are cooperative between callback units. Synchronous callbacks, publication, and cleanup can exceed a requested time slice; an absolute frame-time ceiling is not an established guarantee.

## Dated decisions

### 2026-09-06 — Disposal compatibility retained

The user chose to retain inherited no-op `IFact.Dispose()` for public compatibility. Fact types requiring allocation-free cleanup on Unity Mono must implement `Dispose()` explicitly, including an empty method when they own no resources. The inherited default remains supported with its documented boxing cost. This decision is reflected in the current API documentation and examples.

### 2026-09-07 — Fact multiplicity decision replaced

The earlier contract intentionally preserved distinct same-type facts on one entity while deduplicating identical payloads. The user replaced that direction with **one accepted fact per entity/type per full loop**, including all incremental calls:

- Identical repeat: no-op.
- Different payload: throw before acceptance; never overwrite the accepted value.

Status: approved for implementation, not yet the current runtime behavior. Preserve `Emit`, `All<TFact>()`, and existing caller signatures; after migration, `All<TFact>()` returns zero or one value. Applications needing multiple events must represent or aggregate them explicitly.

### 2026-09-07 — Entity ceiling and incremental execution prioritized

The user prioritized exposing an entity-pool ceiling where `0` means no configured limit and a positive integer sets the limit. The user also required incremental execution as the mandatory simulation model, pausing after a completed system/callback when the frame budget is reached.

Status: approved requirements tracked in the active plan; do not present the new zero-limit semantics as already implemented. Existing incremental behavior and public entry points remain the compatibility baseline. An unlimited logical ceiling does not mean unlimited physical memory or unlimited work per loop.

## Open public-contract boundaries

- Direct public writes to durable state would bypass the established commit authority. Any added write API needs an explicit authority, publication, and ownership contract.
- Replaced, deleted, or abandoned output snapshots are not automatically disposed. Terminal teardown visits currently held disposable output states; shared resource lifetime remains a domain responsibility.
- `void Emit` does not report acceptance versus deduplication or retired-entity rejection. An acceptance-result API would be a separate additive decision.
- Stable domain-key lookup and `AnyOf` query matching remain requested ECS-parity gaps. Runtime slot lookup does not satisfy a unique domain index. The retained domain example is cross-entity damage routing: resolve a victim by stable ID, read the source's committed damage state, and emit a fact on the victim.
- General multi-stratum absence rules, exactly-once external delivery, and full Entitas feature/throughput parity are not established guarantees.
- A standalone Unity package and concise lifecycle, cross-entity, and incremental examples remain product delivery goals. Their scheduling belongs in the active plan, not a competing historical work list.

## Developer rules retained

Preserve developer goals,rules and constraints and implementation context, not new feature commitments.

### Package boundary and registration

- Keep `FactSimulation` as the concrete runtime entry point and lifecycle owner. Do not add a second public facade without a demonstrated host-facing capability to hide. `IFactSimulation` serves adapters needing lifecycle, emission, ticking, and mutation routing; concrete owners dispose the simulation directly.
- `Public` contains consumer-facing types; `Internal` contains implementation, core interfaces, and utilities. Gameplay examples must not depend on implementation details. Keep examples outside the portable package.
- Fact/output IDs derive from CLR type names during feature registration. Do not add static IDs to value types. The feature registry owns the name-to-ID catalog; duplicate names across the full feature tree and integer-ID collisions fail registration. Routing uses `CascadeTypeId`; there is no separate ID-to-type diagnostics map.
- No Runtime reflection!. Cold-path `typeof` registration metadata is permitted. Missing required services or output registrations are setup errors.
- Self-typed `IFact<TFact>` and `IOutputState<TState>` provide allocation-free typed equality. Payload comparison does not require `Equals(object)` or `GetHashCode()`. Retain non-generic compatibility and the explicitly approved inherited no-op disposal; Unity Mono requires an explicit fact `Dispose()` implementation to avoid boxing, including an empty method for resource-free facts.

### Fact lifetime, state triggers, and absence

- Strive for full EntitasECS feature parity: findEntityById, create/destroy entity(via buffer), write/read directly to fact/component, AnyOf/AllOf, full-sleep policy(entitas group created once, when group has no entity -> system early returns), performance of cached groups in non-reactive systems (full-sleep policy), initialization/refresh/teardown, but also use a proper sparse-set (bevy-ecs inspired) with a proper small archetypes.
- Facts describe inputs or consequences for one reduction loop. Do not extend their lifetime with cleanup markers or reducer-owned persistence conventions; durable values belong in **output** state. Continuing external conditions must be emitted for each loop while true.
- Accepted facts cannot be physically removed or overwritten during reduction. Cancellation/removal uses additive facts; durable removal uses `CommitDecision<TState>.Delete()`. Retracting a fact would leave derived consequences behind and restore arrival-order dependence.
- `ReduceState<TState, TReducer>()` uses `ITransactionalReducer` and runs once per live entity containing the trigger in the committed snapshot. State created in tick N first triggers in N+1; same-tick activation consequences must be facts. Register the trigger as an output in the full feature tree, use the narrowest trigger, and query other states inside the reducer. Avoid combinatorial registration overloads.
- Immediate `Reduce<TFact>()` dispatches once per accepted distinct trigger fact under the current multiplicity contract. `Reduce<TFact>().Without<TForbidden>()` retains that fact-level contract but defers execution until positive immediate, transactional, batch, and state-driven work reaches closure. Every chained forbidden fact must be absent.
- Support one terminal negative stratum. Seal its trigger and forbidden fact types when it starts; emitting those condition types afterward fails the tick. Contradictory trigger/forbidden declarations fail feature construction. General dependency sorting would require an explicit emitted-fact/rule-head contract; do not infer one.
- `Emit`, `CreateEntity`, and `DestroyEntity` calls are sealed once incremental execution reaches negative evaluation. Resume to closure before submitting new input. Do not expose the fact-level `Without` contract on `ReduceWhen`, `ReduceBatchWhen`, or `ReduceState`, which have different invocation semantics.
- Output `Without<TFact>()` is an additional reconciliation trigger for previous output members when none of its declared facts is present in the closed fact set. It is not a filter on `AffectedBy`; either route may select the output. Plan each entity/output pair once, and allow the committer to return `Set`, `Delete`, or `Unchanged`.

### Commit policy, preparation, and continuation

- Current behavior retains **distinct same-type facts** and **deduplicates identical payloads**; `All<TFact>()` exposes retained values. The **approved one-fact-per-entity/type** migration above supersedes this direction but is not yet implemented. Never substitute last-write-wins acceptance.
- Declare production conflict policies explicitly. `FoldAll` is the compatibility default and requires a commutative domain fold. `CollapseToSingleMarker` folds presence into a marker. `PriorityWinnerOrThrowOnTie` selects the highest-priority affected fact type, exposes only that type through commit-context facts, and rejects multiple distinct winning inputs before durable writes. Priority belongs to each output/fact registration; zero is valid when no distinction is needed. It never controls reducer scheduling or fact admission.
- Every committer's `previous`, `GetState`, `TryGetState`, and `HasState` reads the same **previous committed snapshot**, including cross-entity reads. Output registration and touched-entity order must not change those reads. Prev-commited state lasts up until full new commit is done.
- Use **buffering of commands**. Prepare all changed entity/output decisions, previous-member absence, and pending-destruction deletions before application. Complete equality, conflict checks, target-generation validation, permitted legacy capacity growth, and action/journal reservation before any durable write.
- Zero memory allocaiton in hot-path. Initialization/warm-up exists for this reason and allow to create buffers. Apply validated typed mutations into reserved array slots without callbacks, equality, or resizing. Finalize state, journals, and entity release together. Failure during preparation publishes neither partial state nor partial mutations.
- Incremental simulation is key. Nothing is visible to public users, internally uses a full ecs (check after each system execution and look up if we are over frame budget).
- While host input remains open, accepted facts and entity creation/destruction invalidate unpublished planning and return execution to reduction. Deduplicated input does not invalidate it; already fired transactional reducers remain fired. Terminal negative sealing still applies.
- Committers must be deterministic and side-effect-free because abandoned planning may repeat. Clear borrowed plan references without disposing copied output snapshots. Mutation journals clear at `BeginTick`, stay empty during suspension, and publish only the completed transaction. Observation is replayable, not consumptive.

- Elapsed time includes routing, planning, and validation. Callbacks, atomic publication, and cleanup are not preemptible. `RunTick` remains full-closure-or-budget-failure; incremental execution resumes the same open tick.

### Capacity, storage, and queries

- The current settings constructor requires positive `MaxEntities`, `MaxFactsPerEntity`, and `MaxFactsPerTypePerEntity`; do not present the approved zero-entity-limit semantics as delivered. Entity capacity counts concurrent live or pending entities, not lifetime creations.
- Settings-backed construction validates, reserves, and freezes runtime capacity.
- Preserve the older `Warmup(WarmupCapacityHints)` and explicit-options workflows for adapters and exploratory growth. Production code should own one settings configuration and construct the simulation once. Warmup only reserves capacity: it creates no entities, facts, reducer effects, committed state, or mutations.
- Size queue capacity for both input and derived facts at peak workload, not entity count alone. Predeclare all fact types through reducer triggers, transaction requirements, batch requirements, or affected-output declarations; undeclared reducer-only emissions cannot be warmed.
- Each fact type uses a flat typed payload slab, sparse entity-slot mapping, compact owner array, and count array. Touched entities receive contiguous fixed-width slices; untouched entities need no per-entity list. `All<TFact>()` borrows a zero-copy `ReadOnlySpan<TFact>` over the active slice.
- Settings reserve `MaxEntities * MaxFactsPerTypePerEntity` payload capacity for every registered fact type. This worst-case reservation supports every entity emitting every type without hot-path growth. `FactListCapacityMode.Fixed` throws before exceeding a slice. `GrowOnDemand` may allocate and repack; retain the legacy enum name for compatibility, not as a description of current storage.
- Output state uses sparse-set membership and compact values. Single-state queries iterate that state; two-state queries iterate the smaller set and test the other. Warm both expected entity IDs and state membership through `OutputStateCapacityPerOutput`.
- `EntityQueryResult` exposes `Count`, indexing, and `AsSpan()` over reusable engine-owned storage. Consume a result before another query reuses the buffer. For additional state requirements, query a narrow supported shape and test remaining membership; add richer matchers only from a measured use case.

### Entity lifecycle and resource teardown

- `EntityRef` identity includes both slot and generation. Recycle slots only after committed destruction or failed-tick rollback cleanup. Generation exhaustion retires the slot rather than wrapping. Runtime slot lookup is not a stable domain index.
- Reducer-created entities are immediately usable in the open tick. `DestroyEntity` and directly emitted `DeadFact` request the same transactional destruction. Pending-dead entities stay visible to queued facts, later facts, reducers, and queries through closure.
- `IsDestroyed` reports pending death immediately and `TryGetEntity` rejects pending-dead slots, while committed state remains readable. Domain reducers must explicitly handle `DeadFact` when they should stop work; cleanup reducers may trigger on it directly.
- Skip normal output projection for pending-dead entities; closure publishes one final deletion per existing output. Failure rolls back destruction and invalidates newly created handles. Stale-handle emissions after closure are rejected. Keep `TryGetEntity` on the concrete simulation to preserve existing adapter implementations.
- `SetStateSilently` is bootstrap/load authority only and must throw during an open tick. Host-requested destruction also needs tick completion to publish deletions.
- The fact store owns only accepted facts, through all incremental pauses, negative evaluation, and commit planning. Queue entries, arguments, views, and spans borrow payloads. Committers copy durable values out; they must not retain a disposable fact's resource in output state.
- For a pooled-buffer fact, equality should identify the buffer lease (for example, `ReferenceEquals(Buffer, other.Buffer)`), and disposal returns it to its pool. Distinct leases must not deduplicate by contents. The producer stops using an accepted lease and must not submit it as independently owned facts on multiple entities. Submit to a known live target: `void Emit` reports neither deduplication nor retired-entity rejection. Failed admission remains caller-owned; re-emitting an accepted lease creates no additional owner.
- Simulation-owned mutation records borrow their `Previous` and `Next` snapshots. Clearing records must not dispose those copies. Terminal teardown disposes currently stored disposable output values; replacement, deletion, and abandoned decisions do not. Keep shared resource ownership in a domain owner until an explicit output-resource contract exists.
- `SubFeature` transfers registration ownership to its parent. Simulation disposal visits the root, attached features, registrations, reducers, and committers that implement `IDisposable`, then clears registry maps. Attached features cannot be roots; disposed features cannot construct another simulation.
- Clear stored payload references before disposal callbacks and attempt each owner exactly once even if others throw. Rethrow a single failure with its stack; aggregate multiple failures without losing the original reduction/commit error. Disposers remain responsible for finishing their own cleanup before throwing.
- A post-publication cleanup failure preserves state, mutations, and completed `LastResult`; do not blindly replay input. Terminal disposal remains terminal/idempotent even on errors. Release static routes and all owned scratch storage after callback failures or prior feature disposal. Every future runtime pool/buffer must be included in disposal. Public simulation calls after disposal throw `ObjectDisposedException`.
- Tick callbacks cannot recursively tick, dispose the simulation, or warm storage. Fact disposers cannot submit input.

### Migration scope and verification

- Preserve the migration boundary: existing ECS/input/event -> entity fact -> reduction closure -> committed output -> typed mutation consumed by ECS/world/UI. React-style reconciliation is a design analogy, not a second state owner or public scheduling API.
- Supported outcomes include generational creation, transactional destruction, current-slot lookup, committed reads, `Set`/`Delete`, durable markers, tick-local event facts, one/two-state queries, typed mutation observation, and feature composition. Durable component changes pass through commit authority; ordered mutable systems and physical fact removal are intentionally replaced/excluded.
- Full Entitas parity is not established. Missing facilities include stable/unique and one-to-many domain indexes, `AnyOf` and richer matchers, persistent live groups with membership callbacks, enforced unique components, generated component/listener/index APIs, multi-context aggregation/transactions, and arbitrary boxed runtime enumeration/editor inspectors. Reducer `Without` is not a general query exclusion matcher.
- Host bootstrap, automatic fact clearing, and terminal disposal replace initialize/cleanup/teardown systems. Host services or explicit entities represent singletons; separate simulations require host coordination. No generated added/removed collector matrix or exactly-once external delivery is promised.
- Add parity primitives only after a thin production slice, measured cost, and public-contract review. Do not treat every missing Entitas API as promised future work. The comparison originally used Entitas's [Components](https://github.com/sschmid/Entitas/wiki/Components) and [Systems](https://github.com/sschmid/Entitas/wiki/Systems) concepts as its reference.
- Keep Hestia as the thin vertical slice: ammo request -> acceptance -> durable ammo and optional dry-fire cue; movement request -> resolution -> position, with ambiguous winning movement rejected before publication. `OutputStateRouteTests` covers cross-type priority and order-independent winner selection. `Assets/Tests` supplies executable contract examples.
- Verify incremental execution, atomic publication, lifecycle, and ownership. Allocation verification uses Unity's synchronous `GC.Alloc` recorder with a positive allocation control; the tested Mono per-thread byte counter is unreliable. Editor allocation results do not certify IL2CPP throughput or full Entitas parity. Preserve allocation-free hot paths and measure workloads of 500+ entities.

## Next Work

1. **Critical performance: replace transactional and batch global eligibility scans with fact-routed candidate scheduling.**
   - Preserve the public registration API and same-tick closure semantics.
   - Bind transactional and batch waiters to the accepted fact routes during feature registration, then evaluate only affected reducer/entity candidates.
   - Add internal eligibility-check diagnostics so scheduler work is measurable instead of hidden from `ProcessedWorkItems`.
   - Verify one thin vertical slice with 500+ entities and a large unrelated reducer registry: zero unrelated reducer invocations, bounded eligibility checks, correct incremental continuation, and 0 B steady-state allocation after warmup.

2. Remove:

- `MaxFacts` counts dequeued facts per incremental call; `MaxWorkItems` counts reducer callbacks, including one atomic batch invocation. `ProcessedWorkItems` is cumulative for the open loop. Budget suspension does not consume `MaxPasses`; completed logical closure passes do.

3. Budgeting. Profile the state-presence relevance slice before adding priority primitives. Only add Reducer-Loop Priority-per-Entity mode if measured workloads require it:
   - use presence of domain-owned `ActiveState`/equivalent as the first relevance filter.
   - measure starvation and frame-slice latency before designing scheduling metadata.
   - do not add `SimulationMode`, priority flags, or dormant scheduler state speculatively.

4. Prepare production package:
   - minimal examples
   - add example of incremental loop where we can specify the hard TimeSpan beyond which we stop the reduction loop and away next frame.
   - move from asset folder to proper unity package (similar to https://github.com/studentutu/FluentPlayableApi)
   - Keep Hestia as the minimal vertical slice.
   - Add one small example showing cross-entity query from a reducer.
   - Add one example showing entity creation/deletion during reduction.
   - Review package readme and add section if limitation, examples are missing.
