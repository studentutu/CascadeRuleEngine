# CascadeEngine Internal Overhaul

Status: proposed against the current MVP.

Scope: internal storage, incremental execution, reconciliation, and ECS capability parity. Preserve the compiled public API and the behavior documented in `Assets/CascadeEngine/Readme.md`.

## 1. Decision

The public model is correct:

```text
facts
  -> reduction to closure
  -> reconciliation
  -> durable output state
  -> typed mutation journal
```

Do not replace it with archetypes, systems, mutable components, or a generic ECS registry.

The overhaul has four jobs:

1. Make fact admission and commit prepare-then-apply operations. Failure must not leave partial state.
2. Consolidate entity membership around one generation-safe sparse-set implementation.
3. Make every incremental stage resumable, including transaction scans and reconciliation.
4. Make the settings-backed path mechanically unable to allocate or grow during an open tick.

The goal is one storage invariant, not one universal container. Durable state is one value per entity; facts are many values per entity; entity lifecycle is a generational allocator; queues and journals are stage-owned buffers.

## 2. Preserve the Working Baseline

Already implemented:

- generational, recyclable `EntityRef`;
- O(1) entity create/current-slot lookup/stale-handle rejection;
- transactional reducer-side create/destroy;
- additive `DeadFact` with durable deletion at closure;
- sparse durable output membership and compact queries;
- sparse typed fact slabs with contiguous per-entity spans;
- payload deduplication with distinct same-type fact preservation;
- immediate reducer and state-reducer continuation cursors;
- previous-snapshot reads for all committers;
- buffered normal commit decisions;
- fixed fact capacity and warmed allocation tests.

Do not reimplement these from scratch. Tighten their ownership and failure behavior.

Public contract freeze:

- preserve both constructors and all current public signatures;
- preserve feature builders, state/fact/query APIs, lifecycle, incremental ticks, mutations, and disposal;
- preserve fact multiplicity/deduplication and pending-dead visibility;
- preserve committed-state visibility during incomplete ticks;
- add no public sparse set, world, scheduler, storage facade, or reflection-based API snapshot.

Add a compile-only contract fixture before implementation. Internal folder placement does not hide a `public` C# type.

## 3. ECS Parity Boundary

Cascade needs ECS capabilities, not ECS mutation semantics.

| ECS capability | Cascade equivalent |
| --- | --- |
| create entity | `CreateEntity()` |
| delete entity | `DestroyEntity()` -> `DeadFact` -> closure |
| find current entity by runtime id | `TryGetEntity(Id_slot, out entity)` |
| add transient request/event | `Emit(entity, fact)` |
| add/replace durable component | committer returns `Set` |
| remove durable component | additive removal fact -> committer returns `Delete` |
| query components | state/fact queries |
| arbitrary cross-entity reducer logic | `IEntityQuery` plus committed-state reads |
| reactive changes | typed `StateMutation<TState>` |
| long-running churn | Id_slot reuse plus generation increment |

### No physical fact removal

Do not add `RemoveFact<TFact>` for an accepted fact.

A reducer may already have derived consequences from that fact. Removing it later cannot retract those consequences without dependency tracking or replaying the tick. Physical removal makes outcome depend on reducer order.

Model removal and cancellation additively:

```text
RemoveShieldRequestedFact
  -> validation
  -> ShieldRemovalAcceptedFact
  -> ShieldCommitter.Delete()
  -> one ShieldState delete mutation
```

Facts disappear automatically when the tick completes or fails. True retraction is a separate engine design requiring causal dependency tracking or deterministic tick replay.

## 4. Current Failures and Gaps

### P0 - Fact admission is not atomic

`FactStore.Emit` currently writes in this order:

```text
typed slab
-> accepted count
-> touched entity
-> fact route
-> total entity fact count
-> total-fact guardrail
-> queue
```

The total per-entity limit is checked after several writes. A failure outside an active tick can leave an accepted-but-unqueued fact because no tick rollback runs.

Required fix:

```text
Prepare:
  validate entity/generation/lifecycle
  resolve pre-bound route and slab
  detect duplicate
  calculate new membership/route changes
  validate causal/cardinality limits
  validate all fixed capacities
  complete allowed legacy growth

Apply:
  write payload
  update membership/counts/routes
  enqueue
  update diagnostics
```

After prepare succeeds, apply may only perform non-throwing array writes and count changes. Rejected/deduplicated/failed facts remain caller-owned; accepted facts become simulation-owned and are disposed once.

### P0 - Commit is not one transaction

Normal committer decisions are evaluated before normal output writes, which is correct. The tail is still split across output action lists, lifecycle deletions, entity release, and mutation buffers.

If state/mutation capacity growth or another managed failure occurs after one output is applied, `FailActiveTick` cannot restore that durable state.

Do not build a generic rollback framework. Make apply mechanically non-throwing:

- user committers, equality, conflict checks, and legacy growth run during planning;
- target generations, action uniqueness, and every capacity are prevalidated;
- lifecycle deletions are part of the same plan;
- apply contains array/index/count writes only;
- mutation journals become visible after state and lifecycle finalization;
- free-slot release is last and cannot grow.

Bevy explicitly guards against invalid parallel-array state when allocation fails mid-insert. Cascade's fixed path can avoid the problem entirely by preflighting and never allocating during apply.

### P1 - Budgeting ends before reconciliation

Current budgets do not cover:

- repeated transactional/batch eligibility scans;
- committer planning and conflict checks;
- lifecycle deletion planning;
- the final apply tail.

`RunTickIncremental` can therefore enter unbounded work after reduction closes.

Budget every resumable stage. Reducer, batch reducer, and committer callbacks remain atomic. A hard time limit can only be checked between callbacks; arbitrary user C# cannot be preempted. Document the maximum one-callback overshoot.

Final apply remains atomic because committed state is publicly readable. Measure its declared worst case before considering double-buffered state.

### P1 - Transactional and batch stages rescan

Immediate and state reducers preserve exact cursors. Entity transactional and batch stages restart touched-entity/registration scans after a budget suspension. Fired trackers prevent duplicate callbacks, but time is spent rediscovering old work and small slices can make poor forward progress.

Add exact registration/entity cursors. Keep fired trackers as correctness guards during migration.

### P1 - Fixed capacity is a convention outside fact payloads

The settings-backed constructor warms expected capacity, but state buckets, mutation/action lists, route lists, scratch buffers, and parts of entity/fact storage still expose growth.

Every owner needs an internal fixed/grow policy:

- fixed: fail before mutation;
- grow: resize during prepare only;
- no resize/factory/bucket creation is reachable after a settings-backed tick opens.

Keep the legacy constructor and `WarmupCapacityHints` source-compatible.

### P2 - Storage policy is duplicated

The issue is not raw file count. It is duplicated sparse ownership, generation checks, growth rules, and clear behavior.

- `DenseEntityCounter` and `DenseEntityObjectStore<T>` only hide arrays owned by `FactStore`; inline them.
- `DenseEntitySet` has a different membership rule and does not validate a full owner.
- `StateBucket<T>` and `FactBucket<T>` each reimplement sparse entity membership.
- `EntityRefBuffer` is legitimate stage scratch; keep it unless a direct array/count is clearer.

### P2 - Global worst-case fact memory can be large

Every registered fact type reserves:

```text
MaxEntities * MaxFactsPerTypePerEntity * sizeof(TFact)
```

With hundreds of types this can dominate memory. Do not add per-fact public capacity settings speculatively. First report schema counts/capacities at construction and measure production. Per-fact cardinality would be a separate public proposal.

## 5. Target Storage Model

```text
FactSimulation
|
+-- EntityStore
|   +-- status[], generation[], freeSlots[]
|   +-- EntitySparseSet<byte> pendingCreated/pendingDestroyed
|
+-- FactStore
|   +-- EntitySparseSet<byte> touchedEntities
|   +-- factCountBySlot[]
|   +-- routeScratchBySlot[]
|   +-- FactSlab<TFact> per registered fact type
|   +-- queuedFact[] + head/count
|
+-- StateBucket<TState>
|   +-- EntitySparseSet<TState> committed values
|   +-- prepared actions
|   +-- hidden mutation journal
|
+-- PartialSimulation
    +-- ReductionCursor
    +-- ReconciliationCursor
    +-- input revision and plan state
```

### `EntitySparseSet<TValue>`

One reusable internal primitive:

```text
sparse[entity.Value] -> dense row + 1
denseEntities[row]   -> complete EntityRef
denseValues[row]     -> TValue
```

It owns only storage mechanics:

- O(1) full-generation lookup;
- insert/replace;
- compact dense iteration;
- swap-back removal and moved-entry repair;
- dense-only clear;
- fixed/grow capacity enforcement.

It does not dispose values, record mutations, understand facts, or own lifecycle policy.

Membership-only consumers use `EntitySparseSet<byte>` rather than a second set implementation.

### `FactSlab<TFact>`

Facts remain a distinct one-to-many shape:

```text
EntitySparseSet<int> rows  // value = entity/type fact count
TFact[] payload            // row * perEntityCapacity + local index
```

Requirements:

- contiguous zero-copy `All<TFact>()`;
- dedupe within one entity/type slice;
- no individual fact deletion;
- fixed mode never repacks;
- grow mode repacks during prepare;
- clear disposes accepted facts, then clears row membership.

This reuses sparse membership without pretending facts are one-value components.

### `EntityStore`

Keep the flat generational allocator. Recycled slot ids are bounded by concurrent capacity, so a flat sparse index is simpler than Bevy/EnTT paged storage.

Use sparse sets for compact pending membership. Add a live-entity set only when a real state-independent enumeration call site exists; current queries already iterate narrower state/fact membership.

Queues, query buffers, action plans, and journals remain stage-owned typed buffers, not general stores.

## 6. Execution and Reconciliation Model

Explicit phases:

```text
Idle
-> ImmediateFacts
-> EntityTransactions
-> BatchTransactions
-> StateReducers
-> ClosureCheck
-> ReconciliationPlan
-> ReconciliationValidate
-> AtomicApply
-> Publish
-> Complete
```

Rules:

- cursor advances only after one atomic work item succeeds;
- budget suspension retains facts, lifecycle staging, cursors, and plan;
- incomplete results expose only previous committed state and previous completed journal;
- failure clears tick facts once and rolls back staged lifecycle;
- resumption cannot repeat completed work;
- planning produces at most one action per entity/output;
- apply and publish have explicit `Empty/Planning/Validated/Applied/Published` state.

### Late input during an incremental tick

Facts, create, and destroy calls between reduction steps join the open tick.

If input arrives after reconciliation planning started:

1. increment the input revision;
2. discard the unpublished plan;
3. return to reduction;
4. plan again from unchanged committed state.

Committers must be deterministic and side-effect-free because unpublished planning can be invalidated. Durable apply and mutation publication still happen once.

### Transactional semantic conflict

There is one existing conflict:

- `CascadeRuleEngineProposal.md` implies a new distinct required fact may create a new transactional frontier;
- README, progress, code, and tests define one invocation per registration/entity/tick.

Preserve the current README behavior in this internal refactor. Add a development diagnostic/test for a required fact arriving after that registration fired. A revision/frontier model can be proposed separately; it changes observable reducer invocation count.

### Idempotence boundary

Engine guarantees:

- one final durable action per entity/output/tick;
- no partial visible commit;
- no duplicate mutation record for one final action;
- continuation cannot apply/publish twice;
- equal durable state emits no mutation.

`ForEachMutation` is replayable. Calling it twice invokes the handler twice. The current API cannot guarantee exactly-once external side effects without a consumer cursor/ack contract. Do not claim exactly-once delivery or silently make the journal consumptive.

## 7. Implementation Generations

### Generation 1 - Atomic admission: mandatory thin vertical slice

Tests first:

- total per-entity guardrail failure outside a tick leaves slab, route, count, touch, and queue unchanged;
- fixed queue/route/slab overflow changes nothing;
- rejected facts remain caller-owned;
- accepted facts dispose once;
- a valid tick after failed admission is clean;
- warmed 512-entity allocation baseline is unchanged.

Then split fact admission into prepare/apply and pre-bind every settings-backed slab.

Gate: focused tests, zero-allocation proof, Rider/MSBuild compile, empty Unity compile-error report.

### Generation 2 - Consolidate sparse storage

Order:

1. implement/test `EntitySparseSet<TValue>`;
2. migrate one `StateBucket<TState>` vertical slice;
3. verify create/update/delete/query/mutation behavior;
4. migrate pending/touched membership;
5. layer one fact slab on `EntitySparseSet<int>`;
6. migrate remaining buckets;
7. inline fact counters/route scratch into `FactStore`;
8. delete `DenseEntitySet`, `DenseEntityCounter`, and `DenseEntityObjectStore<T>`.

Required proof:

- insert/replace/remove first-middle-last/swap repair;
- old generation never aliases a reused slot;
- clear visits dense members only;
- one/two-state queries remain compact;
- fact span/dedupe/disposal behavior is unchanged;
- high-iteration entity churn does not grow beyond concurrent capacity.

Gate: one sparse membership invariant, no obsolete helper consumer, no public API diff.

### Generation 3 - Exact cursors and enforced capacity

- derive one internal fixed/grow plan from construction mode;
- block every fixed growth path before write;
- add transactional and batch cursors;
- check elapsed time while scanning candidates;
- preserve current once-per-tick transaction semantics;
- report exact pending phase/entity/registration.

Test `MaxWorkItems = 1`, tiny time slices, new queued facts, batch eligibility, reducer-side create/destroy, cumulative tick guardrails, and no partial commit.

Gate: every reduction phase resumes exactly and settings-backed execution has no reachable growth operation.

### Generation 4 - Resumable reconciliation and ECS parity proof

- plan one entity/output action at a time under step budgets;
- include lifecycle deletions;
- invalidate unpublished plans on input revision change;
- preflight action/mutation capacity;
- use array-only final apply;
- expose journals after lifecycle finalization.

Add one Hestia scenario:

```text
Tick A: reducer queries another entity, creates child, initializes ChildState
Tick B: removal fact deletes ChildState while child remains live
Tick C: DeadFact destroys child, stale handle fails, slot reuses with new generation
```

It must prove create, destroy, `TryGetEntity`, state add/remove, arbitrary cross-entity read, incremental continuation, and one typed mutation per final change.

Gate: planning suspension/failure changes no durable state; apply/publish occurs once; worst-case atomic apply is measured.

## 8. Verification Matrix

| Invariant | Proof |
| --- | --- |
| stale handles | old generation cannot read, emit, query, delete, or target a plan |
| bounded churn | high-iteration create/destroy causes no capacity growth |
| atomic admission | every failed prepare leaves all fact structures unchanged |
| monotonic closure | accepted facts cannot be physically removed |
| incremental progress | exact phase/cursor after every suspension |
| snapshot isolation | all committers read pre-commit state |
| atomic commit | plan failure/suspension changes no durable state |
| idempotent apply | one action/journal row per entity/output/tick |
| lifecycle transaction | state deletion and slot release finalize together |
| zero allocation | warmed full emit/reduce/plan/apply/publish reports 0 B |
| disposal | accepted facts/runtime resources release once |
| compatibility | compile-only public API fixture remains unchanged |

Every generation runs:

1. focused tests;
2. mandatory compile/rebuild (kiss-unity-mcp)
3. Running tests (kiss-unity-mcp)
4. `git diff --check`;
5. allocation measurement for each changed hot path.

## 9. Explicit Non-Goals

- archetypes, tables, chunks, or system ordering;
- mutable output access from reducers;
- physical fact retraction;
- global facts;
- runtime reflection or unsafe/type-erased columns;
- paged sparse arrays without a measured slot-space problem;
- public per-fact capacities without measured schema memory;
- generic rollback framework;
- double-buffered output before apply-tail measurement;
- exactly-once consumer delivery without cursor/ack;
- public all-entity enumeration without a production call site;
- universal repository/pool/storage abstraction.

## 10. Primary Sources

- Bevy storage overview: <https://docs.rs/bevy_ecs/latest/bevy_ecs/storage/index.html>
- Bevy sparse-set source: <https://docs.rs/bevy_ecs/latest/src/bevy_ecs/storage/sparse_set.rs.html>
- Bevy storage tradeoff: <https://docs.rs/bevy_ecs/latest/bevy_ecs/component/enum.StorageType.html>
- EnTT storage/entity design: <https://github.com/skypjack/entt/wiki/Entity-Component-System>

Applicable lessons:

- sparse lookup plus dense owner/value arrays fits add/remove state;
- swap-back removal must repair the moved sparse entry;
- Cascade must validate full generations in every build because its stores are independently resumable;
- multi-array writes need one failure policy;
- sparse sets favor random lookup and structural churn over maximum iteration speed;
- paged sparse arrays solve large sparse id spaces, which recycled bounded Cascade slots currently avoid.

## 11. Done

The overhaul is complete when:

- fact admission cannot partially mutate tick state;
- one generation-safe sparse set owns reusable entity membership;
- facts use one typed multi-value slab layered on that membership;
- old dense helper clutter is deleted;
- all reduction and reconciliation stages resume exactly;
- late open-tick input reaches closure before commit;
- commit planning can suspend without visible output;
- durable state, mutation journal, and slot release finalize once;
- settings-backed ticks cannot allocate or grow;
- warmed 500+ entity end-to-end tests report 0 B;
- ECS parity scenario covers lifecycle, lookup, state add/remove, cross-entity query, facts, and mutations;
- physical fact removal remains outside the core;
- the existing public API remains source-compatible.
- minimal set of good quality primitives
