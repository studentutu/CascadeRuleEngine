# Progress

## Current State

- CascadeEngine MVP package with full vertical-slice: fact -> reducer -> committer -> typed mutation pipeline.
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
- `EntityStore` now tracks destroyed entity ids with `HashSet<int>` while preserving monotonic created entity ids through `Count`.
- `FactSimulation.Warmup(WarmupCapacityHints)` now pre-sizes dense fact stores, query/transaction/batch buffers, commit and mutation buffers, the fact queue, and registered fact buckets for expected gameplay load.
- Fact emit routing now uses per-fact typed routes containing the fact id and optional explicit `IFactPriorityResolver<TFact>`, so emit avoids type-catalog fact id lookup and erased object resolver casts.
- Output state routing now binds simulation-owned typed state buckets, so `GetStateBucket<TState>()` and query/state access avoid type-catalog output id lookup.
- `EntityFactList<TFact>` now has explicit grow/fixed capacity behavior. `FactListCapacityMode.Fixed` turns underestimated per-entity fact capacity into a setup error instead of hidden gameplay allocation.
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
- Edit-mode allocation coverage is not a substitute for Unity player and IL2CPP profiling with real gameplay facts/reducers.
- review the concept behind many individual arrays that warm-up, come up with a better solution (switch to sparse-set or alternative methods, review the actual concept of usage behind it maybe a partial redesign will fix it).

## Next Work

1. Hot-path tightening.
  1.1 Fact/type routing still uses `System.Type` references in several internal maps and descriptors. This is heavier than needed for hot-path fact identity and routing.
  1.2 Make cycle/budget failures actionable instead of generic exceptions (we need to add incremental Loop, meaning  RunTick makes full loop, while RunTickIncremental makes a single loop and returns whether it is done or not). This is needed for projects with broad and complex reducers and single full loop may be too heavy. Incremental loop allows to have a strict budget per single reducer loop iteration and act on it, allowing main-thread to go do other job and return to the loop in the next frame.
  1.3 Diagnostics: Include current fact, entity, causal depth, reducer type, and budget reason.

2. Harden commit conflict behavior.
   - Add tests for equal-priority conflicts across multiple facts.
   - Add tests proving committers read previous committed state, not partially committed output from another committer.

3. Improve package:
   - minimal examples
   - improve Fact/Output ergonomics, currently always specified separate IEquatable/others methods, we need to reduce boilerplate code
   - move from asset folder to proper unity package (similar to https://github.com/studentutu/FluentPlayableApi)
   - Keep Hestia as the minimal vertical slice.
   - Add one small example showing cross-entity query from a reducer.
   - Add one example showing entity creation/deletion during reduction.
   - Review package readme and add section if limitation, examples are missing.
