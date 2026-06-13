# CascadeEngine

## Overview

`CascadeEngine` is the drop-in package boundary for the Cascade Rule Engine.

The package replaces reducer-side state writes with a rigid pipeline:

```text
emit facts
-> run reducers until the fact graph closes
-> run output committers for affected entities
-> publish typed output mutations
```

Core rule: 
- reducers never write durable state.
- reducers only read committed state plus accumulated tick facts, then emit more facts. 
- committers are the only code that writes `IOutputState`.

## Minimal Host Flow

```csharp
var feature = new GameplayFeature();
var simulation = new FactSimulation(feature);
var entity = simulation.CreateEntity();

simulation.Emit(entity, new MoveRequestedFact(12f, FactPriority.PlayerVisible));

SimulationResult result = simulation.RunTick(ReduceOptions.Default());

simulation.ForEachMutation(feature.Position, OnPositionChanged);
```

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

## Main Public API Types

| Type | Role |
| --- | --- |
| `IFact` | transient input or derived consequence for one tick |
| `IFactReducer<TFact>` | fact-triggered reducer; emits facts only |
| `IOutputState` | durable committed state consumers can trust |
| `IOutputCommitter<TState>` | folds closed facts into one durable state decision |
| `FactFeature` | registration hub for reducers and outputs |
| `FactSimulation` | entity lifecycle, fact queue, reduction, commit, mutation routing |
| `OutputState<TState>` | typed mutation stream descriptor |
| `StateMutation<TState>` | create/update/delete diff for one output state |

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
