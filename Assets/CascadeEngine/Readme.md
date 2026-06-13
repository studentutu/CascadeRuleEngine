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
    MutationCapacityPerOutput = expectedEntities
});

for (var i = 0; i < expectedEntities; i++)
{
    EntityRef entity = simulation.CreateEntity();
    // Bootstrap committed output state here when the domain requires it.
}
```

Keep the hints honest. If one gameplay tick can enqueue two input facts and two derived facts per entity, size `FactQueueCapacity` for that shape instead of assuming entity count is enough.

Warmup pre-creates buckets for fact types known from feature registration: reducer triggers, transactional requirements, batch transactional requirements, and output affected-fact declarations. Facts emitted only from reducer code and never declared in the feature cannot be warmed.

## Feature Registration

```csharp
public sealed class GameplayFeature : FactFeature
{
    public GameplayFeature()
    {
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

## Public API contract

| Type | Role |
| --- | --- |
| `IFact` | transient input or derived consequence for one tick; accepted facts are disposed when tick-local storage clears |
| `IFactReducer<TFact>` | fact-triggered reducer; emits facts only |
| `IOutputState` | durable committed state consumers can trust |
| `IOutputCommitter<TState>` | folds closed facts into one durable state decision |
| `FactFeature` | registration hub for reducers and outputs |
| `FactSimulation` | entity lifecycle, fact queue, reduction, commit, mutation routing |
| `WarmupCapacityHints` | host-provided capacity hints for pre-sizing simulation stores before gameplay ticks |
| `OutputState<TState>` | typed mutation stream descriptor |
| `StateMutation<TState>` | create/update/delete diff for one output state |

## Package Boundary

Use `FactSimulation` as the concrete runtime entry point. Do not add a second public facade until there is a real host-facing capability to hide. `IFactSimulation` exists for adapters that only need entity lifecycle, fact emission, ticks, and mutation routing.

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
