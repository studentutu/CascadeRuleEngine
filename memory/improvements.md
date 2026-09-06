# CascadeEngine Internal Overhaul

Status: revised proposal against the ownership-hardened baseline, 2026-09-06. The generations below are outstanding work, not a description of implemented behavior.

Scope: internal transaction boundaries, scheduling, capacity enforcement, and storage ownership. Preserve existing public signatures and valid observable behavior. This refactoring does not establish full Entitas parity.

## 1. Decision and Expected Benefits

Preserve the public model:

```text
facts
  -> reduction to closure
  -> reconciliation
  -> durable output state
  -> typed mutation journal
```

The implementation order follows the remaining risks:

1. Make commit preparation and application one transaction, including lifecycle deletion.
2. Replace transactional/batch global eligibility scans with fact-routed candidates and exact continuation cursors.
3. Make reconciliation resumable and enforce capacity before every accepted write.
4. Consolidate sparse storage only after one state bucket and one fact slab prove the shared primitive.

| Change | Expected benefit | Evidence required |
| --- | --- | --- |
| Prepared atomic commit | A planning failure cannot leave partially changed durable state | Fault-injection tests across outputs, entities, equality, and lifecycle deletion |
| Fact-routed candidates plus exact cursors | Less unrelated scheduler work and less repeated work after suspension | Eligibility counts and continuation latency with 512 entities and a large unrelated registry |
| Budgeted reconciliation | Commit planning no longer forms an unchecked tail after reduction | Suspension tests and separate planning/apply/cleanup timings |
| Enforced runtime capacity | Capacity exhaustion fails before logical mutation instead of growing during gameplay | Boundary/overflow tests and allocation measurements |
| Shared generational sparse storage | One tested implementation of membership, generation checks, and capacity rules | Simpler ownership plus no measured performance regression |
| Inlining small wrappers | Potentially clearer ownership of arrays | Fewer competing invariants at actual call sites, not merely fewer files |

Do not promise a general speedup, lower RAM use, or Entitas throughput parity from this refactor. Current fact storage is already flat and typed. Reserved capacity can improve execution predictability while increasing memory use.

Keep a small set of internal primitives: typed fact slabs, durable state buckets, prepared typed actions, stage-owned buffers/cursors, and the existing cleanup-error collector. A shared sparse set is a candidate storage primitive, not a prerequisite for fixing transactions or scheduling.

## 2. Preserve the Working Baseline

Already implemented:

- generational, recyclable entity handles and O(1) current-slot lookup;
- transactional reducer-side creation and destruction;
- additive `DeadFact`, pending-dead visibility through closure, and durable deletion at closure;
- sparse durable output membership and compact queries;
- sparse typed fact slabs with contiguous per-entity spans;
- payload deduplication with distinct same-type fact preservation;
- immediate/state reducer continuation and terminal negative-rule continuation;
- closure-safe direct reducer `Without<TFact>()` with sealed condition types and host input;
- output `Without<TFact>()` reconciliation over previous committed members;
- previous-snapshot reads for every committer and buffered normal commit decisions;
- fixed fact-slab capacity and warmed allocation tests.

The recent ownership slice also implemented:

- total per-entity admission checks and queue/route reservation before payload ownership;
- construction-time guardrails for idle emissions;
- fact ownership across incremental pauses and commit planning;
- one disposal attempt per accepted fact on closure, failure, or teardown;
- exhaustive cleanup with `CleanupErrors`, retaining original errors alongside cleanup failures;
- terminal cleanup of registration trees, static routes, and runtime scratch storage, including after prior feature disposal;
- preservation of committed state, mutations, and completed `LastResult` when post-publication fact cleanup fails;
- rejection of nested ticks, simulation disposal inside tick callbacks, warmup during open ticks, and new input during fact cleanup.

Verification at this baseline: 83 EditMode tests passed; Unity import and Rider/MSBuild passed; the warmed 512-entity resource-fact, representative pipeline, and state-trigger slices reported 0 B steady-state allocation. These measurements cover their tested shapes, not every capacity boundary or player platform.

Do not redo that ownership work. Preserve its regression tests while tightening the remaining boundaries.

Public contract freeze:

- preserve both constructors, all current public signatures, and existing adapters;
- preserve fact multiplicity, once-per-tick transactional invocation, pending-dead visibility, and previous-state reads;
- preserve terminal negative-rule sealing and output absence reconciliation;
- preserve mutation visibility and replayable consumption as defined in section 6;
- preserve reducer-work accounting: `MaxWorkItems` and `ProcessedWorkItems` count reducer invocations, not internal scans or committer planning;
- add no public sparse set, scheduler, world, storage facade, or reflection-based API snapshot;
- remain within C# 8 / Unity 6 and existing dependencies.

Add a compile-only contract fixture before implementation. A type declared `public` is part of the compatibility review even when its file lives in `Internal`.

## 3. ECS Parity Boundary

Cascade needs ECS capabilities within its fact/closure/commit contract.

| ECS capability | Existing Cascade equivalent |
| --- | --- |
| create entity | `CreateEntity()` |
| destroy entity | `DestroyEntity()` -> `DeadFact` -> closure |
| current runtime-slot lookup | `TryGetEntity(slotId, out entity)` |
| transient request/event | `Emit(entity, fact)` |
| add/replace durable component | committer returns `Set` |
| remove durable component | additive removal fact -> committer returns `Delete` |
| component queries and cross-entity reads | `IEntityQuery` plus committed-state/fact views |
| reactive output changes | typed `StateMutation<TState>` |
| terminal teardown | `FactSimulation.Dispose()` |
| long-running entity churn | slot reuse with generation increment |

This overhaul strengthens those capabilities. It does not add stable domain-key indexes, one-to-many indexes, `AnyOf`, live groups, generated listeners, or multi-context transactions. Use the package README parity matrix for the full boundary.

Do not add physical removal of an accepted fact. Derived consequences would remain after its removal, making results depend on reducer order. Cancellation stays additive:

```text
RemoveShieldRequestedFact
  -> validation
  -> ShieldRemovalAcceptedFact
  -> ShieldCommitter.Delete()
  -> ShieldState delete mutation
```

## 4. Remaining Failures and Gaps

### P0 - Commit is not one transaction

Committer decisions are buffered, but application still calls `StateBucket.Set`, which can grow storage and invoke user-defined equality. Lifecycle deletion, mutation recording, and slot release follow through separate operations.

Concrete failure:

```text
output A applies its decision
-> output B's state equality throws
-> failed-tick cleanup runs
-> output A is already changed and cannot be restored
```

Move potentially throwing work into preparation:

- committer callbacks, equality, conflict checks, and permitted legacy growth;
- target-generation validation and one-action-per-entity/output validation;
- normal state changes and pending-destruction deletions;
- state, action, mutation, and free-slot capacity checks.

After validation, apply performs only prepared array/index/count operations. It must not call user code, evaluate equality again, allocate, or resize. Publish the journal after state/lifecycle finalization. Release recyclable slots only after their previous owners can no longer participate.

Do not build a generic rollback framework. Prevent recoverable failures after application starts. Fact disposal remains after publication; its failure reports cleanup errors and must not roll back or hide a committed tick.

### P1 - Transactional and batch eligibility uses global scans

Current scheduling checks touched entities against registrations, including unrelated registrations. After suspension, scans start again; fired trackers prevent duplicate callbacks but do not remove the repeated eligibility work.

Exact cursors alone solve only the repetition. Route accepted fact types to the registrations waiting for them, then evaluate affected entity/registration candidates.

Requirements:

- bind waiters during feature registration;
- deduplicate pending candidates without losing a candidate when another required fact arrives later;
- preserve one invocation per entity/registration/tick and batch eligibility at existing logical phase boundaries;
- preserve candidates and exact cursors across suspension;
- route newly accepted facts from every reduction stage;
- keep fired trackers as correctness guards until equivalent coverage proves any replacement;
- measure eligibility checks separately from public reducer-work counters.

State-presence iteration and output absence reconciliation are intentional membership scans. Profile them separately; do not remove them under the name of fact routing.

### P1 - Budgeting ends before reconciliation

Eligibility scans, commit planning, conflict/equality work, lifecycle planning, application, and cleanup are not uniformly covered by time checks.

Budget all resumable engine work, including candidate collection and plan validation. Preserve existing public reducer-work accounting; use internal diagnostics for scan/plan work.

Time limits are cooperative. User callbacks cannot be preempted, and final application must remain atomic while committed state is publicly readable. Check time before atomic units and measure the maximum callback, apply tail, and cleanup tail separately. Do not advertise an absolute wall-clock ceiling. A long callback or finalization tail can exceed the requested slice.

Do not introduce double-buffered state or resumable resource cleanup until measurements justify their additional lifetime rules.

### P1 - Admission and fixed capacity still need complete preflight

The original total-fact admission bug is fixed. Full enforcement across all owners is not.

The remaining admission target is:

```text
Prepare:
  validate identity, lifecycle, duplicates, and cardinality
  validate queue, route, touched-membership, and slab capacities
  complete permitted legacy growth without acquiring the proposed payload

Apply:
  write payload, membership, counts, route, and queue
  report acceptance
```

Preparation failure may reserve larger legacy backing arrays, but must not change logical fact membership, ownership, accepted counts, or queued work. Existing rejection/deduplication diagnostics remain valid.

Derive one internal fixed/grow policy from construction:

- fixed: reserve known runtime storage before gameplay; fail before logical mutation;
- grow: permit resize during preparation only;
- no engine-owned resize, bucket factory, or lazy candidate allocation may be reached during settings-backed execution, including root admission and continuation.

Account for candidate queues, fired markers, commit plans, mutation journals, and lifecycle buffers. Do not choose an unmeasured full entity-by-registration allocation merely to eliminate growth.

A successful 0 B benchmark is evidence for one workload. It does not replace capacity-boundary tests or prove arbitrary user callbacks allocate nothing.

### P2 - Storage rules are duplicated

`StateBucket<T>` and `FactBucket<T>` duplicate parts of sparse membership. `DenseEntitySet` uses a different ownership check. A shared generational primitive can reduce the number of invariants to maintain.

Evaluate `DenseEntityCounter` and `DenseEntityObjectStore<T>` at their call sites. Inline them only if the containing owner becomes clearer without duplicating mechanics elsewhere. Keep `EntityRefBuffer` where it makes stage scratch ownership explicit.

Deleting named helpers is not a completion criterion. Migrate and remove only abstractions superseded by a proven implementation.

### P2 - Fact reservation and output-resource ownership are separate concerns

The current and proposed slabs both reserve approximately:

```text
sum over registered fact types:
  MaxEntities * MaxFactsPerTypePerEntity * payload slot size
```

Metadata and any separately allocated payload resources add to that reservation. Sparse membership consolidation does not remove it. Report schema counts, reserved capacities, and measured memory before proposing per-fact limits or alternate slab layouts.

Disposable output states retain snapshot semantics: terminal teardown disposes current stored states, but replacement, deletion, and abandoned commit decisions do not automatically dispose copied resources. A shared sparse set cannot decide that ownership.

Do not automatically dispose discarded `Set` values or `Previous` mutation payloads. They can alias existing state or other snapshots. Define a separate public lifetime contract before changing this behavior. Facts/views borrow their payload; committers must not retain disposable fact resources in durable state.

Likewise, `void Emit` does not report acceptance. An explicit acceptance-result API would be a separate additive proposal, not an incidental part of internal refactoring.

## 5. Candidate Storage Primitives

Keep the generational entity allocator and the distinct storage shapes:

```text
FactSimulation
+-- EntityStore: status, generation, free slots, pending lifecycle membership
+-- FactStore: touched membership, counters, accepted routes, typed fact slabs, queue
+-- StateBucket<TState>: committed values, prepared actions, mutation journal
+-- PartialSimulation: phase, exact cursors, pending candidates, unpublished plan
```

### EntitySparseSet<TValue>

Candidate internal layout:

```text
sparse[entity.Value] -> dense row + 1
denseEntities[row]   -> complete EntityRef
denseValues[row]     -> TValue
```

Its responsibility is storage mechanics: full-generation lookup, insertion/replacement, dense iteration, swap-back removal with moved-entry repair, dense-only clearing, and capacity preflight.

It must not dispose values, emit mutations, or own entity lifecycle. Owners retain those responsibilities, including the existing exhaustive cleanup behavior.

Membership-only use of `EntitySparseSet<byte>` is an option, not a mandate to add an unused values array everywhere. Compare clarity and metadata cost before migration.

### Typed fact slab

Facts remain many values per entity with contiguous spans:

```text
EntitySparseSet<int> rows  // entity/type count, if the migration proves useful
TFact[] payload           // row * perEntityCapacity + local index
```

Preserve:

- zero-copy `All<TFact>()`, deduplication, and immutable accepted membership;
- stable row/payload alignment throughout an open tick;
- no swap-back row removal while queued fact indices or borrowed spans depend on that row;
- no fixed-mode repacking; legacy growth only during preparation;
- one disposal attempt per accepted payload, followed by complete ownership/membership clearing even when callbacks throw.

Do not force facts into one-value component storage. Do not add a generalized storage facade around the two shapes.

### Stage-owned buffers and cursors

Queues, candidates, query scratch, action plans, and journals belong to their execution stage. Use typed reusable buffers and explicit cursors. Keep phase as one source of truth; avoid parallel flags that independently encode the same plan status.

The existing `CleanupErrors` primitive remains the shared failure collector. Storage consolidation must preserve terminal route unbinding and scratch release.

## 6. Execution, Input, and Publication Contracts

Logical flow, with resumable cursors inside each applicable stage:

```text
Idle
-> PositiveClosure:
     immediate facts -> entity transactions -> batch transactions -> state reducers
     repeat applicable work until positive closure
-> TerminalWithout:
     seal condition types and host input
     evaluate deferred negative triggers
     drain resulting positive consequences to closure
-> ReconciliationPlan
-> ReconciliationValidate
-> AtomicApply
-> Publish
-> FactCleanup
-> Complete
```

Skip terminal negative evaluation when no negative rules exist. Preserve existing scheduling semantics for facts derived after negative evaluation: sealed condition facts remain forbidden, and accepted positive consequences still reach closure before commit. Output `Without<TFact>()` belongs to reconciliation over the final fact set, not to the reducer negative stratum.

Rules:

- suspension retains facts, pending lifecycle changes, candidates, cursors, and any unpublished plan;
- incomplete execution exposes the previous committed state and no partial new journal;
- current behavior clears the previous mutation journal at `BeginTick`; preserve that behavior;
- retaining the previous journal throughout an open tick would be a separate observable-contract change;
- pre-application failure discards unpublished plans, rolls back staged lifecycle, and attempts all fact cleanup;
- post-publication cleanup failure retains the committed result, journal, and completed `LastResult`;
- one final action and at most one mutation exist per entity/output/tick;
- `ForEachMutation` remains replayable and non-consumptive.

Use explicit execution phases and cursors without introducing public scheduler APIs. Do not redefine `MaxWorkItems` to count committers or eligibility scans silently.

### Late input during incremental execution

Preserve the existing input boundary:

- while host input remains open, new facts/create/destroy calls join the current tick;
- after terminal negative evaluation starts, those calls remain rejected until closure;
- resuming a sealed tick must never reopen its input.

Resumable reconciliation creates a new externally observable pause. If input is still permitted at that pause:

1. record the accepted input/lifecycle revision;
2. invalidate the unpublished plan before resuming;
3. return to reduction and process routed candidates;
4. replan from unchanged committed state.

Do not invalidate a plan for a deduplicated emission that changed no inputs. Do not rerun already-fired transactional callbacks. For a sealed tick, reject late input rather than restarting negation.

Committers must be deterministic and side-effect-free because unpublished planning may repeat. Plan invalidation must clear borrowed references without inventing disposable-output ownership. Measure invalidation frequency and time to closure under sustained host input; do not silently drop or defer accepted input to avoid starvation.

### Transactional semantics and external effects

Preserve one invocation per entity/registration/tick. New distinct required facts do not create another transactional frontier. A frontier/revision model would change observable invocation counts and needs a separate proposal.

A single final mutation is not exactly-once external delivery. Calling `ForEachMutation` twice still invokes the consumer twice. Consumer cursors/acknowledgements are outside this refactor.

## 7. Implementation Generations

### Generation 1 - Atomic commit: mandatory thin vertical slice

Tests first:

1. Bootstrap two outputs with previous state.
2. Submit changes to both.
3. Make the second output's equality throw during what is currently application.
4. Assert that both outputs retain their previous state and no partial new journal appears.
5. Repeat across output/entity order, with pending destruction and reducer-created entities.

Then move equality, state/mutation capacity preparation, and lifecycle deletions into one validated plan. Apply only prepared writes and finalize lifecycle once. Preserve exhaustive fact cleanup on both planning failure and successful publication.

Include unchanged decisions, no-op deletion, repeated destruction, stale handles, and post-publication disposal failure. Preserve existing admission tests.

Gate: no recoverable planning failure produces partial durable state; callbacks/growth are absent from apply; the public fixture and focused tests pass.

### Generation 2 - Fact-routed candidates and exact cursors

- Bind transactional and batch waiters to accepted fact routes.
- Evaluate only affected entity/registration candidates and preserve required-fact completeness.
- Retain exact collection/dispatch cursors across every suspension.
- Preserve batch membership at existing phase boundaries and once-per-entity invocation.
- Add internal eligibility-check/candidate counters without changing public work accounting.
- Size reusable candidate storage explicitly and measure its memory cost.

Thin performance slice: 512 entities, a small relevant rule chain, and hundreds of unrelated registrations. Verify zero unrelated eligibility checks after registration, correct closure under `MaxWorkItems = 1`, late required facts, incremental create/destroy, negative-derived consequences, and 0 B steady-state allocation after warmup.

Gate: scheduler work follows relevant routes; resume does not restart completed scans; callback semantics remain unchanged.

### Generation 3 - Budgeted reconciliation and full capacity enforcement

- Plan one entity/output decision at a time, including output absence and lifecycle deletion.
- Budget candidate collection, planning, and validation with exact cursors.
- Preserve journal clearing, input sealing, and permitted late-input invalidation.
- Finish fixed/grow preflight for admission and every runtime buffer.
- Verify below-capacity, exact-capacity, and overflow cases before logical mutation.
- Check legacy growth compatibility separately.
- Measure callback, planning, validation, atomic apply, and cleanup durations independently.

Test suspension and input arrival during reconciliation, repeated plan invalidation, tiny time slices, and failures with resource-bearing facts. Final application remains atomic; report its measured cost rather than claiming preemption.

Gate: every resumable stage makes forward progress without partial publication, and settings-backed engine work cannot grow storage after initialization.

### Generation 4 - Conditional sparse-storage consolidation and lifecycle proof

1. Implement/test the candidate generational sparse primitive.
2. Migrate one state bucket; verify create/replace/delete, query, mutation, and capacity behavior.
3. Migrate one fact slab; verify row alignment, multiplicity, spans, disposal, and continuation.
4. Compare runtime, allocation, reserved metadata, and ownership complexity.
5. Extend the migration only where those results justify it.
6. Remove superseded helpers only after their last consumer is migrated.

Required storage proof: swap-back first/middle/last removal, moved-entry repair, stale-generation rejection, dense-only clear, bounded entity churn, and failure-safe preparation.

Final Hestia regression slice:

```text
Tick A: reducer reads another entity, creates child, initializes ChildState
Tick B: removal fact deletes ChildState while child remains live
Tick C: DeadFact destroys child; old handle fails; slot reuses with a new generation
```

Verify incremental execution, one mutation per final change, accepted fact cleanup, and terminal teardown. This proves the existing migration boundary; it does not certify full Entitas parity.

Gate: fewer competing storage invariants without measured performance regression. Reject a generic migration that merely moves the same complexity behind more layers.

## 8. Verification Matrix

| Invariant | Proof |
| --- | --- |
| public compatibility | compile-only fixture plus existing behavioral tests |
| stale handles | old generation cannot read, emit, query, delete, or target a prepared action |
| bounded churn | repeated create/destroy stays within concurrent capacity |
| admission ownership | rejection/failed preparation leaves logical membership and payload ownership unchanged |
| atomic commit | equality/conflict/capacity failure leaves all durable state unchanged |
| snapshot isolation | every committer reads the pre-commit snapshot |
| lifecycle transaction | state deletion and entity release finalize together |
| routed scheduling | unrelated registry growth does not increase per-tick eligibility work |
| incremental progress | exact pending phase/cursor without rediscovering completed work |
| negative sealing | late forbidden input is rejected and negative consequences reach closure |
| journal visibility | begin-tick clearing and replayable final consumption remain unchanged |
| disposal | each accepted fact gets one attempt; failures do not strand later owners or hide publication |
| fixed capacity | exact boundary succeeds; overflow fails before logical mutation |
| zero allocation | warmed 512-entity engine paths report 0 B, including relevant capacity-boundary shapes |
| frame cost | callback, planning, apply, and cleanup timings are reported separately |
| memory cost | schema/candidate/slab reservations are measured before and after migration |

For each generation run focused tests, appropriate kiss-unity-mcp compilation/import and EditMode tests, allocation measurements for changed hot paths, and `git diff --check`. Retain fresh logs and diagnostic evidence. Editor measurements do not establish IL2CPP/player performance; verify the intended deployment backend before claiming production throughput parity.

## 9. Explicit Non-Goals

- archetypes, chunks, mutable reducer output, ordered gameplay systems, or physical fact retraction;
- global facts or a universal repository/pool/storage facade;
- runtime reflection, unsafe/type-erased columns, or new frameworks/packages;
- full Entitas parity, new matcher/index APIs, or generated listeners;
- automatic disposal of replaced/deleted/output-plan snapshots;
- a new acceptance-result API hidden inside admission refactoring;
- public per-fact capacity settings without measured need;
- general multi-stratum negation or new transactional frontier semantics;
- retaining previous journals through open ticks without a separate contract decision;
- double-buffered state, generic rollback, or resumable cleanup without measured justification;
- exactly-once external delivery or an absolute wall-clock execution ceiling;
- mandatory removal of small helpers solely to reduce file count.

## 10. Background References

Existing proposal references, retained for storage implementation review:

- [Bevy storage overview](https://docs.rs/bevy_ecs/latest/bevy_ecs/storage/index.html)
- [Bevy sparse-set source](https://docs.rs/bevy_ecs/latest/src/bevy_ecs/storage/sparse_set.rs.html)
- [Bevy storage tradeoffs](https://docs.rs/bevy_ecs/latest/bevy_ecs/component/enum.StorageType.html)
- [EnTT storage/entity design](https://github.com/skypjack/entt/wiki/Entity-Component-System)

Use these as implementation references, not evidence of Cascade performance. Current package contracts and local regression tests take precedence over an external engine's ownership or execution semantics.

## 11. Done

The required overhaul is complete when:

- preparation failure cannot partially accept a fact or change durable state;
- state writes, mutations, and lifecycle finalization form one validated transaction;
- transactional/batch work is routed to relevant candidates and resumes exactly;
- reconciliation can suspend without changing committed state or exposing partial output;
- negative sealing, permitted late input, and existing mutation visibility are preserved;
- settings-backed engine execution cannot allocate or grow its runtime storage;
- accepted facts and terminal resources retain exhaustive, one-attempt cleanup;
- warmed 512-entity allocation, scheduling, capacity, and lifecycle tests pass;
- measured callback/apply/cleanup costs and reserved memory are documented;
- public signatures and valid existing behavior remain compatible.

Sparse consolidation is accepted only where it demonstrably simplifies ownership without performance regression. Full Entitas parity and disposable output-resource ownership remain separate decisions.
