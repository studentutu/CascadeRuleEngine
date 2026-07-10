# Progress

## Current State

- CascadeEngine package with full vertical-slice: fact -> reducer -> committer -> typed mutation pipeline.
- HestiaGame reorganized into Core, Facts, Reducers, Output, and Utils.
- Hestia facts and output states use the package self-typed equality contracts without object equality or hash boilerplate.
- `System.Math` usage under `Assets` was replaced with `Mathf`.
- Per-tick fact storage was refactored away from dictionary-heavy entity buckets into internal dense storage utilities:
  - `DenseEntitySet`
  - `DenseEntityCounter`
  - `DenseEntityObjectStore<T>`
  - `EntityRefBuffer`
- Raw dense storage mechanics are now isolated behind tested utilities instead of being embedded directly in `FactStore`, `FactBucket`, or `FactSimulation`.
- `IFact` now inherits `IDisposable`; accepted stored facts are disposed when tick-local fact storage clears.
- `EntityStore` now tracks destroyed entity ids.
- `FactSimulation.Warmup(WarmupCapacityHints)` now pre-sizes dense fact stores, query/transaction/batch buffers, commit and mutation buffers, the fact queue, and registered fact buckets for expected gameplay load.
- Fact emit routing now uses per-fact typed routes containing the fact id, so emit avoids type-catalog fact id lookup.
- Output state routing now binds simulation-owned typed state buckets, so `GetStateBucket<TState>()` and query/state access avoid type-catalog output id lookup.
- Output registrations now keep their simulation-owned typed state bucket after binding, so commit queuing does not re-fetch the bucket through `OutputStateRouteCache<TState>`.
- Commit routing now records accepted fact routes per touched entity and reads affected outputs directly from those routes, so commit reconciliation no longer scans fact buckets or looks up affected outputs by fact id.
- Reducer routing now binds reducer invokers into per-fact typed routes, so the reduction loop no longer performs a reducer dictionary lookup by fact id for each queued fact.
- No Global facts should exists in package API and storage model. All emitted facts must belong to concrete entities.
- Commit actions are buffered per output as reusable value-type lists, preserving delayed reconciliation without per-mutation action object allocation.
- A 512-entity warmup allocation test now measures first-use and steady-state emit/tick execution; result should be (any or zero) bytes first-use and 0 bytes steady-state in edit-mode test output.
- `FactSimulation.Dispose()` is now the only terminal simulation lifecycle API. It releases simulation-owned tick facts, output state buckets, mutation buffers, entity lifecycle data, commit buffers, fired reducer caches, and the bound feature registry tree exactly once.
- `SubFeature` transfers registration ownership into the parent feature and drains the child registry. Attached sub-features are not valid simulation roots.
- `FactFeature.Dispose()` clears its registry and attached sub-features. Reducer and committer instances that implement `IDisposable` are disposed before registration maps are cleared.
- Transactional reducer semantics are covered by focused edit-mode tests:
  - two-fact entity-scoped reducer fires once when both required facts exist.
  - batch transactional reducer receives only eligible entities.
  - batch transactional reducer fires exactly once per entity when entities become eligible across different passes, while incomplete entities stay excluded.
  - generic `ReduceWhen` and `ReduceBatchWhen` registrations support two through four facts from one dedicated `FactFeature.TransactionalRegistration.cs` surface.
  - arities above four chain package-provided `.And<TFact>()` builder extensions without feature inheritance or package changes.
- Same entity/fact-type multiplicity is intentional: identical fact payloads dedupe, distinct payloads are preserved and exposed through `IEntityFactView.All<TFact>()`.
- Commit priority conflict handling is declarative and commit-only:
  - `AffectedBy<TFact>(int priority)` assigns output-scoped priority without changing reducer scheduling.
  - `PriorityWinnerOrThrowOnTie` exposes only the winning fact type to the committer and rejects multiple distinct winning facts before durable writes.
  - `IPrioritizedFact`, `IFactConflictComparer<TFact>`, and `FactConflictResolution` were removed from the public API.
- Committed output state can now drive per-tick work through the minimal `ReduceState<TState, TReducer>()` registration. It reuses `ITransactionalReducer`; no state-specific reducer interface or builder was added.
- State-trigger membership is a committed tick snapshot. Newly committed state becomes eligible next tick, state reducers run once per eligible live entity, and incremental continuation preserves the registration/entity cursor.
- `ReduceOptions.MaxWorkItems` is a per-call reducer-invocation budget independent of `MaxFacts`; `SimulationResult.ProcessedWorkItems` reports cumulative tick work.
- Incremental dispatch now preserves a popped fact and its next reducer index when a budget stops between multiple reducers for the same fact.
- Reducer-side entity creation/destruction is transactional:
  - new entities can receive facts in the same open tick.
  - `DestroyEntity` and direct `DeadFact` emission stage the same additive lifecycle fact.
  - pending-dead entities continue immediate, transactional, batch, and state reduction through closure; reducers explicitly skip work by querying `DeadFact`.
  - normal output projection is skipped for pending-dead entities and durable output deletion waits for closure.
  - failed ticks roll back destruction and permanently tombstone ids created by the failed tick.
- `FactSimulation.TryGetEntity(int, out EntityRef)` validates host/persistence ids without expanding `IFactSimulation`.
- Output state storage now uses compact sparse membership/value arrays instead of per-output dictionaries. One-state queries iterate that state only; two-state queries iterate the smaller state set.
- Entity lifecycle storage is warmed array state instead of an allocating destroyed-id `HashSet`.
- The state-trigger vertical slice covers activation/deactivation, dormant filtering, cross-entity state query, incremental work suspension, reducer-side create/destroy, failure rollback, and same-fact multi-reducer continuation.
- The 512-entity state-trigger allocation test reports 0 bytes for first-use and steady-state measured ticks after warmup.
- `IFact<TFact>` and `IOutputState<TState>` now reduce normal fact/state equality boilerplate to one typed `Equals` method. `IFact` supplies default no-op disposal; resource-owning facts can still override it.

## Known Gaps

- Warmup is only as accurate as the host-provided hints.
  - Underestimated entity count, queue size, output state capacity, or mutation capacity can still grow during gameplay.
  - In fixed fact-list mode, underestimated per-entity fact count now throws instead of allocating.
- Fact warmup still pre-creates per-entity fact-list objects for every known fact bucket. Replace that Cartesian entity/fact-type shape with sparse typed slabs without weakening `ReadOnlySpan<TFact>` access or fixed-capacity failure behavior.

## Next Work

1. Harden core policy:
   - Add tests proving committers read previous committed state, not partially committed output from another committer.

2. Budgeting. Profile the state-presence relevance slice before adding priority primitives. Only add Reducer-Loop Priority-per-Entity mode if measured workloads require it:
   - use presence of domain-owned `ActiveState`/equivalent as the first relevance filter.
   - measure starvation and frame-slice latency before designing scheduling metadata.
   - do not add `SimulationMode`, priority flags, or dormant scheduler state speculatively.

3. Prepare production package:
   - minimal examples
   - add example of incremental loop where we can specify the hard TimeSpan beyond which we stop the reduction loop and away next frame.
   - move from asset folder to proper unity package (similar to https://github.com/studentutu/FluentPlayableApi)
   - Keep Hestia as the minimal vertical slice.
   - Add one small example showing cross-entity query from a reducer.
   - Add one example showing entity creation/deletion during reduction.
   - Review package readme and add section if limitation, examples are missing.
