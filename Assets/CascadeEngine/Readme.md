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

## Minimal Host Flow

```csharp
var feature = new GameplayFeature();
var simulation = new FactSimulation(feature);
var entity = simulation.CreateEntity();

simulation.Emit(entity, new MoveRequestedFact(12f));

SimulationResult result = simulation.RunTick(ReduceOptions.Default());

simulation.ForEachMutation(feature.Position, OnPositionChanged);
```

## Incremental Host Flow

`RunTick` keeps the original full-closure contract. `RunTickIncremental` runs one reduction pass on `FactSimulation` and returns `true` only when the tick closes and commit has been applied.

```csharp
while (!simulation.RunTickIncremental(options, out SimulationResult result))
{
    // No durable output state has been committed yet.
    // Yield to the host frame loop, then continue the same open tick.
}

simulation.ForEachMutation(feature.Position, OnPositionChanged);
```

Incomplete incremental results are diagnostic only. Consumers must keep trusting committed `IOutputState`; commit still happens only after reduction closure.

`ReduceOptions.MaxFacts` bounds dequeued facts per incremental call. `ReduceOptions.MaxWorkItems` separately bounds reducer invocations per call: immediate reducer invocation, entity transactional/state invocation, or one atomic batch reducer invocation. `MaxMilliseconds` remains the hard elapsed-time slice. Budget suspensions do not consume `MaxPasses`; only completed logical closure passes do.

If a time or work budget stops dispatch between two reducers registered for the same fact, the simulation preserves the popped fact and next reducer index. Continuation never drops or repeats the remaining reducer invocations.

## Transactional Entity Lifecycle

Entity creation and destruction requested while a tick is open are part of that tick:

- A newly created entity is immediately usable by reducers and can receive facts in the same tick.
- Reducer-side destruction tombstones the entity immediately for further dispatch, but its committed output state remains unchanged until closure.
- Closing the tick commits created entities and deletes all output states for destroyed entities, publishing typed delete mutations once.
- A failed full tick rolls back destruction. Entities created by the failed tick become permanently destroyed; ids are never reused.
- `SetStateSilently` is bootstrap/load authority only and throws while a tick is open because it would invalidate committed snapshot membership.

At host or persistence boundaries, validate stored integer ids without manufacturing unchecked handles:

```csharp
if (simulation.TryGetEntity(savedEntityId, out EntityRef entity))
{
    simulation.Emit(entity, new RestoreRequestedFact());
}
```

`TryGetEntity` is intentionally on concrete `FactSimulation`; adding it to `IFactSimulation` would break existing adapter implementations.

## Stable Type Ids

Fact and output ids are derived during feature registration from the CLR type name. Do not add static ids to fact or output structs. The feature registry owns the initialized name-to-id catalog and validates duplicates before a simulation can use it.

```csharp
public readonly struct MoveRequestedFact : IFact
{
    public void Dispose()
    {
    }
}

public readonly struct PositionState : IOutputState
{
}
```

Type names must be unique inside one full feature registration, including sub-features. Duplicate names or int-id collisions fail during registration. There is no id-to-type diagnostics map; routing maps use `CascadeTypeId`.

## Warmup For 500+ Entities

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

Output state uses sparse-set storage: entity membership and values are compact and directly iterable. Single-state queries iterate only entities containing that state; two-state queries iterate the smaller state bucket and test membership in the other. Warm `OutputStateCapacityPerOutput` for both expected entity ids and state membership so state-trigger and query hot paths do not resize.

Use `FactListCapacityMode.Fixed` for gameplay hot paths that must not allocate. In fixed mode, an underestimated `FactsPerEntityPerTypeCapacity` throws instead of silently resizing an `EntityFactList<TFact>`. Use the default `GrowOnDemand` only while prototyping or when the host explicitly accepts capacity growth.

## Dispose Ownership Rules

`FactSimulation` owns runtime data and the feature registry tree passed into it. `Dispose()` is terminal and idempotent: call it during scene unload, domain replacement, or editor-session cleanup. After disposal, public simulation APIs throw `ObjectDisposedException`.

Ownership rules:

- Tick-local facts are owned by the `FactStore` only after `Emit` accepts them. Accepted facts are disposed when tick-local storage clears after a tick, after a failed tick, or during `Dispose()`. Rejected or deduplicated facts are not owned by the simulation.
- Output state buckets are owned by the simulation. `Dispose()` clears every bucket and disposes current stored output states that implement `IDisposable`. Output states should still be immutable value snapshots; do not hide shared resource ownership in copied mutation payloads.
- Mutation buffers are simulation-owned last-result records. They do not own `Previous` or `Next` state payloads and are cleared without disposing those copies.
- `SubFeature` transfers registration ownership into the parent feature. The attached sub-feature is no longer a valid simulation root.
- `FactSimulation.Dispose()` disposes the bound root `FactFeature`, including attached sub-features. Reducer registrations, output registrations, reducer instances, and committer instances are disposed when they implement `IDisposable`, then registry maps are cleared. A disposed feature cannot be reused to construct another simulation.
- Future runtime pools or scratch buffers allocated by `FactSimulation`, its stores, or feature registration objects must be released from `Dispose()`.

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
| `EntityRef` | stable non-reused entity handle; validate external integer ids with `FactSimulation.TryGetEntity` |
| `CascadeTypeId` | compact fact/output-state identity derived from feature registration |
| `CascadeReductionException` | reduction guardrail failure with budget reason, fact id/name, entity, causal depth, and reducer name |
| `IFact` | transient input or derived consequence for one tick; accepted facts are disposed when tick-local storage clears |
| `IFactReducer<TFact>` | fact-triggered reducer; emits facts only |
| `ITransactionalReducer` | entity-scoped reducer used by required-fact and committed-state eligibility registrations |
| `TransactionalReducerRegistrationExtensions` | appends required fact types with `.And<TFact>()` for entity or batch transactional registration |
| `IOutputState` | durable committed state consumers can trust |
| `IOutputCommitter<TState>` | folds closed facts into one durable state decision |
| `CommitConflictPolicy` | declared output merge policy used by feature registration and committer examples |
| `FactFeature` | registration hub for fact reducers, transactional reducers, state reducers, and outputs |
| `FactSimulation` | transactional entity lifecycle, validated id lookup, fact queue, reduction, commit, mutation routing, terminal disposal |
| `ReduceOptions` | per-call fact, work-item, pass, and elapsed-time budgets |
| `WarmupCapacityHints` | host-provided capacity hints for pre-sizing simulation stores before gameplay ticks |
| `FactListCapacityMode` | grow or fixed capacity policy for per-entity fact lists |
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
