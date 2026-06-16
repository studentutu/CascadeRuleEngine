# Progress

## Current State

- CascadeEngine package with full vertical-slice: fact -> reducer -> committer -> typed mutation pipeline.
- HestiaGame reorganized into Core, Facts, Reducers, Output, and Utils.
- Hestia hash-combine logic is centralized in `HestiaExtensions`.
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
- Fact emit routing now uses per-fact typed routes containing the fact id and optional explicit `IFactPriorityResolver<TFact>`, so emit avoids type-catalog fact id lookup and erased object resolver casts.
- Output state routing now binds simulation-owned typed state buckets, so `GetStateBucket<TState>()` and query/state access avoid type-catalog output id lookup.
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

## Known Gaps

- Warmup is only as accurate as the host-provided hints.
  - Underestimated entity count, queue size, output state capacity, or mutation capacity can still grow during gameplay.
  - In fixed fact-list mode, underestimated per-entity fact count now throws instead of allocating.
- review the concept behind many individual arrays that warm-up, come up with a better solution (switch to sparse-set or alternative methods, review the actual concept of usage behind it maybe a partial redesign will fix it).

## Next Work

1. Ergonomics tightening.
  1.1 Review ReduceWhen ergonomics: we need to need to support up to 4 facts as parameters for the ReduceWhen/BatchReduceWhen. This should also be trivial to expand if needed. Make it clean and separate in code so that it is clearly visible for the external developers who needs more.
  1.2 Review ergonomics of the Fact and his priority. Currently we need to so specify them separately, but Ideally fact would be bound to the policy. We need to be able to deduce the priority by fact in the registration once.
  1.3 Review if we handle removal/additional of entities while fact-reduction is not yet complete. Double check incremental use with the same task.
  1.4 improve Fact/Output ergonomics, currently always specified separate IEquatable/others methods, we need to reduce boilerplate code

2. Harden commit conflict behavior.
   - Add tests for equal-priority conflicts across multiple facts.
   - Add tests proving committers read previous committed state, not partially committed output from another committer.

3. Add BudgetMode:
   - Reducer loop is currently broad for every fact across all entities, we need to make a budget mode so that only-relevant entities (entity marker as relevant in a entity flags) are fully reduced.
   - Add test coverage to verify that.
   - should work both modes of simulation for both full and partial(incremental).

3. Improve package:
   - minimal examples
   - add example of incremental loop where we can specify the hard TimeSpan beyond which we stop the reduction loop and away next frame.
   - move from asset folder to proper unity package (similar to https://github.com/studentutu/FluentPlayableApi)
   - Keep Hestia as the minimal vertical slice.
   - Add one small example showing cross-entity query from a reducer.
   - Add one example showing entity creation/deletion during reduction.
   - Review package readme and add section if limitation, examples are missing.
