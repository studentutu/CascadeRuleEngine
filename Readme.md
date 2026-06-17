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
- zero memory-allocation on hot path.
- reducer loop is commutative and idempotent, order of reducers doesn't change output.
- minimal and clean API (full feature parity to Entitas ECS).

## The key rule

The commit stage is not optional glue. It is the reconciliation layer.

The concept:

```text
Facts are what happened or what was requested.
Reducers derive consequences.
Committers decide durable truth.
OutputState is the only thing consumers trust and consume.
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
  -> dirty output component published
```

## Minimal Host Flow

```csharp
var feature = new GameplayFeature();
var simulation = new FactSimulation(feature);
var entity = simulation.CreateEntity();

simulation.Emit(entity, new MoveRequestedFact(12f, priority: 1000));

SimulationResult result = simulation.RunTick(ReduceOptions.Default());

simulation.ForEachMutation(feature.Position, OnPositionChanged);
```

## Stable Type Ids

Every fact and output state type must declare one stable id:

```csharp
public readonly struct MoveRequestedFact : IFact
{
    public static readonly CascadeTypeId CascadeId =
        CascadeTypeId.FromName(nameof(MoveRequestedFact));

    public void Dispose()
    {
    }
}

public readonly struct PositionState : IOutputState
{
    public static readonly CascadeTypeId CascadeId =
        CascadeTypeId.FromName(nameof(PositionState));
}
```

`System.Type` is retained only behind generic construction and diagnostic exception utilities.

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

## Dispose Ownership Rules

`FactSimulation` owns runtime data and the feature registry tree passed into it.

`Dispose()` is terminal and idempotent: call it during scene unload, domain replacement, or editor-session cleanup. After disposal, public simulation APIs throw `ObjectDisposedException`.

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
| `CascadeTypeId` | stable fact/output-state identity used by runtime routing |
| `IFact` | transient input or derived consequence for one tick; accepted facts are disposed when tick-local storage clears |
| `IPrioritizedFact` | fact contract exposing integer priority for commit-stage winner selection |
| `IFactConflictComparer<TFact>` | output-specific same-priority conflict predicate for prioritized fact selection |
| `FactConflictResolution` | allocation-free helper for selecting a priority winner from closed facts |
| `IFactReducer<TFact>` | fact-triggered reducer; emits facts only |
| `IOutputState` | durable committed state consumers can trust |
| `IOutputCommitter<TState>` | folds closed facts into one durable state decision |
| `FactFeature` | registration hub for reducers and outputs |
| `FactSimulation` | entity lifecycle, fact queue, reduction, commit, mutation routing, terminal disposal |
| `WarmupCapacityHints` | host-provided capacity hints for pre-sizing simulation stores before gameplay ticks |
| `OutputState<TState>` | typed mutation stream descriptor |
| `StateMutation<TState>` | create/update/delete diff for one output state |

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
