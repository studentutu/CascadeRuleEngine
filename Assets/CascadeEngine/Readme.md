# CascadeEngine

## Overview and goal

CascadeEngine provides entity-based gameplay rules with explicit inputs, committed state, and typed output changes. The goal is a small, portable C# package that replaces hidden ECS execution order and integrates with existing games and UI.

Copy this folder into your Unity project. The public API uses `CascadeEngineApi`; project-specific facts, reducers, committers, and consumers belong in your game code. See the [Hestia sample](../HestiaGame) for a complete integration.

## Usage

This example uses Hestia's existing movement feature and fact. `OnPositionChanged` is your consumer callback with signature `void OnPositionChanged(EntityRef entity, in StateMutation<HestiaPositionState> mutation)`.

```csharp
using CascadeEngineApi;
using Hestia;

// Inside a host method; choose capacities for your workload.
var feature = new HestiaGameSimulationFeature();
var settings = new CascadeSettings(
    maxEntities: 512,
    maxFactsPerEntity: 8,
    maxFactsPerTypePerEntity: 2);
using var simulation = new FactSimulation(feature, settings);
var entity = simulation.CreateEntity();

simulation.Emit(entity, new MoveRequestedFact(12f));
SimulationResult result = simulation.RunTick();
simulation.ForEachMutation(feature.Position, OnPositionChanged);
```

For gameplay, retain the simulation for its host's lifetime and dispose it at teardown. `CascadeSettings` supplies capacity and budget policy at construction; later settings edits do not reconfigure the simulation. Exceeding configured capacity fails instead of growing during gameplay.

For work spread across frames, call `RunTickIncremental` once per host frame until it returns `true`, then consume mutations. Submit the next tick's input after completion:

```csharp
void SimulateFrame()
{
    if (!simulation.RunTickIncremental(out SimulationResult result))
        return; // Continue the same tick next frame.

    simulation.ForEachMutation(feature.Position, OnPositionChanged);
}
```

Use either full ticks or incremental calls for a given loop. `RunTick` completes the tick or reports a budget failure; incremental calls may pause without publishing partial changes. `MaxWorkItemsPerStep` and `MaxMillisecondsPerStep` set per-call budgets; `MaxWorkItemsPerTick` limits total reducer work across the tick. Time limits are cooperative, so callbacks and tick completion may exceed the requested duration.

## How it works

```text
Emit entity-owned facts -> reducers derive consequences
  -> committers decide durable state -> consumers receive typed mutations
```

- Define facts with `IFact<TFact>` and durable values with `IOutputState<TState>`.
- Register rules and outputs in a `FactFeature`. Use `Reduce<TFact>().With<TReducer>()` for a fact rule and `Output<TState>(name).AffectedBy<TFact>(priority).ConflictPolicy(policy).CommitWith<TCommitter>()` for an output.
- Reducers query accumulated facts and committed entity state, then emit consequences. Committers read the same previous state and return `Set`, `Delete`, or `Unchanged` decisions.
- After successful completion, route `StateMutation<TState>` records to your world, ECS, or UI. Repeated observation replays the same records; the next tick clears them.

## Policies

- **Fact lifetime:** facts last for the full tick, including pauses. Re-emit continuing external conditions next tick. Accepted facts are immutable; model cancellation with another fact.
- **Conflicts:** identical facts deduplicate; the current runtime retains distinct same-type payloads. Declare `FoldAll`, `CollapseToSingleMarker`, or `PriorityWinnerOrThrowOnTie` per output. Priority selects output inputs; domain folds must be commutative and idempotent.
- **Publication:** a failure before publication leaves committed state unchanged. Committers must be deterministic and side-effect-free. A cleanup error after publication preserves the completed result; inspect `LastResult` before considering input replay.
- **Entities:** retain the full `EntityRef`. Its numeric slot can be reused; save and network identities need a domain-owned key. `DestroyEntity` takes effect at successful tick completion and publishes state deletions.
- **Ownership:** accepted facts belong to the simulation until completion, failure cleanup, or disposal. Rejected payloads stay caller-owned; `Emit` does not report acceptance. Fact resources are borrowed by readers and must not escape into outputs. Prefer immutable value outputs; replacement and deletion do not automatically dispose their resources.
- **Teardown:** disposing a simulation also disposes its feature tree. Disposal is terminal and idempotent; attached sub-features cannot be used as separate simulation roots.
- **Performance:** size capacity for peak workloads and keep callbacks allocation-free. Facts on allocation-free paths must explicitly implement `Dispose()`, including an empty body when they own no resources.

## Problems solved and limits

Cascade provides explicit gameplay dependencies, atomic state publication, resumable work, and typed create/change/delete output for existing consumers. Entity queries and the [Hestia movement and ammo example](../HestiaGame) demonstrate the ECS migration path; [contract tests](../Tests) cover additional usage.

Full Entitas feature and throughput parity are not established. Built-in domain-key indexes, `AnyOf` queries, persistent live groups, generated listeners, and cross-simulation transactions are not provided. Accepted facts cannot be physically retracted, and callbacks cannot rely on registration order for gameplay correctness. Mutation observation does not guarantee exactly-once external delivery.
