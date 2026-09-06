# Progress and decision history

This document records product and public-contract decisions, their status, and unresolved user-facing requirements. [package README](../Assets/CascadeEngine/Readme.md) documents current usage. Do not add internal restructuring notes, implementation changelogs, or benchmark/tests run histories here.

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

## Next Work

1. **Critical performance: replace transactional and batch global eligibility scans with fact-routed candidate scheduling.**
   - Preserve the public registration API and same-tick closure semantics.
   - Bind transactional and batch waiters to the accepted fact routes during feature registration, then evaluate only affected reducer/entity candidates.
   - Add internal eligibility-check diagnostics so scheduler work is measurable instead of hidden from `ProcessedWorkItems`.
   - Verify one thin vertical slice with 500+ entities and a large unrelated reducer registry: zero unrelated reducer invocations, bounded eligibility checks, correct incremental continuation, and 0 B steady-state allocation after warmup.

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
