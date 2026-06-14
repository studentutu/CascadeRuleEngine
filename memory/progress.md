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
- `FactSimulation.Dispose()` is now the only terminal simulation lifecycle API. It releases simulation-owned tick facts, output state buckets, mutation buffers, entity lifecycle data, commit buffers, fired reducer caches, and the bound feature registry tree exactly once.
- `SubFeature` transfers registration ownership into the parent feature and drains the child registry. Attached sub-features are not valid simulation roots.
- `FactFeature.Dispose()` clears its registry and attached sub-features. Reducer and committer instances that implement `IDisposable` are disposed before registration maps are cleared.
- `Assets/CascadeEngine/Readme.md` now defines ownership rules for tick-local facts, output state buckets, mutation buffers, feature registries, and future simulation-owned pooled resources.

## Known Gaps

- Warmup is only as accurate as the host-provided hints.
  - Underestimated entity count, per-entity fact count, or queue size will still grow during gameplay.
  - `IPrioritizedFact` and transactional fired-key strings can still allocate independently of capacity warmup.
- `IPrioritizedFact` priority extraction can still box prioritized structs because it is a non-generic interface check. Fixing that cleanly needs an additive priority API or a generated/registered typed priority resolver.
- Fact/type routing still uses `System.Type` references in several internal maps and descriptors. This is heavier than needed for hot-path fact identity and routing.
- `EntityFactList<TFact>` remains array-backed to provide contiguous `ReadOnlySpan<TFact>` access. This is acceptable for now, but it needs explicit capacity policy or pooling before real load testing.
- review the concept behind many individual arrays that warm-up, come up with a better solution (switch to sparse-set). 

## Next Work

1. Finish remaining hot-path allocation tightening.
   - Replace `IPrioritizedFact` interface priority lookup with a typed resolver path that does not box structs.
   - Add explicit capacity policy or pooling for `EntityFactList<TFact>`.
   - Measure first-use and steady-state allocations with a realistic 500+ entity scenario.

2. Replace `System.Type` fact/output identity with lightweight ids.
   - Introduce a lightweight GUID or stable type id for each fact/output type instead of storing and routing by `System.Type`.
   - Each fact type should expose or register one static unique id with startup validation for duplicates.
   - Don't Keep `System.Type`, full cut-out. For diagnostic purposes add a utility that will map guid to the actual type (do not use it in hotpath, only in exceptions!)
   - Update reducer maps, affected-output maps, queued facts, fact masks, and transaction required-fact checks to use the lightweight id.
   - Add tests proving duplicate ids fail during feature/build validation and valid ids route reducers/committers correctly.

3. Add focused diagnostics for reduction failures.
   - Include current fact type, entity, causal depth, reducer type, and budget reason.
   - Make cycle/budget failures actionable instead of generic exceptions.

4. Validate transactional reducer semantics with tests.
   - Add a two-fact entity-scoped reducer test.
   - Add a batch transactional reducer test with only eligible entities.
   - Verify transactional reducers do not fire twice for the same entity/fact set.

5. Harden commit conflict behavior.
   - Add tests for equal-priority conflicts across multiple facts.
   - Add tests proving committers read previous committed state, not partially committed output from another committer.

6. Improve package examples.
   - Keep Hestia as the minimal vertical slice.
   - Add one small example showing cross-entity query from a reducer.
   - Add one example showing entity creation during reduction.
