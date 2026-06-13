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

## Known Gaps

- There is no proper warmup phase yet.
  - Dense stores and reusable buffers grow on demand or during entity creation, but there is no explicit package-level warmup API for expected entity counts, fact type capacities, per-entity fact list capacities, queue capacity, or reducer/batch buffers.
  - This can still produce first-use allocation spikes in real gameplay loads.
- There is no full and proper teardown phase yet.
  - Tick-local facts are disposed when storage clears, but the runtime does not expose an explicit teardown/dispose path for all long-lived internal stores, output buckets, mutation buffers, reducer registrations, and future pooled resources.
  - This matters for scene unload/reload, domain-level simulation replacement, and long-running editor sessions.
- `IPrioritizedFact` priority extraction can still box prioritized structs because it is a non-generic interface check. Fixing that cleanly needs an additive priority API or a generated/registered typed priority resolver.
- Fact/type routing still uses `System.Type` references in several internal maps and descriptors. This is heavier than needed for hot-path fact identity and routing.
- `EntityFactList<TFact>` remains array-backed to provide contiguous `ReadOnlySpan<TFact>` access. This is acceptable for now, but it needs explicit capacity policy or pooling before real load testing.

## Next Work

1. Add a proper warmup phase.
   - Pre-size dense entity stores, query buffers, transaction buffers, batch buffers, fact queue, and known fact buckets from host-provided capacity hints.
   - Add API examples showing warmup for 500+ entities.
   - Add tests proving warmup prevents capacity growth during a representative tick.

2. Add a full teardown phase.
   - Define ownership rules for tick-local facts, output state buckets, mutation buffers, reducer registrations, and future pooled resources.
   - Add a simulation teardown/dispose API and tests proving facts/resources are disposed exactly once.

3. Finish remaining hot-path allocation tightening.
   - Replace `IPrioritizedFact` interface priority lookup with a typed resolver path that does not box structs.
   - Add explicit capacity policy or pooling for `EntityFactList<TFact>`.
   - Measure first-use and steady-state allocations with a realistic 500+ entity scenario.

4. Replace `System.Type` fact/output identity with lightweight ids.
   - Introduce a lightweight GUID or stable type id for each fact/output type instead of storing and routing by `System.Type`.
   - Each fact type should expose or register one static unique id with startup validation for duplicates.
   - Don't Keep `System.Type`, full cut-out. For diagnostic purposes add a utility that will map guid to the actual type (do not use it in hotpath, only in exceptions!)
   - Update reducer maps, affected-output maps, queued facts, fact masks, and transaction required-fact checks to use the lightweight id.
   - Add tests proving duplicate ids fail during feature/build validation and valid ids route reducers/committers correctly.

5. Add focused diagnostics for reduction failures.
   - Include current fact type, entity, causal depth, reducer type, and budget reason.
   - Make cycle/budget failures actionable instead of generic exceptions.

6. Validate transactional reducer semantics with tests.
   - Add a two-fact entity-scoped reducer test.
   - Add a batch transactional reducer test with only eligible entities.
   - Verify transactional reducers do not fire twice for the same entity/fact set.

7. Harden commit conflict behavior.
   - Add tests for equal-priority conflicts across multiple facts.
   - Add tests proving committers read previous committed state, not partially committed output from another committer.

8. Improve package examples.
   - Keep Hestia as the minimal vertical slice.
   - Add one small example showing cross-entity query from a reducer.
   - Add one example showing entity creation during reduction.
