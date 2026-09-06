# Internal overhaul results — 2026-09-06

Implemented [improvements.md](improvements.md) with the existing public entry points and ownership model intact. The compile-only adapter/consumer fixture and existing behavioral tests remain part of the verification suite. No framework, package, runtime reflection, unsafe storage, or public storage/scheduler facade was introduced.

## Final design

1. **Prepare, validate, apply.** Output decisions, equality, capacity reservation, absence reconciliation, and lifecycle deletions are prepared against the previous snapshot. Typed actions validate target generations before application. Apply writes reserved arrays/indexes, publishes final mutations, and releases destroyed slots as one uninterrupted transaction. No user equality, committer, resize, or allocation belongs to apply.
2. **Routed candidates.** Composed feature registration binds transactional/batch waiters to typed fact routes. Accepted queue entries feed packed pending pairs. Required-fact checks occur only for routed candidates; fired markers preserve once-per-entity invocation. Touched-entity order, registration frontiers, and pending batch collection survive suspension. Late required facts requeue incomplete candidates without refiring completed ones.
3. **Resumable reconciliation.** Explicit affected/absence/lifecycle/validation cursors retain an unpublished plan. Accepted input revisions invalidate that plan only while host input is open. Negative sealing remains terminal. Reducer-work accounting is unchanged; internal collection and planning use time checks instead.
4. **Bounded ownership.** Settings reserve and freeze runtime storage before execution. Fact/entity admission completes permitted legacy growth before logical membership or payload ownership changes. Prepared action and mutation arrays have explicit counts and capacity checks. Rejected capacity requests preserve the previous logical state.
5. **Shared sparse mechanics.** `EntitySparseSet<TValue>` now owns generation checks, compact values, swap-back repair, dense clearing, and capacity preparation for both state buckets and fact-slab row counts. Fact slabs retain contiguous typed payloads and never remove/reorder rows during an open tick. Disposal remains with the payload owner.

`DenseEntityCounter`, the slot-owned route-object store, and `EntityRefBuffer` remain because they retain distinct, small responsibilities. Membership-only scratch uses generation-checked indexes without an unused values array. Superseded state/fact sparse implementations and the old output mark array/direct commit path were removed.

## Public compatibility decision

The user approved preserving inherited no-op `IFact.Dispose()` while requiring an explicit implementation on allocation-free fact types:

```csharp
public readonly struct RequestFact : IFact<RequestFact>
{
    public bool Equals(RequestFact other) => true;
    public void Dispose() { }
}
```

On the tested Unity Mono runtime, 100 constrained calls to inherited default disposal produced **100 allocation events**; 100 calls to an explicit empty struct implementation produced **0**. Resource-owning implementations still receive exactly one cleanup attempt per accepted fact. `DeadFact`, Hestia facts, and the allocation fixtures use explicit disposal. Existing types inheriting the default still compile and behave as before, with the documented allocation cost.

Two cold generic value-construction allocations were isolated to `StateMutation<TState>` and `CommitAction<TState>`. Their value constructors are now initialized during state-bucket registration without calling user code, creating entities, accepting facts, or writing durable state. The cold typed-storage regression records zero events for decision/mutation/action construction, preparation, equality, apply, and journal clearing after registration.

## Verification

- Unity **6000.3.15f1** authoritative import/project generation: passed; fresh import snapshot and success marker.
- EditMode tests: **111 passed, 0 failed, 0 inconclusive, 0 skipped**.
- Rider **2025.1.4** MSBuild: process/capture exit 0 and `SIMPLE_UNITY_MCP_CI:MSBUILD_PASSED`.
- `git diff --check`: passed.
- Generated editor projects report MSB3277 conflicts involving `System.Reflection.DispatchProxy` and `System.Private.ServiceModel`. The build succeeds; generated project/dependency configuration was not changed to suppress these warnings.

The regression suite covers equality failure across output/entity order, previous-snapshot reads, capacity failure before apply, stale prepared actions, lifecycle rollback, rejected resource ownership, negative sealing, repeated plan invalidation, journal clearing/replay, exact incremental continuation, late required facts, legacy growth with pending batches, and terminal cleanup.

The Hestia-composed lifecycle slice proves:

```text
Tick A: read another entity's HestiaAmmoState -> create child -> commit ChildState
Tick B: additive removal fact -> delete ChildState while child remains live
Tick C: DeadFact -> release child -> recycle slot with a new generation
```

A second slice injects a child-commit failure and verifies destruction rollback, child-handle invalidation, and one accepted-resource cleanup attempt.

## Allocation evidence

The earlier `GC.GetAllocatedBytesForCurrentThread()` checks were not valid evidence on this Mono runtime: the counter returned zero even for explicitly retained newly allocated arrays. All allocation tests now use Unity's synchronous [`GC.Alloc` recorder pattern](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Unity.Profiling.ProfilerRecorderOptions.SumAllSamplesInFrame.html), restricted to the current thread, with a positive control that must detect a new array. Zero recorded allocation events establish 0 B for the measured scope; nonzero events are reported as counts, not fabricated byte totals.

| Measured workload | Result |
| --- | --- |
| 512-entity representative pipeline, first tick resumed with one reducer work item per step | 0 events |
| Same representative pipeline, steady state | 0 events |
| 512-entity state-trigger pipeline, first use and steady state | 0 events |
| 512-entity resource-owning fact pipeline, steady state | 0 events |
| 512 entities with 602 transactional/batch registrations, eight ticks changing outputs | 0 events |
| 512-entity create/destroy churn, eight ticks | 0 events |

The mutation-changing timing workload initializes create and replace paths before the steady-state measurement. User callbacks and custom equality/disposal must themselves be allocation-free. Failure reporting is permitted to allocate exceptions and cleanup aggregates.

## Timing and reservations

One final Editor run of the 512-entity routed workload averaged **1.498 ms/tick** across eight ticks, including input admission. Every entity produces a changed output each tick. The last tick recorded:

| Phase | Milliseconds |
| --- | ---: |
| Maximum reducer callback | 0.0063 |
| Maximum committer callback | 0.0001 |
| Planning, including callbacks/equality | 0.2867 |
| Action validation | 0.0289 |
| Atomic apply and plan clearing | 0.0217 |
| Fact cleanup | 0.0453 |

These are observations, not latency ceilings. Callback maxima are included in their enclosing stages, so the rows are not additive. Clock checks cannot preempt callbacks, atomic application, or cleanup.

The isolated sparse-migration gate observed 1.439 ms/tick before and 1.440 ms/tick after migration on the then-unchanged workload. This supports retaining the shared mechanics; it is not a statistically established speedup. Subsequent runs use stricter scheduling boundaries, reliable allocation instrumentation, and outputs that change every tick, so they are not interchangeable baseline comparisons.

For 512 entities and 301 registrations in each transactional stage:

- Relevant eligibility checks remain **1,024**, identical to one relevant registration per stage. Unrelated registrations receive no per-tick eligibility checks.
- Candidate bit arrays reserve **38,528 B**, compared with **128 B** for one registration per stage. They reserve bits per possible pair, not candidate objects.
- Existing fired-marker elements reserve **1,232,896 B** (`512 * 602 * 4`), excluding array/object headers. They were retained as correctness guards.
- The workload has six registered fact types, including `DeadFact`: **3,072 payload slots** at one fact per type/entity. Payload byte size depends on the value type; the queue reserves 2,048 accepted entries from the four-facts-per-entity setting.
- A 512-row fact metadata layout reserves **8,192 element bytes** both before and after sparse consolidation: sparse indexes, full entity handles, and per-row counts. The positive-controlled construction probe observes three array allocations for the former layout and four allocations for the shared primitive plus those arrays. The extra owner object is an explicit initialization cost, not a RAM saving.
- State values, typed action arrays, and mutation arrays remain independently reserved per output. No claim is made that sharing sparse mechanics reduces payload reservation or total heap size.

## Evidence locations and limits

Fresh local artifacts are in `Logs/kissunitymcp/`: `UnityCompile.log`, `ImportSnapshot.txt`, `CITestOutput.xml`, `UnityTests.log`, `RiderMsBuild.log`, and `RiderMsBuild.console.log`. Extracted compiler/MSBuild error files are empty. Logs stay outside source control.

This proves the tested Unity Editor/Mono contract and workload shapes. IL2CPP/player throughput, production frame ceilings, full Entitas API parity, stable domain indexes, and disposable output-snapshot ownership remain outside this implementation. Output replacement/deletion still does not dispose copied state resources; changing that requires a separate lifetime contract.
