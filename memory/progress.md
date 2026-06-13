# Progress

## Current State

- CascadeEngine MVP package with full vertical-slice: fact -> reducer -> committer -> typed mutation pipeline.
- HestiaGame reorganized into Core, Facts, Reducers, Output, and Utils.
- Hestia hash-combine logic is centralized in `HestiaExtensions`.
- `System.Math` usage under `Assets` was replaced with `Mathf`.
- Unity compile and edit-mode tests passed after the latest code changes.

## Next Work

1. Review the CascadeEngine core API names and folder layout and abstractions for package drop-in clarity.
   - Check whether `FactSimulation` should stay as the concrete runtime name or whether a thinner public facade is needed.
   - Check what should move into `Internal` folders/namespaces.
   - minimize abstractions if they are not truly needed (no useless abstraction)

2. Tighten hot-path allocation behavior.
   - Replace boxed fact payloads in `FactStore` / `QueuedFact`.
   - Replace dictionary-heavy per-tick storage with typed dense buckets or pooled stores.
   - Keep the current public API stable while changing internals.

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

7. Re-check Unity/editor integration boundaries.
   - Confirm asmdef references are minimal.
   - Confirm folder `.meta` files are committed.
   - Confirm no runtime package code depends on Hestia domain code.

