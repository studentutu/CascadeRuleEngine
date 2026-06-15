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

simulation.Emit(entity, new MoveRequestedFact(12f, FactPriority.PlayerVisible));

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
        Priority<MoveRequestedFact>()
            .FromFact();

        Priority<MoveResolvedFact>()
            .FromFact();

        Reduce<MoveRequestedFact>()
            .With<MoveRequestReducer>();

        Position = Output<PositionState>("Position")
            .AffectedBy<MoveResolvedFact>()
            .ConflictPolicy(CommitConflictPolicy.PriorityWinnerOrThrowOnTie)
            .CommitWith<PositionCommitter>();
    }

    public OutputState<PositionState> Position { get; }
}
```

Fact queue priority is explicit during feature registration. Facts without a priority registration use `FactPriority.Normal`.

```csharp
Priority<MoveRequestedFact>()
    .FromFact();

Priority<CustomPriorityFact>()
    .With<CustomPriorityResolver>();
```

`FromFact()` is a zero-reflection convenience for facts that implement `IPrioritizedFact`. `With<TResolver>()` registers a typed `IFactPriorityResolver<TFact>` and keeps the `Emit` path non-boxing.

## Public API contract

| Type | Role |
| --- | --- |
| `CascadeTypeId` | compact fact/output-state identity derived from feature registration |
| `CascadeReductionException` | reduction guardrail failure with budget reason, fact id/name, entity, causal depth, and reducer name |
| `IFact` | transient input or derived consequence for one tick; accepted facts are disposed when tick-local storage clears |
| `IFactPriorityResolver<TFact>` | typed fact queue priority resolver registered explicitly through `Priority<TFact>()` |
| `IFactReducer<TFact>` | fact-triggered reducer; emits facts only |
| `IOutputState` | durable committed state consumers can trust |
| `IOutputCommitter<TState>` | folds closed facts into one durable state decision |
| `FactFeature` | registration hub for reducers and outputs |
| `FactSimulation` | entity lifecycle, fact queue, reduction, commit, mutation routing, terminal disposal |
| `WarmupCapacityHints` | host-provided capacity hints for pre-sizing simulation stores before gameplay ticks |
| `FactListCapacityMode` | grow or fixed capacity policy for per-entity fact lists |
| `OutputState<TState>` | typed mutation stream descriptor |
| `StateMutation<TState>` | create/update/delete diff for one output state |
| `SimulationResultCounters` | numeric tick counters grouped away from result construction |
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

Movement demonstrates priority conflict handling:

```text
MoveRequestedFact
-> MoveResolvedFact
-> HestiaPositionCommitter picks highest priority or throws on equal-priority conflict
```

The tests under `Assets/Tests` are the executable API examples.
