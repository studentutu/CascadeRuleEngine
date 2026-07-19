# CascadeEngine Incremental Refactoring Proposal

Status: proposed sequence after the completed generational-lifecycle Slice 1.

This document deliberately does not propose Slice 1 again. The implemented baseline is:

- `EntityRef` is `(Value, Generation)`.
- `Value` is a recyclable runtime slot.
- stale generations are rejected.
- slots are returned only after committed closure or failed-tick rollback cleanup.
- `EntityStore` lookup and allocation are O(1).

The remaining work must preserve that contract while replacing the inconsistent internal storage model and making incremental reduction plus reconciliation reliable under real production churn.

## 1. Original Direction That Must Not Be Lost

The initial request explicitly required:

> remove current clutter with different kind of storages and prefer to use a simple sparse-set for the entities! Look into the implementation of bevy-ecs

It also identified the operational reason:

> Current max-entity and monotonic ids approach doesn't work (30 minutes of constant reduction loop and movement already breaks this).

> we need to use sparse-set as is done in bevy-ecs in order to not be limited by the facts/archetype/no-hard-max-id-constraints

> very weak reusable storage implementation ... Find from web solutions to mitigate it

The intent is more important than mechanically introducing a class named `SparseSet`.

The internal stores must be fixed because the engine needs all of the following at the same time:

1. Memory must be bounded by maximum concurrent workload, not by lifetime entity or fact churn.
2. Every store must use the same entity identity rule. A slot match without a generation/owner match must never expose data owned by a previous entity.
3. Lookup must be O(1), while queries iterate only compact active membership rather than scanning every possible slot.
4. Creation, destruction, state addition, and state removal must remain safe across an incremental tick that spans multiple frames.
5. Warmed production execution must not allocate. Growth is a cold-path or explicitly selected legacy behavior.
6. Failure or suspension must not leave one store cleared, advanced, or committed while another store still represents the previous stage.
7. Commit and published output must be idempotent. Consumers must never observe half of a reconciliation.
8. The package must have fewer storage concepts, with explicit ownership, lifecycle, and capacity rules.

This is not an exercise in copying an ECS. It is how Cascade avoids the same lifetime and hidden-state problems while preserving the fact → closure → reconciliation model.

### What "no hard max id" can realistically mean

Sparse sets and generational reuse remove the lifetime-created/highest-id constraint. They do not make production capacity infinite.

An unbounded number of concurrent entities or facts is incompatible with a zero-allocation hot path: memory must either be reserved ahead of time, grow during execution, or reject overflow. Cascade's production contract should remain:

- `MaxEntities` bounds concurrent live/pending entities, not lifetime creations;
- fact limits bound one tick's declared worst case;
- sparse membership prevents absent entities/states from consuming dense value rows;
- recycled slots prevent 30 minutes of churn from increasing the highest runtime id;
- the legacy grow-on-demand path may allocate when a host explicitly accepts that tradeoff.

The refactor must remove hard limits caused by monotonic lifetime identity and dense highest-id storage. It must not pretend configured concurrent capacity can disappear.

## 2. What To Take From Bevy

The reference used for this proposal is Bevy ECS 0.19.0.

Bevy separates entity allocation from component membership. Its entity lifecycle frees an index only after despawn and advances the generation so previous handles become invalid. That is the part already implemented by Slice 1. See Bevy's [entity lifecycle and allocator source](https://github.com/bevyengine/bevy/blob/v0.19.0/crates/bevy_ecs/src/entity/mod.rs).

Bevy's component sparse set has three relevant pieces:

```text
sparse entity index -> dense row
dense row -> entity owner
dense row -> component value
```

The sparse array gives O(1) membership lookup. Dense owners and values give compact iteration. Removal uses dense compaction and repairs the moved entity's sparse entry. See Bevy's [sparse-set source](https://github.com/bevyengine/bevy/blob/v0.19.0/crates/bevy_ecs/src/storage/sparse_set.rs) and [storage overview](https://docs.rs/bevy_ecs/latest/bevy_ecs/storage/index.html).

Cascade should copy those invariants, not Bevy's implementation:

- Keep typed C# arrays. Do not introduce unsafe type-erased columns.
- Store the complete `EntityRef` as the dense owner and validate it in every build. Bevy can rely more heavily on its `World` lifecycle; Cascade's stores are independently resumable and should not.
- Use swap-back removal for durable state and long-lived membership.
- Pre-size sparse and dense arrays from `CascadeSettings`.
- Keep registration/type dictionaries in the cold routing layer. Do not use dictionaries for per-entity membership.

Bevy also demonstrates what not to flatten into one abstraction. It has different storage strategies for different access shapes. Cascade must do the same:

- one durable value per entity/type is a sparse set;
- touched/pending membership is a sparse entity set;
- multiple facts per entity/type require a sparse multi-value slab;
- FIFO reduction work is a queue;
- resumable query results are buffers/cursors.

Forcing all five into one generic "reusable storage" would be over-engineering and would hide the semantics that matter.

## 3. Current Storage Problems

| Current type | Useful behavior | Problem to remove |
| --- | --- | --- |
| `EntityStore` | Generational O(1) slot ownership from Slice 1 | It has no compact live-entity membership for state-independent iteration; pending membership still depends on the old dense-set helper |
| `StateBucket<TState>` | It is already structurally close to a sparse set | Sparse indexing, values, mutation publication, disposal, and growth policy are mixed in one class |
| `FactBucket<TFact>` | Sparse entity-to-slab lookup and contiguous per-entity facts are the correct general shape | It independently reimplements sparse ownership/capacity rules and can lazily allocate or repack |
| `DenseEntitySet` | Add-once touched membership | `bool[] + List<EntityRef>` has a separate membership convention and does not validate the dense owner in `Contains` |
| `DenseEntityCounter` | O(1) tick-local counters | It is a generic wrapper around one array but depends on another store to clear correctly |
| `DenseEntityObjectStore<T>` | Reuses per-slot reference objects | Its name hides that objects survive entity generations; the factory may allocate on first gameplay access if warmup is incomplete |
| `EntityRefBuffer` | Correct allocation-free scratch/query buffer | It is legitimate, but its capacity must be owned by the stage using it |
| `FactStore` dictionaries | Cold type registration and erased access | Some lazy bucket creation and repeated bucket resolution still occur after execution begins |
| `PartialSimulation` fields | Immediate-fact and state-reducer continuation already retain some cursors | Transactional and batch stages rescan prior entities/registrations after suspension; reconciliation is still one monolithic tail |

The problem is duplicated policy, not merely the number of files. Today each helper makes its own decision about:

- whether a slot is enough or a full generation must match;
- who owns sparse capacity;
- whether growth is allowed during execution;
- whether clear scans capacity or only touched entries;
- whether data survives a generation change;
- whether an incomplete tick may retain references to the store.

Those decisions must become explicit and consistent.

## 4. Non-Negotiable Invariants

Every slice below must preserve these invariants.

### Public contract

- Do not remove or rename existing public types or methods.
- `EntityRef.Value`, `EntityRef.Generation`, equality, and stale-handle behavior remain unchanged.
- `CreateEntity`, `DestroyEntity`, `TryGetEntity`, `Emit`, committed-state reads, entity queries, tick methods, and typed mutations keep their public signatures.
- Do not add a second public world/storage facade.
- New public primitives require a demonstrated production call site. Internal refactoring alone is not justification.
- no obsolete implementation (hard-cutoff)

### Reduction semantics

- Facts are additive observations/requests for one tick.
- Reducers read committed state plus accumulated tick facts.
- Reducers emit facts; they never mutate durable output.
- Committers alone decide durable output.
- Pending-dead entities remain reduction-visible through closure.
- Reducer order must not change final durable output.

### Storage semantics

- A sparse lookup starts with `EntityRef.Value` and succeeds only when the dense owner equals the complete `EntityRef`.
- Sparse entries use `0` as missing and store `denseIndex + 1`.
- Durable removal uses swap-back and repairs the moved owner's sparse entry.
- Tick-local clear visits touched/dense entries, not every configured entity slot.
- Fixed production storage throws before mutation when capacity is insufficient.
- Grow-on-demand remains a legacy/prototyping policy and may allocate only where already documented.

### Incremental and failure semantics

- A budget stop occurs only between atomic work units.
- A continuation cursor advances only after its work unit completes.
- Incomplete results never publish mutations or alter committed state.
- Failure before final apply discards the plan, rolls back staged lifecycle changes, and clears tick-local facts exactly once.
- Re-entering a continuation cannot repeat a reducer, committer decision, durable apply, or output publication.

### Platform constraints

- C# 8 and Unity 6.
- No runtime reflection.
- No new package dependency.
- No hot-path allocation after configured warmup.
- One class per file and explicit disposal.

## 5. ECS Parity Without Reintroducing ECS Semantics

| ECS capability | Cascade equivalent |
| --- | --- |
| Create entity | `CreateEntity()` returns a generational handle |
| Delete entity | `DestroyEntity()` emits `DeadFact`; reconciliation deletes durable state at closure |
| Find current entity by runtime id | `TryGetEntity(slotId, out entity)` |
| Add transient input/event | `Emit(entity, fact)` |
| Add or replace durable component | A committer returns a set decision for `IOutputState` |
| Remove durable component | Emit a domain removal fact; the committer returns a delete decision |
| Query components | `With<TState>()` and `With<TStateA, TStateB>()` over compact state membership |
| Query tick facts | `WithFact<TFact>()` and `IEntityFactView` |
| Read another entity in a reducer | `IEntityQuery` plus `ICommittedStateStore` |
| Entity churn | Recycled slot plus incremented generation |

There should not be a physical `RemoveFact<TFact>` operation during closure.

Removing an already accepted fact would make closure non-monotonic: a reducer could fire from a fact that another reducer later retracts, making results dependent on hidden execution order. ECS-style component removal maps to an additive removal request fact and a committer delete decision:

```text
RemoveShieldRequestedFact
  -> reducers derive consequences
  -> ShieldCommitter returns Delete
  -> ShieldState removal is published once
```

If true cancellation/retraction of accepted tick facts is required, that is a separate semantic proposal, not a storage refactor. It would require dependency tracking or an explicit conflict/cancellation fact model.

## 6. Target Internal Shape

```text
FactSimulation
|
+-- EntityStore
|   +-- generation/status/free-slot arrays
|   +-- compact live SparseEntitySet
|   +-- pending-created SparseEntitySet
|   +-- pending-destroyed SparseEntitySet
|
+-- FactStore
|   +-- touched SparseEntitySet
|   +-- one SparseFactSlab<TFact> per registered fact type
|   +-- warmed per-slot tick counters and route lists
|   +-- reusable QueuedFact queue
|
+-- output registrations
|   +-- StateBucket<TState>
|       +-- EntitySparseSet<TState>
|       +-- mutation journal
|       +-- prepared reconciliation actions
|
+-- ReductionCursor
|   +-- phase/pass
|   +-- pending fact/reducer
|   +-- transactional entity/registration
|   +-- batch registration
|   +-- state registration/entity
|
+-- ReconciliationPlan
    +-- typed prepared actions per output
    +-- lifecycle deletions
    +-- plan/apply/publication state
```

The intended storage primitives after cleanup are:

1. `EntityStore`: entity allocation and lifecycle authority.
2. `EntitySparseSet<T>`: one durable typed value per entity.
3. `SparseEntitySet`: compact unique entity membership.
4. `SparseFactSlab<T>`: multiple contiguous tick facts per entity.
5. `EntityRefBuffer`: non-owning reusable scratch/result buffer.

Queues, mutation journals, and reconciliation plans are stage-owned buffers, not general entity stores.

## 7. Incremental Refactoring Sequence

### Slice 2 — Durable State Sparse-Set Vertical Slice

#### Goal

Make one durable output type use a clear sparse-set primitive without changing `StateBucket<TState>`'s public or registration-facing behavior.

This is the first post-lifecycle implementation slice because durable state is the smallest complete path:

```text
fact -> reducer -> committer -> sparse durable state -> typed mutation
```

#### Changes

1. Add internal `EntitySparseSet<TValue>`.
2. Give it exactly these responsibilities:
   - sparse slot-to-dense mapping;
   - dense `EntityRef` owners;
   - dense typed values;
   - O(1) `Has`, `TryGet`, insert/replace, and remove;
   - compact `EntityAt` iteration;
   - swap-back removal;
   - capacity and terminal clear/dispose mechanics.
3. Require full owner equality after every sparse lookup.
4. Move state membership/value arrays out of `StateBucket<TState>` into the sparse set.
5. Keep mutation recording, state equality policy, output naming, and output-state disposal in `StateBucket<TState>`.
6. Preserve fixed warmed capacity for the settings-backed path and legacy growth where currently supported.

#### Tests first

- insert, replace, missing lookup, and swap-back delete;
- remove first/middle/last dense entry;
- reuse one slot with a higher generation and prove old state is invisible;
- `With<TState>()` iterates only dense members;
- `With<TStateA, TStateB>()` still iterates the smaller set;
- create/update/delete mutation payloads remain identical;
- warmed 512-entity set/lookup/delete path allocates zero bytes.

#### Exit gate

- Hestia ammo creation, update, deletion, and mutation tests pass unchanged.
- `StateBucket<TState>` no longer owns sparse/dense arrays directly.
- No new public type.
- No general-purpose abstraction beyond the operations used by durable state.

### Slice 3 — Sparse Entity Membership and Compact Live Iteration

#### Goal

Replace `DenseEntitySet` with one generation-safe `SparseEntitySet` and make entity membership use the same sparse-to-dense invariant as output state.

#### Changes

1. Add `SparseEntitySet` backed by:
   - `int[] sparse`;
   - `EntityRef[] dense`;
   - `int count`.
2. Validate the full dense owner for `Contains`.
3. Clear only dense members and repair their sparse entries.
4. Replace:
   - `EntityStore._pendingCreated`;
   - `EntityStore._pendingDestroyed`;
   - `FactStore._touchedEntities`.
5. Add compact live membership inside `EntityStore`.
   - Creation adds immediately.
   - Pending destruction remains in live membership through closure.
   - Committed destruction removes with swap-back before the slot becomes reusable.
   - Failed destruction keeps the live member.
   - Failed tick creation removes the member before generation advancement/reuse.
6. Keep `TryGetEntity` as direct slot/generation lookup. Do not implement it by scanning the live dense array.
7. Expose live iteration internally only. A new public `AllEntities()` API is out of scope until a production reducer or host actually needs state-independent enumeration.

#### Tests first

- add/contains/add-duplicate/clear;
- swap-back removal and sparse repair;
- same slot with two generations never aliases membership;
- pending-dead entity remains query-visible until closure;
- failed tick restores destroyed live membership;
- failed tick removes newly created live membership;
- repeated churn keeps sparse and dense capacities constant.

#### Exit gate

- `DenseEntitySet` has no consumers and is deleted.
- EntityStore can enumerate live entities without scanning slot capacity.
- No slot is returned to the free list while present in any lifecycle membership set.

### Slice 4 — Consolidate Tick Scratch Ownership in FactStore

#### Goal

Remove generic wrappers that obscure tick ownership and make `FactStore` the single owner of fact-related per-slot scratch.

#### Changes

1. Replace `DenseEntityCounter` with a warmed `int[] factCountsBySlot` owned by `FactStore`.
2. Replace `DenseEntityObjectStore<EntityFactRouteList>` with a warmed per-slot route-list array owned by `FactStore`.
3. Allocate every route list during construction/warmup for the configured production capacity.
4. Allow legacy growth only from the existing grow-on-demand path.
5. Clear counters and route lists by iterating `FactStore`'s touched sparse set.
6. Document that route-list objects are scratch resources attached to a slot and deliberately reused across entity generations only after the previous tick clears.
7. Keep `EntityRefBuffer` because query/transaction/batch results have different lifetimes and cursors. Rename it only if the rename makes ownership clearer and does not leak publicly.

#### Tests first

- first production tick does not lazily create route-list objects;
- clear resets only touched slots;
- a reused slot sees empty counters/routes;
- an incomplete tick retains its counters/routes until completion;
- failure clears exactly once;
- capacity snapshot remains stable under churn.

#### Exit gate

- `DenseEntityCounter` and `DenseEntityObjectStore<T>` are deleted.
- All fact scratch capacity changes happen through one `FactStore.EnsureCapacity` path.
- The settings-backed path cannot invoke a scratch factory during reduction.

### Slice 5 — Harden Sparse Fact Slabs and Bind Typed Routes

#### Goal

Keep the correct one-to-many fact shape, but make ownership, registration, and capacity explicit and remove lazy store resolution from reduction.

#### Changes

1. Rename/refactor `FactBucket<TFact>` to make its actual role explicit: a sparse multi-value slab, not a normal sparse set.
2. Preserve the storage shape:

   ```text
   sparse slot -> dense slab + 1
   dense slab -> complete EntityRef owner
   dense slab -> fact count
   dense slab * factsPerEntity -> contiguous TFact payload slice
   ```

3. Require complete owner equality on every lookup.
4. Create all registered fact slabs during simulation initialization.
5. Bind each `FactEmitRoute<TFact>` to its simulation-owned typed slab during initialization.
6. Bind transactional required-fact registrations to bucket references rather than resolving every required fact id through a dictionary for every entity.
7. Keep type dictionaries only as cold registration/diagnostic catalogs.
8. In the settings-backed path:
   - unknown/unregistered fact types are setup errors;
   - fixed slab overflow throws before writing;
   - payload capacity uses checked arithmetic;
   - emit, dedupe, queue, query, and clear do not allocate.
9. Keep grow-on-demand slab repacking only for the documented legacy path.
10. Do not add physical fact deletion. Domain removal remains an additive fact followed by a durable delete decision.

#### Tests first

- identical fact dedupe and distinct fact preservation;
- contiguous `All<TFact>()` spans before and after legacy growth;
- fixed overflow leaves counts, payload, queue, and touched membership unchanged;
- destroyed/stale generation rejection;
- slot reuse after closure does not expose prior facts;
- accepted facts dispose exactly once on completion, failure, and terminal dispose;
- every registered production fact bucket exists before the first emit;
- warmed 500+ entity fact closure allocates zero bytes.

#### Exit gate

- No fact bucket is created during a settings-backed tick.
- No per-entity dictionary or list exists in fact storage.
- FactStore dictionary access is limited to cold setup, diagnostics, or legacy compatibility paths.

### Slice 6 — Explicit Resumable Reduction Stage Machine

#### Goal

Make every reduction stage resume from an exact cursor instead of rescanning previously considered entities and relying on fired trackers to suppress duplicate work.

#### Current risk

Immediate fact dispatch and state reducers already preserve useful cursors. Transactional and batch stages currently restart their loops after a budget suspension.

That is more than wasted CPU. A time budget can be consumed rescanning already-fired entries before the cursor reaches new work, producing repeated incomplete steps with little or no forward progress.

#### Changes

1. Introduce an internal `ReductionPhase`:
   - immediate fact dispatch;
   - entity transactional reducers;
   - batch transactional reducers;
   - committed-state reducers;
   - closure check;
   - reconciliation planning;
   - final apply/publication;
   - complete.
2. Group stage state into explicit value-type cursors rather than unrelated fields:
   - pending fact and next reducer;
   - touched entity and transactional registration;
   - batch registration;
   - state registration and dense entity;
   - closure pass.
3. Advance a cursor only after its reducer invocation returns.
4. Preserve the current atomic work-unit definitions:
   - one immediate reducer invocation;
   - one entity transactional invocation;
   - one batch reducer invocation for its prepared span;
   - one state reducer invocation.
5. A batch reducer remains atomic. Do not pretend a user reducer can be safely preempted.
6. If a reducer emits a new fact, return to immediate dispatch at the correct closure boundary.
7. Reset transactional/batch cursors only for a new closure pass, not for a new frame step.
8. Retain fired trackers as correctness guards until cursor tests prove they are redundant. Remove them only in a later cleanup if doing so simplifies the design.
9. Require every incremental call to either:
   - complete;
   - execute at least one new work unit; or
   - return a precise zero-progress reason such as a time budget already exhausted before entry.

#### Tests first

- `MaxWorkItems = 1` across multiple immediate reducers;
- multiple entities and multiple transactional registrations resume without rescan/repeat;
- batch registrations resume at the next registration;
- state reducers resume at the exact dense entity;
- reducer-side creation/destruction across stage suspension;
- newly emitted facts restart closure without repeating completed work;
- cumulative tick work guardrail still spans all continuation calls;
- incomplete results publish no mutations and preserve old committed state.

#### Exit gate

- No stage depends on a full rescan to find its next work item.
- Diagnostics report the actual pending entity/fact/reducer at every suspension.
- Existing incremental public API remains unchanged.

### Slice 7 — Incremental Reconciliation Plan and Idempotent Apply

#### Goal

Turn commit from a monolithic method call into an explicit transaction:

```text
closed facts
  -> incrementally plan all output decisions
  -> validate complete plan
  -> atomically apply durable changes
  -> publish typed mutations once
  -> release destroyed entity slots
```

#### Current risk

Committers read one unchanged snapshot before queued writes are applied, which is correct. However:

- planning all entities/outputs is not resumable;
- lifecycle deletion is a separate loop after normal output commit;
- durable actions are applied output by output;
- a managed failure during application could occur after an earlier output was already changed;
- mutation publication and lifecycle slot release are not represented as one explicit transaction state.

#### Changes

1. Add a per-tick `ReconciliationPlan`.
2. Planning:
   - iterates touched entities and affected outputs using resumable cursors;
   - invokes committers only while reading the old committed snapshot;
   - resolves priority/conflict policy;
   - computes state equality/no-op decisions;
   - includes lifecycle deletions for pending-dead entities;
   - verifies all dense/sparse/mutation capacities;
   - records typed prepared actions without durable writes.
3. Validation:
   - every action targets the same tick and full entity generation;
   - no output/entity pair has two final actions;
   - all required capacity is already available;
   - no user code remains to be called during apply.
4. Apply:
   - performs only prevalidated array/index writes;
   - records enough previous raw storage information to reverse already-applied actions if an unexpected managed exception occurs;
   - does not call committer code, equality code, factories, dictionary insertion, or capacity growth.
5. Publication:
   - mutation journals become visible only after every durable apply succeeds;
   - lifecycle generations advance and slots return to the free list only after durable deletion succeeds;
   - the plan records `NotApplied`, `Applied`, and `Published` state so continuation/re-entry cannot double-apply.
6. Failure before apply discards the plan and rolls back entity lifecycle staging.
7. Failure during apply restores prior raw state in reverse order, discards unpublished mutations, rolls back lifecycle staging, and reports an actionable reconciliation diagnostic.
8. Clear facts only after publication and lifecycle finalization.

#### Budget model

Reconciliation planning must honor `MaxMilliseconds` and resume by cursor.

The final durable apply is an atomic tail. It cannot be safely exposed half-applied between incremental frames while committed state remains publicly readable. Initially, bound it by:

- `MaxEntities`;
- registered output count;
- prepared action capacity;
- measured worst-case action count.

Do not introduce double-buffered output state speculatively. If profiling proves the atomic apply tail exceeds the frame budget, the next design decision is explicit:

- double-buffer each state bucket and swap after incremental writes; or
- add a host-visible read barrier during apply.

The first option doubles relevant durable storage. The second changes the public availability contract. Neither should be smuggled into a storage cleanup.

#### Tests first

- all committers read the same previous snapshot regardless of output/entity order;
- conflict during planning leaves every output unchanged;
- committer exception during planning leaves every output unchanged;
- budget suspension in planning leaves every output unchanged;
- resume completes without repeating committer decisions;
- create/update/delete actions publish once;
- pending entity destruction and normal output actions finalize in one transaction;
- forced apply failure restores prior raw state and publishes nothing;
- repeated completion/failure cleanup disposes tick facts exactly once;
- stale handles cannot target a planned action after lifecycle finalization.

#### Exit gate

- `CompleteTick` cannot partially publish.
- Durable state, mutations, and entity slot release share one transaction boundary.
- Commit planning can span frames without consumers seeing staged state.
- Final apply contains no user callbacks and no expected managed failure points.

### Slice 8 — Make Capacity and Allocation Policy Enforceable

#### Goal

Make "no allocation in the hot path" an enforced construction-time policy rather than a warmup convention spread across stores.

#### Changes

1. Derive one internal capacity plan from `CascadeSettings`.
2. Allocate during construction:
   - entity sparse/dense membership;
   - every registered state sparse set and mutation journal;
   - every registered fact slab;
   - fact queue;
   - route lists;
   - touched/transaction/batch/query buffers;
   - reconciliation action buffers.
3. Pass an internal fixed/grow policy to each owning store.
4. Fixed stores throw a store-specific capacity exception before any write.
5. Use checked multiplication for:
   - entities × facts per type;
   - entities × affected outputs;
   - mutation/action capacity.
6. Make `CaptureCapacitySnapshot` report the actual new store families rather than legacy helper names.
7. Keep `Warmup(WarmupCapacityHints)` source-compatible for the legacy constructor.
8. Add diagnostics identifying:
   - store;
   - required capacity;
   - configured capacity;
   - tick and entity;
   - whether the path was fixed or grow-on-demand.

#### Verification

- synthetic churn reuses slots for millions of create/destroy cycles without capacity growth;
- 500+ entities with worst-case declared facts;
- many output add/remove operations;
- incremental suspension across many frames;
- zero bytes allocated for warmed emit/reduce/plan/apply/publish;
- no capacity changes after the first settings-backed tick;
- disposal releases all arrays, lists, plans, and accepted facts exactly once.

The churn test should simulate the reported "30 minutes" failure shape with a high iteration count. It should not literally sleep for 30 minutes.

#### Exit gate

- Production allocation tests cover the complete pipeline, not only reduction.
- Every possible growth site is either initialization/legacy-only or fails before mutation.
- Capacity diagnostics point to a concrete settings correction.

### Slice 9 — Remove Obsolete Storage Clutter and Freeze the Package Boundary

#### Goal

Delete superseded helpers, make ownership obvious from the folder structure, and leave one coherent internal model.

#### Changes

1. Delete old helpers only after their last consumer migrates:
   - `DenseEntitySet`;
   - `DenseEntityCounter`;
   - `DenseEntityObjectStore<T>`;
   - old `FactBucket<T>` implementation if renamed/replaced.
2. Keep `EntityRefBuffer` or its clearer internal rename if it remains the correct stage buffer.
3. Keep type-erased registration interfaces only where cold registration/disposal genuinely requires them.
4. Ensure dictionaries are registration/type-routing structures, not per-entity storage.
5. Update:
   - package README;
   - proposal;
   - progress memory;
   - storage/lifecycle diagrams;
   - Hestia entity creation/deletion and state removal examples.
6. Add a concise internal ownership document:
   - creator;
   - warmer;
   - writer;
   - reader;
   - clearer;
   - disposer;
   - lifetime.
7. Audit every public type. Do not expose internal sparse sets, capacity plans, stage cursors, or reconciliation actions.

#### Exit gate

- Each remaining storage class has one distinct access shape and owner.
- No duplicate sparse membership convention remains.
- Public API diff is empty unless a separately approved production requirement justified an addition.
- Package folder can still be copied as the drop-in boundary.

## 8. Slice Ordering and Dependency Rules

The order is intentional:

```text
Slice 1 generational lifecycle [done]
  -> Slice 2 durable sparse set
  -> Slice 3 entity membership
  -> Slice 4 fact scratch ownership
  -> Slice 5 sparse fact slabs/routing
  -> Slice 6 resumable reduction cursors
  -> Slice 7 reconciliation transaction
  -> Slice 8 fixed capacity proof
  -> Slice 9 cleanup/package freeze
```

- Do not start cursor/reconciliation restructuring while storage membership can still change underneath it.
- Do not delete old helpers in the same slice that introduces the first replacement consumer. Prove the replacement first.
- Do not combine output sparse-set migration with fact-slab migration. Their cardinality and lifetime semantics differ.
- Do not add double-buffered state before measuring the atomic apply tail.
- Do not broaden the public API to make an internal migration easier.

Each slice must be independently releasable and must end with:

1. Rider/MSBuild compilation.
2. Empty Unity compile-error report.
3. Focused tests for the changed invariant.
4. Existing EditMode suite when Unity licensing is available.
5. Allocation measurement for every changed warmed hot path.
6. `git diff --check`.
7. README/proposal changes only when externally observable semantics changed.

## 9. Decision Gates

### After Slice 2

Compare the sparse primitive against the previous `StateBucket<TState>`:

- fewer duplicated membership lines;
- equivalent or better lookup/iteration;
- no extra allocations;
- clearer mutation ownership.

If it makes `StateBucket<TState>` harder to review, simplify it before using it elsewhere.

### After Slice 5

Profile:

- emit lookup;
- fact dedupe;
- required-fact eligibility;
- clear/disposal;
- memory per registered fact type.

Do not introduce archetypes, chunk storage, or a generic query planner unless this profile shows the simple sparse model is insufficient.

### After Slice 7

Measure the atomic apply tail at the declared worst case. Only then decide whether double-buffered output state is justified.

### Before Any New Public API

Require:

- a concrete Hestia/production call site;
- an example that cannot be expressed with existing primitives;
- allocation and continuation semantics;
- a public-contract migration plan.

## 10. Assumptions That Must Stay Visible

1. Host and reducer reads during an incomplete tick continue to see the previous committed state.
2. Accepted facts are not retractable. Removal is modeled as another fact and reconciled durable deletion.
3. Batch reducer invocation is atomic and cannot be frame-sliced internally by the engine.
4. `TryGetEntity(int)` resolves the current generation for a runtime slot; it is not persistence identity.
5. The settings-backed path may reject underestimated capacity rather than allocate.
6. Legacy grow-on-demand behavior remains supported but is not the production performance contract.
7. Full owner validation is retained in release builds even if Bevy omits some generation checks outside debug builds.

If any of these assumptions changes, stop and revise the affected slice before implementation.

## 11. Definition of Done

The post-Slice-1 overhaul is complete when:

- entity churn is bounded by concurrent capacity;
- durable state is stored in one generation-safe sparse-set implementation;
- touched/pending/live membership uses one generation-safe sparse-set implementation;
- facts use one explicit sparse multi-value slab implementation;
- old dense helper clutter is gone;
- no settings-backed tick lazily creates or grows a store;
- all reduction stages resume from exact cursors;
- reconciliation planning can suspend without durable writes;
- durable apply, mutation publication, and entity slot release occur once at one transaction boundary;
- stale handles cannot read, mutate, delete, or receive facts for a replacement entity;
- ECS parity is demonstrated through create/delete, current-id lookup, add/remove durable state via facts, and arbitrary cross-entity reducer queries;
- the public API remains small and source-compatible;
- warmed production tests report zero hot-path allocations.
