# CascadeEngine

## Overview

`CascadeEngine` is the drop-in package boundary for the Cascade Rule Engine.

The package replaces ECS component with reducer-loop and output state rigid pipeline:

```text
Input or events -> emit facts
  -> fact work queue
  -> reducers emit more facts
  -> reduction reaches closure
  -> committers project facts into durable output components
  -> typed output mutations are published
```

Core:

- reducers never write durable state.
- reducers only read committed state plus accumulated tick facts, then emit more facts.
- committers are the only code that writes `IOutputState`.

## The key rule

The commit stage is not optional glue. It is the reconciliation layer.

The concept:

```text
Facts are what happened or what was requested.
Reducers derive consequences.
Committers decide durable truth.
OutputState is the only thing consumers trust.
```

## Fact Lifetime Rule

Facts exist for one reduction loop. Anything expected to remain observable after closure must be committed state.

A continuous condition owned by an external domain is still a tick input, not a persistent fact. Emit it again at the start of each reduction loop while that external condition is true:

```csharp
if (weatherDomain.IsWet(entity))
{
    simulation.Emit(entity, new WetEnvironmentObservedFact());
}

simulation.RunTick(ReduceOptions.Default());
```

`WetEnvironmentObservedFact` participates in that loop's closure only. If later reducers, committers, UI, or old ECS consumers must observe wetness after closure, commit a durable output:

```text
WetEnvironmentObservedFact
-> reducers derive wet consequences
-> WetStatusCommitter writes WetStatusState
```

Do not keep facts alive with cleanup markers or reducer-owned lifetime conventions. That creates a second durable state source and makes unordered reduction semantics harder to reason about.

Committed Cascade output state is different from an external condition. A persistent output such as `ActiveState` can be registered as a per-tick reducer trigger without creating a synthetic public fact:

```csharp
public sealed class NavigationFeature : FactFeature
{
    public NavigationFeature()
    {
        ReduceState<ActiveState, NavigationReducer>();

        Reduce<MoveCandidateFact>()
            .With<CollisionReducer>();
    }
}

public sealed class NavigationReducer : ITransactionalReducer
{
    public void Reduce(IReduceContext context, EntityRef entity)
    {
        ActiveState active = context.GetState<ActiveState>(entity);

        if (!context.TryGetState<BotState>(entity, out BotState bot))
            return;

        context.Emit(entity, new MoveCandidateFact(bot.Position, bot.Target));
    }
}
```

State-trigger rules:

- `ReduceState<TState, TReducer>()` runs the reducer once per tick for each live entity containing committed `TState`.
- State membership is the committed snapshot for the entire open tick. A state created during tick N becomes eligible in tick N+1.
- Same-tick activation consequences must still be emitted as facts by the activation reducer.
- Trigger on the narrowest state, such as `ActiveState`, and query additional states inside the reducer. Do not create combinatorial state-registration overloads.
- State reducers reuse `ITransactionalReducer`; registration eligibility differs, but the entity-scoped reducer contract is identical.
- State reducers emit facts only. Committers remain the only durable-state writers.
- The trigger state must be registered as an output in the full feature tree. Missing output registration is a setup error.

## Closure-Safe `Without`

Fact absence is not safe to test during ordinary positive reduction. A later reducer can still emit the forbidden fact, and already emitted consequences cannot be retracted. Chain `Without<TFact>()` from the existing fact-reducer registration when that reducer requires closure-safe absence:

```csharp
Reduce<AmmoSpendRequestedFact>()
    .Without<DeadFact>()
    .With<HestiaAmmoSpendRequestReducer>();
```

This registration preserves the `IFactReducer<AmmoSpendRequestedFact>` contract. Each accepted distinct `AmmoSpendRequestedFact` is deferred until immediate, positive transactional, batch, and state-driven work reaches closure. The reducer then runs once for that trigger fact only when the entity has no `DeadFact`.

Multiple forbidden facts can be chained; every declared fact must be absent:

```csharp
Reduce<MoveRequestedFact>()
    .Without<DeadFact>()
    .Without<MoveBlockedFact>()
    .With<ResolveUnblockedMoveReducer>();
```

The scheduling boundary is explicit:

```text
Reduce<TFact>().With<TReducer>()
  -> immediate, once per accepted distinct TFact

Reduce<TFact>().Without<TForbidden>().With<TReducer>()
  -> terminal negative stratum, once per accepted distinct TFact
  -> only when no declared forbidden fact exists for that entity
```

Trigger and forbidden fact types used by a negative rule are sealed when this final stratum starts. A reducer running in or after that stratum cannot emit one of those condition facts; doing so fails the tick. This deliberately supports one safe terminal negative stratum, not opaque multi-stratum negation. Because reducers do not declare their emitted fact types, general negative dependency sorting would require a larger public rule-head contract.

Direct contradictions fail during feature construction:

```csharp
// Invalid: the same registration cannot trigger on and forbid MoveRequestedFact.
Reduce<MoveRequestedFact>()
    .Without<MoveRequestedFact>()
    .With<InvalidReducer>();
```

When incremental execution reaches negative evaluation, host input for that open tick is sealed. Calls to `Emit`, `CreateEntity`, or `DestroyEntity` between incremental steps throw until the tick reaches closure. Resume the open tick, then submit the new input for the next tick. This prevents a late root fact from invalidating already derived absence consequences.

`Without<TFact>()` is intentionally not exposed on `ReduceWhen`, `ReduceBatchWhen`, or `ReduceState`. Those contracts are entity-level eligibility rules with different invocation semantics. Committed-state membership can be queried safely because it is an immutable tick snapshot. Closure-safe fact absence must use an explicit trigger fact and the direct `Reduce<TFact>().Without<TForbiddenFact>()` contract.

Output reconciliation has its own `Without<TFact>()`:

```csharp
Output<VisibleState>("Visible")
    .AffectedBy<VisibleObservedFact>(0)
    .AffectedBy<VisibilityPulseFact>(0)
    .Without<VisibleObservedFact>()
    .CommitWith<VisibleCommitter>();
```

For output registration, `Without<TFact>()` is an additional reconciliation trigger over entities that held that output in the previous committed snapshot. When none of the declared facts exists in the final closed fact set, the committer runs and can return `Set`, `Delete`, or `Unchanged`. It is not a filter: normal `AffectedBy` facts still invoke the committer. One entity/output pair is planned once even if both affected-fact and absence routing select it.

## Add And Remove Semantics

`Emit` adds a transient fact. Accepted facts are immutable and are never physically removed from an open reduction loop. Removing an accepted fact would leave already derived consequences behind and make results depend on reducer order.

Model cancellation and removal additively:

```text
RemoveShieldRequestedFact
-> validation reducers
-> ShieldRemovalAcceptedFact
-> ShieldCommitter returns Delete()
-> one ShieldState delete mutation
```

Facts clear automatically when the tick completes or fails. Durable output removal is `CommitDecision<TState>.Delete()`. Entity removal remains `DestroyEntity` -> `DeadFact` -> closure -> typed state deletion mutations.

That is the closest ECS equivalent to React-style reconciliation:

```text
React event/action
  -> reducers/state derivation
  -> virtual result
  -> reconciliation
  -> dirty DOM update

Fact ECS
  -> reducers/fact derivation
  -> fact closure
  -> committers
  -> dirty output component update
```

## Entitas Parity Matrix

This comparison uses the core Entitas entity, component, group, index, collector, system, and feature concepts documented in the Entitas [Components](https://github.com/sschmid/Entitas/wiki/Components) and [Systems](https://github.com/sschmid/Entitas/wiki/Systems) guides. It measures production replacement capability, not API-name compatibility.

Status meanings:

- **Equivalent**: the production capability exists directly.
- **Pipeline equivalent**: the outcome exists through Cascade's fact -> closure -> commit contract rather than direct component mutation.
- **Limited**: a deliberately narrower API exists; the stated limit matters during migration.
- **Not provided**: no package primitive currently exists. Add one only from a measured production slice.
- **Intentionally unsupported**: providing the Entitas behavior would violate Cascade's deterministic reduction model.
- **Intentionally replaced**: Cascade solves the production requirement through a different scheduling model and must not preserve the Entitas behavior.

| Entitas capability | CascadeEngine equivalent | Status | Contract and migration consequence |
| --- | --- | --- | --- |
| `Context.CreateEntity()` | `FactSimulation.CreateEntity()` / `IReduceContext.CreateEntity()` | **Equivalent** | Returns a live generational `EntityRef`. Reducer-created entities can receive facts in the same open tick. |
| `Entity.Destroy()` | `DestroyEntity()` -> `DeadFact` -> closure -> state deletion mutations | **Pipeline equivalent** | Destruction is transactional. Committed state remains readable through closure; a failed tick rolls destruction back. |
| Entity identity with pooled reuse | `EntityRef.Value` plus `EntityRef.Generation` | **Equivalent** | Stale handles cannot alias a reused slot. Preserve the complete handle, not only `Value`. |
| Find current entity by runtime id | `FactSimulation.TryGetEntity(slotId, out entity)` | **Limited** | Resolves the current generation in a runtime slot. Slot ids are recyclable and are not save-game, network, or domain identity. |
| Stable or domain-key entity lookup | Domain-owned stable-key state plus a host-owned index | **Not provided** | There is no built-in stable-id index. The package also does not enforce uniqueness for a domain key. |
| Add or replace a durable component | Committer returns `CommitDecision<TState>.Set(next)` | **Pipeline equivalent** | Reducers cannot write state. All committers read the same previous snapshot, then buffered decisions are applied once. |
| Remove a durable component | Committer returns `CommitDecision<TState>.Delete()` | **Pipeline equivalent** | Deletion publishes one typed `StateMutation<TState>` when prior state exists. Repeated deletion is idempotent. |
| Marker component and `hasX` / `isX` | Empty `IOutputState<TState>` plus `Has<TState>()`; tick marker facts plus `Facts(entity).Has<TFact>()` | **Equivalent** | Use output state for durable membership and facts for tick-local membership. They are intentionally different lifetimes. |
| Direct component read | `GetState<TState>()`, `TryGetState<TState>()`, `Facts(entity).TryGetLatest<TFact>()`, and `All<TFact>()` | **Equivalent** | Reducers and committers receive query-only views. Missing durable state remains explicit. |
| Request/event component added for one frame | `Emit(entity, fact)`; tick storage clears after closure or failure | **Pipeline equivalent** | No cleanup system is required. Identical payloads deduplicate; distinct same-type payloads remain available through `All<TFact>()`. |
| Remove an accepted request component during system execution | Add a cancellation/rejection fact and reconcile the durable result | **Intentionally unsupported** | Physical fact removal would leave already-derived consequences behind and make results depend on reducer order. |
| `AllOf` group over one state | `Query.With<TState>()` | **Equivalent** | Returns an allocation-free `EntityQueryResult` over an engine-owned reusable buffer. |
| `AllOf` group over two states | `Query.With<TStateA, TStateB>()` | **Equivalent** | Iterates the smaller sparse state set and checks the other membership. Consume the result before another query reuses the buffer. |
| Arbitrary-arity `AllOf` matcher | Trigger on one narrow state and query other requirements manually | **Limited** | No generic three-or-more-state matcher is exposed. Do not add combinatorial overloads without a measured hot-path use case. |
| `AnyOf` group matcher | None | **Not provided** | Reducers can issue separate supported queries, but the package has no union query primitive. |
| `NoneOf` / matcher-level exclusion | Closure-safe reducer `Without<TFact>()` only | **Limited** | Reducer `Without` is fact absence in the terminal negative stratum. It is not a general state/fact query matcher. |
| Group count and entity enumeration | `EntityQueryResult.Count`, indexer, and `AsSpan()` | **Equivalent** | Available for supported query shapes without `IEnumerable` allocation. |
| Persistent live `IGroup` with membership events | None | **Not provided** | Cascade evaluates supported sparse queries on demand and does not expose mutable group objects or group callbacks. |
| `[PrimaryEntityIndex]` one-to-one lookup | None | **Not provided** | `TryGetEntity` is only slot lookup; it is not a primary key index and does not validate domain-key uniqueness. |
| `[EntityIndex]` one-to-many lookup | None | **Not provided** | Cross-entity reducers can scan supported state queries. Add a typed package index only after a production profile proves scans are insufficient. |
| `[Unique]` context component | Project-owned singleton/service or explicitly bootstrapped entity | **Not provided** | Uniqueness is not enforced by the package. Missing required services remain setup errors rather than implicit global entities. |
| `ReactiveSystem` / `Collector` for changed components | Fact-triggered reducers during reduction; `ForEachMutation(output, handler)` after commit | **Pipeline equivalent** | Reducers react to accepted facts. Consumers receive final create/update/delete output mutations, not intermediate component churn. |
| Generated `[Event]` systems | Typed output mutation stream | **Limited** | There is no generated listener API or added/removed collector matrix. Consumers subscribe operationally by routing the required output after `RunTick`. |
| Ordered `IExecuteSystem` chain | Fact routing, transactional eligibility, closure, then reconciliation | **Intentionally replaced** | Reducer registration order is not a gameplay priority mechanism. Output conflict policy owns durable conflict resolution. |
| `IInitializeSystem`, `ICleanupSystem`, and `ITearDownSystem` | Host bootstrap, automatic fact clearing, and `FactSimulation.Dispose()` | **Pipeline equivalent** | Cascade does not reproduce system lifecycle interfaces. The host owns construction and ticking; the simulation owns terminal teardown. |
| Entitas `Feature` composition | `FactFeature` plus `SubFeature(...)` | **Equivalent** | Sub-feature registration ownership transfers to the parent. Features organize routes; they do not establish execution order. |
| Multiple generated contexts and multi-context reactive systems | Host-owned coordination across separate simulations/adapters | **Not provided** | One simulation has one entity/state world. Cross-simulation transactions and aggregate queries are host responsibilities. |
| Generated component, matcher, event, and index APIs | Generic registration and typed runtime routes | **Limited** | Cascade has no code-generation layer. Runtime reflection is banned; only cold-path `typeof`-based registration metadata is used. |
| Runtime component enumeration and editor visual-debugging APIs | Focused typed queries plus diagnostics in `SimulationResult` | **Not provided** | The package does not expose arbitrary state/fact object enumeration, boxed component arrays, or an Entitas-style entity inspector. |

### Parity verdict

CascadeEngine has the ECS replacement slice required by the documented migration boundary:

```text
old ECS/input/event
-> entity-owned facts
-> same-tick reduction closure
-> reconciled durable output state
-> typed mutation consumed by ECS/view/UI adapters
```

It does **not** have full Entitas API parity. The material missing facilities are stable domain-key indexes, one-to-many indexes, richer matcher shapes, persistent live groups, generated listeners, and multi-context aggregation. These are not implied future work. Each requires a thin production vertical slice, measured cost, and a public-contract review before entering the package.

Physical removal of accepted facts and ordered mutable-system execution are not parity gaps. They are explicitly excluded because they reintroduce partial consequences and hidden temporal ordering—the production failures CascadeEngine exists to remove.

## Minimal Host Flow

```csharp
var feature = new GameplayFeature();
var settings = new CascadeSettings(
    maxEntities: 1024,
    maxFactsPerEntity: 32,
    maxFactsPerTypePerEntity: 4)
{
    MaxWorkItemsPerStep = 50_000,
    MaxWorkItemsPerTick = 100_000,
    MaxPasses = 64,
    MaxMillisecondsPerStep = 8,
    MaxCausalDepth = 32
};
using var simulation = new FactSimulation(feature, settings);
var entity = simulation.CreateEntity();

simulation.Emit(entity, new MoveRequestedFact(12f));

SimulationResult result = simulation.RunTick();

simulation.ForEachMutation(feature.Position, OnPositionChanged);
```

## Incremental Host Flow

`RunTick` keeps the original full-closure contract. `RunTickIncremental` runs one reduction pass on `FactSimulation` and returns `true` only when the tick closes and commit has been applied.

```csharp
while (!simulation.RunTickIncremental(out SimulationResult result))
{
    // No durable output state has been committed yet.
    // Yield to the host frame loop, then continue the same open tick.
}

simulation.ForEachMutation(feature.Position, OnPositionChanged);
```

Incomplete incremental results are diagnostic only. Consumers must keep trusting committed `IOutputState`; commit still happens only after reduction closure.

The parameterless tick methods use the immutable settings snapshot captured by the simulation constructor. Existing overloads accepting `ReduceOptions` remain available for diagnostics and exceptional host-controlled overrides.

`ReduceOptions.MaxFacts` bounds dequeued facts per incremental call. `ReduceOptions.MaxWorkItems` separately bounds reducer invocations per call: immediate reducer invocation, entity transactional/state invocation, or one atomic batch reducer invocation. `MaxMilliseconds` remains the hard elapsed-time slice. Budget suspensions do not consume `MaxPasses`; only completed logical closure passes do.

If a time or work budget stops dispatch between two reducers registered for the same fact, the simulation preserves the popped fact and next reducer index. Continuation never drops or repeats the remaining reducer invocations.

## Transactional Entity Lifecycle

Entity creation and destruction requested while a tick is open are part of that tick:

- A newly created entity is immediately usable by reducers and can receive facts in the same tick.
- `DestroyEntity(entity)` emits the built-in `DeadFact`; directly emitting `DeadFact` has the same meaning.
- Pending-dead entities remain reduction-visible through closure. Already queued facts, facts emitted after `DeadFact`, transactional reducers, state reducers, and reducer queries continue to include them.
- `IsDestroyed` reports pending death immediately, while committed output remains readable until closure. `TryGetEntity` rejects pending-dead slots.
- Reducers that should stop domain work must make that rule explicit with `context.Facts(entity).Has<DeadFact>()`. Cleanup reducers can register directly with `Reduce<DeadFact>()`.
- Normal output projection is skipped for pending-dead entities. Closing the tick deletes all their output states and publishes one final typed delete mutation per existing output state.
- A failed full tick rolls back destruction. Handles created by the failed tick are invalidated; their slots can be reused with a higher generation after rollback.
- `SetStateSilently` is bootstrap/load authority only and throws while a tick is open because it would invalidate committed snapshot membership.

Host-side destruction is also transactional: call `RunTick` or continue the open incremental tick to reach closure and publish deletion mutations. Facts emitted after closure through the stale handle are rejected.

`EntityRef.Value` is a recyclable runtime slot id, not a persistent identity. `EntityRef.Generation` changes before that slot is reused, and equality includes both fields. Preserve the complete handle for transient runtime references. Use a domain-owned stable key for save data, network identity, or references that must survive entity destruction.

`TryGetEntity` resolves the current live generation for a runtime slot. It is useful for current-world `FindById` lookup, but an old integer slot id can resolve to a different entity after reuse:

```csharp
if (simulation.TryGetEntity(currentRuntimeSlot, out EntityRef entity))
{
    simulation.Emit(entity, new SelectedFact());
}
```

`TryGetEntity` is intentionally on concrete `FactSimulation`; adding it to `IFactSimulation` would break existing adapter implementations.

## Stable Type Ids

Fact and output ids are derived during feature registration from the CLR type name. Do not add static ids to fact or output structs. The feature registry owns the initialized name-to-id catalog and validates duplicates before a simulation can use it.

```csharp
public readonly struct MoveRequestedFact : IFact<MoveRequestedFact>
{
    public MoveRequestedFact(float distance)
    {
        Distance = distance;
    }

    public float Distance { get; }

    public bool Equals(MoveRequestedFact other)
        => Distance.Equals(other.Distance);
}

public readonly struct PositionState : IOutputState<PositionState>
{
    public PositionState(float value)
    {
        Value = value;
    }

    public float Value { get; }

    public bool Equals(PositionState other)
        => Value.Equals(other.Value);
}
```

Use the self-typed `IFact<TFact>` and `IOutputState<TState>` contracts for normal package values. They require only typed equality, which keeps deduplication and state change detection allocation-free. The engine does not hash payloads, so `Equals(object)` and `GetHashCode()` are not required. `IFact` supplies no-op disposal; only resource-owning facts implement `Dispose()` explicitly. The non-generic interfaces remain supported for existing code.

Type names must be unique inside one full feature registration, including sub-features. Duplicate names or int-id collisions fail during registration. There is no id-to-type diagnostics map; routing maps use `CascadeTypeId`.

## Warmup For 500+ Entities

`CascadeSettings` is the recommended production setup surface. Constructing `FactSimulation` with settings validates hard limits and warms every runtime buffer automatically:

The three cardinality limits are mandatory constructor arguments. The package deliberately has no guessed defaults for them.

- `MaxEntities` is the maximum number of concurrent live or pending entities, not a lifetime creation limit.
- Entity slots are recycled only after committed destruction or failed-tick rollback cleanup. Reuse increments the slot generation, so stale `EntityRef` values cannot alias the new entity.
- Generation exhaustion retires that individual slot instead of wrapping and accepting a stale handle.
- `MaxFactsPerEntity` and `MaxFactsPerTypePerEntity` are hard per-tick limits and exact warm capacities.
- The settings-backed path uses fixed typed-slab capacity. Underestimation throws instead of allocating during reduction.
- `MaxWorkItemsPerStep` controls incremental frame slicing. `MaxWorkItemsPerTick` is the cumulative closure guardrail across all continuation steps.
- Mutating the settings object after construction does not reconfigure an existing simulation.

The older `Warmup(WarmupCapacityHints)` and `RunTick(ReduceOptions)` APIs remain source-compatible for adapters and exploratory grow-on-demand workflows. Do not combine them accidentally with the settings-backed path; project production code should own one `CascadeSettings` instance and construct the simulation once.

Warmup is a capacity phase only. It does not create entities, emit facts, run reducers, commit output state, or publish mutations.

```csharp
var feature = new GameplayFeature();
var simulation = new FactSimulation(feature);

const int expectedEntities = 512;
simulation.Warmup(new WarmupCapacityHints
{
    EntityCapacity = expectedEntities,
    FactQueueCapacity = expectedEntities * 4,
    FactsPerEntityPerTypeCapacity = 4,
    QueryEntityCapacity = expectedEntities,
    TransactionEntityCapacity = expectedEntities,
    BatchEntityCapacity = expectedEntities,
    CommitActionCapacity = expectedEntities,
    OutputStateCapacityPerOutput = expectedEntities,
    MutationCapacityPerOutput = expectedEntities,
    FactListCapacityMode = FactListCapacityMode.Fixed
});

for (var i = 0; i < expectedEntities; i++)
{
    EntityRef entity = simulation.CreateEntity();
    // Bootstrap committed output state here when the domain requires it.
}
```

Keep the hints honest. If one gameplay tick can enqueue two input facts and two derived facts per entity, size `FactQueueCapacity` for that shape instead of assuming entity count is enough.

Warmup pre-creates buckets for fact types known from feature registration: reducer triggers, transactional requirements, batch transactional requirements, and output affected-fact declarations. Facts emitted only from reducer code still need a declaration in the feature, usually as an affected fact for the output that consumes them.

Each fact type owns one flat typed payload slab, one sparse entity-slot-to-slab map, one compact slab-owner array, and one count array. Touched entities receive contiguous fixed-width slices on demand; untouched entities have no active slice and no per-entity list object. `IEntityFactView.All<TFact>()` returns a zero-copy `ReadOnlySpan<TFact>` over the entity's active slice.

The settings-backed path reserves the worst-case payload size `MaxEntities * MaxFactsPerTypePerEntity` for each registered fact type. That reservation is required to guarantee no allocation when every entity can emit that type. `GrowOnDemand` may repack one fact type's slab and allocate; it remains a legacy/prototyping policy only.

Output state uses sparse-set storage: entity membership and values are compact and directly iterable. Single-state queries iterate only entities containing that state; two-state queries iterate the smaller state bucket and test membership in the other. Warm `OutputStateCapacityPerOutput` for both expected entity ids and state membership so state-trigger and query hot paths do not resize.

Use `FactListCapacityMode.Fixed` for gameplay hot paths that must not allocate. In fixed mode, an underestimated `FactsPerEntityPerTypeCapacity` throws before writing past the entity's slab slice. Use the default `GrowOnDemand` only while prototyping or when the host explicitly accepts slab repacking and capacity growth. The enum retains its original name for public-contract compatibility.

## Dispose Ownership Rules

`FactSimulation` owns runtime data and the feature registry tree passed into it. `Dispose()` is terminal and idempotent: call it during scene unload, domain replacement, or editor-session cleanup. After disposal, public simulation APIs throw `ObjectDisposedException`.

Ownership rules:

- Tick-local facts are owned by the `FactStore` only after `Emit` accepts them. Accepted facts are disposed when tick-local storage clears after a tick, after a failed tick, or during `Dispose()`. `IFact` provides no-op disposal; resource-owning facts override it. Rejected or deduplicated facts are not owned by the simulation.
- Output state buckets are owned by the simulation. `Dispose()` clears every bucket and disposes current stored output states that implement `IDisposable`. Output states should still be immutable value snapshots; do not hide shared resource ownership in copied mutation payloads.
- Mutation buffers are simulation-owned last-result records. They do not own `Previous` or `Next` state payloads and are cleared without disposing those copies.
- `SubFeature` transfers registration ownership into the parent feature. The attached sub-feature is no longer a valid simulation root.
- `FactSimulation.Dispose()` disposes the bound root `FactFeature`, including attached sub-features. Reducer registrations, output registrations, reducer instances, and committer instances are disposed when they implement `IDisposable`, then registry maps are cleared. A disposed feature cannot be reused to construct another simulation.
- Future runtime pools or scratch buffers allocated by `FactSimulation`, its stores, or feature registration objects must be released from `Dispose()`.

Resource ownership lasts through the **entire open tick**, including incremental pauses, negative reduction, and commit planning. It ends only after publication, failed-tick rollback, or terminal disposal. A queue entry, reducer argument, fact view, or span borrows the stored payload; it is not a second owner. Committers must copy durable values out of disposable fact data. Storing the fact's resource in output state leaves that state pointing at a disposed resource after closure.

For example, a project can transfer one rented buffer into an accepted fact:

```csharp
public readonly struct BufferReceivedFact : IFact<BufferReceivedFact>
{
    public BufferReceivedFact(byte[] buffer, int count)
    {
        Buffer = buffer;
        Count = count;
    }

    public byte[] Buffer { get; }
    public int Count { get; }

    // Equality identifies this lease; different rented buffers must not deduplicate by contents.
    public bool Equals(BufferReceivedFact other) => ReferenceEquals(Buffer, other.Buffer);

    public void Dispose() => System.Buffers.ArrayPool<byte>.Shared.Return(Buffer);
}
```

Register the fact normally. The producer must stop using an accepted buffer and must not submit the same lease as independently owned facts on multiple entities. The unchanged `void Emit` contract does not report deduplication or retired-entity rejection; use lease identity for resource-fact equality and submit to a known live target. Failed admission leaves the proposed payload caller-owned. Resubmitting a copy of an already accepted lease does not create another owner.

Cleanup is exhaustive: a throwing disposer does not prevent other facts, state buckets, reducers, committers, or sub-features from being visited. Stored payload references are cleared before their disposal callback, and failed callbacks are not retried. One failure is rethrown with its original stack; multiple failures produce `AggregateException`. The original reducer/committer error is retained alongside any cleanup errors. Disposers should still finish their own resource cleanup before throwing; the engine cannot recover resources hidden inside a failed callback.

If disposal fails **after commit**, state and mutation records remain published and `LastResult` identifies that completed tick. This is a cleanup error, not a rolled-back tick; do not blindly replay its input. Terminal `Dispose()` remains terminal and idempotent even when it reports cleanup errors. Static routes and owned scratch storage are released, including when the feature was disposed before its simulation. Tick callbacks cannot recursively tick, dispose the simulation, or warm storage; fact disposers cannot submit new input.

Disposable **output states** retain their existing contract: terminal disposal visits only currently stored states. Replacement, deletion, and abandoned commit decisions do not automatically dispose copied payloads. Use immutable value snapshots and keep shared resource ownership in a domain owner until a separate output-resource lifetime contract is defined.

## Feature Registration

```csharp
public sealed class GameplayFeature : FactFeature
{
    public GameplayFeature()
    {
        Reduce<MoveRequestedFact>()
            .With<MoveRequestReducer>();

        Position = Output<PositionState>("Position")
            .AffectedBy<MoveResolvedFact>(priority: 100)
            .AffectedBy<TeleportResolvedFact>(priority: 1000)
            .ConflictPolicy(CommitConflictPolicy.PriorityWinnerOrThrowOnTie)
            .CommitWith<PositionCommitter>();
    }

    public OutputState<PositionState> Position { get; }
}
```

Fact reduction order is not a public scheduling policy. Facts are reduced until closure; durable conflict priority belongs to the output-to-fact registration and is consulted only after closure.

## Fact Multiplicity And Commit Conflict

The package supports multiple distinct facts of the same type on the same entity in one reduction loop. Identical payloads are deduplicated; distinct payloads are retained and exposed through `IEntityFactView.All<TFact>()`.

Do not overwrite same-type facts during emit. Overwrite semantics make fact arrival order durable again, which recreates the ECS hidden-order problem. If the output needs one winner, declare priorities on its affected facts. If the output needs all changes, use `FoldAll` and make the fold commutative.

`CommitConflictPolicy` is a registration-level declaration of the expected merge behavior:

- `PriorityWinnerOrThrowOnTie`: the commit phase selects the affected fact type with the highest registration priority. The committer sees only that type through `ICommitContext.Facts(entity)`. Multiple distinct facts at the winning priority throw before durable writes.
- `FoldAll`: the committer folds every relevant fact into one durable state write.
- `CollapseToSingleMarker`: the committer collapses one or more facts into one marker-style output.

`AffectedBy<TFact>()` is shorthand for priority `0`. Priority is scoped to one output registration: the same fact type may have different commit priority for different outputs. Priority never changes reducer scheduling or fact acceptance.

The builder defaults to `FoldAll` for backward-compatible pass-through behavior. Use `ConflictPolicy(...)` explicitly in production registrations so the merge contract is visible during review.

The engine cannot automatically merge arbitrary output state. Committers remain the final projection boundary, while the commit phase owns deterministic winner selection and tie rejection.

## Commit Snapshot Isolation

Every committer in one closing tick reads the same previous committed-state snapshot. The engine first evaluates and buffers every `CommitDecision` for every touched entity and output. Only after all decisions succeed does it apply durable writes.

Consequences:

- `previous` and `ICommitContext.GetState/TryGetState/HasState` observe pre-commit state.
- One committer cannot observe another committer's pending decision, including decisions for another entity.
- Output registration order and touched-entity order cannot change commit reads.
- If any committer or conflict check throws, no queued durable write is applied and no partial mutation output is published.

## Transactional Registration Arity

`ReduceWhen<TA, TB>()` and `ReduceBatchWhen<TA, TB>()` have generic overloads for two, three, and four required facts. All overloads are grouped in `FactFeature.TransactionalRegistration.cs`; adding another package-supported arity is a mechanical overload there.

For more than four facts, continue the declaration with the package-provided `And<TFact>()` builder extension. No feature inheritance or package overload is required:

```csharp
ReduceWhen<FactA, FactB, FactC, FactD>()
    .And<FactE>()
    .And<FactF>()
    .With<DomainReducer>();

ReduceBatchWhen<FactA, FactB, FactC, FactD>()
    .And<FactE>()
    .With<DomainBatchReducer>();
```

Each extension returns a new builder with one appended required fact. Its `FactType[]` allocation happens during feature registration and never enters the reduction hot path.

## Public API contract

| Type | Role |
| --- | --- |
| `EntityRef` | generational entity handle: `Value` is a recyclable runtime slot and `Generation` prevents stale-handle aliasing |
| `CascadeSettings` | single project-level hard-cap, warmup, and default reduction-budget configuration |
| `CascadeTypeId` | compact fact/output-state identity derived from feature registration |
| `CascadeReductionException` | reduction guardrail failure with budget reason, fact id/name, entity, causal depth, and reducer name |
| `IFact` | transient input or derived consequence for one tick; accepted facts are disposed when tick-local storage clears |
| `IFact<TFact>` | self-typed allocation-free fact equality with inherited no-op disposal |
| `DeadFact` | built-in additive lifecycle fact; reduction continues through closure before durable state deletion |
| `IFactReducer<TFact>` | fact-triggered reducer; emits facts only |
| `ReducerRegistrationBuilder<TFact>` | existing `Reduce<TFact>()` fluent contract; optionally declares closure-safe absence with `.Without<TForbiddenFact>()`, then binds `.With<TReducer>()` |
| `ITransactionalReducer` | entity-scoped reducer used by required-fact and committed-state eligibility registrations |
| `TransactionalReducerRegistrationExtensions` | appends required facts with `.And<TFact>()` |
| `IOutputState` | durable committed state consumers can trust |
| `IOutputState<TState>` | self-typed allocation-free durable-state equality |
| `IOutputCommitter<TState>` | folds closed facts into one durable state decision |
| `CommitConflictPolicy` | declared output merge policy used by feature registration and committer examples |
| `FactFeature` | registration hub for fact reducers, transactional reducers, state reducers, and outputs |
| `FactSimulation` | transactional entity lifecycle, current-slot lookup, fact queue, reduction, commit, mutation routing, terminal disposal |
| `ReduceOptions` | per-call fact, work-item, pass, and elapsed-time budgets |
| `WarmupCapacityHints` | host-provided capacity hints for pre-sizing simulation stores before gameplay ticks |
| `FactListCapacityMode` | legacy-named grow or fixed policy for per-entity typed-slab slices |
| `OutputState<TState>` | typed mutation stream descriptor |
| `StateMutation<TState>` | create/update/delete diff for one output state |
| `SimulationResultCounters` | numeric tick counters, including processed reducer work items, grouped away from result construction |
| `SimulationResultDiagnostics` | incomplete-tick and guardrail context grouped away from result construction |

## Package Boundary

Use `FactSimulation` as the concrete runtime entry point and lifecycle owner. Do not add a second public facade until there is a real host-facing capability to hide. `IFactSimulation` exists for adapters that only need entity lifecycle, fact emission, ticks, and mutation routing; concrete owners should dispose `FactSimulation` directly.

Folder intent:

- `Public`: public types normal package consumers directly uses.
- `Internal`: rest of the package with core interfaces, implementation, utilities. These are package implementation details and should be hidden from sample gameplay code.

## Hestia Sample

`Assets/HestiaGame` shows the thin vertical slice:

```text
AmmoSpendRequestedFact
-> HestiaAmmoSpendRequestReducer
-> AmmoSpendAcceptedFact
-> HestiaAmmoCommitter writes HestiaAmmoState once
-> HestiaAudioCueCommitter may publish a marker-style DryFire cue
```

Movement demonstrates same-type conflict handling:

```text
MoveRequestedFact
-> MoveResolvedFact
-> commit phase rejects multiple distinct resolutions before HestiaPositionCommitter writes state
```

`OutputStateRouteTests` demonstrates cross-type registration priority and order-independent winner selection. The tests under `Assets/Tests` are the executable API examples.
