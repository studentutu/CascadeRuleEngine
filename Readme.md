# CascadeEngine

## Overview and goal

CascadeEngine is a portable C# package for entity-based gameplay rules. Its goal is to replace hidden ECS execution order with explicit inputs, rules, committed state, and typed changes that an existing game, ECS, or UI can consume.

```text
Input/events -> facts -> gameplay rules -> committed state -> consumers
```

Facts describe what happened or was requested. Reducers derive consequences. Committers decide durable state. Consumers observe the published result.

### Minimal flow example

With a project-defined `GameplayFeature`, movement fact, and mutation handler:

```csharp
var feature = new GameplayFeature();
using var simulation = new FactSimulation(feature);
var entity = simulation.CreateEntity();

simulation.Emit(entity, new MoveRequestedFact(12f));

SimulationResult result = simulation.RunTick(ReduceOptions.Default());

simulation.ForEachMutation(feature.Position, OnPositionChanged);
```

See the [package usage guide](Assets/CascadeEngine/Readme.md) for capacity settings and incremental execution. The [Hestia sample](Assets/HestiaGame) provides a complete movement and ammo integration.

## Problems solved

- Express gameplay dependencies through facts and explicit conflict policies instead of system registration order.
- Publish completed state changes together, so consumers do not observe intermediate writes.
- Resume unfinished work across frames with explicit work and time budgets.
- Integrate entity creation, state changes, and destruction with existing ECS, world, and UI consumers.

## Usage policies and scope

- Facts last for one full tick, including incremental calls. Emit continuing external conditions again next tick; keep lasting values in committed state.
- Reducers read state and emit facts. Committers own durable writes; domain rules must resolve conflicts consistently.
- Time budgets are cooperative. Individual callbacks and tick completion can exceed a requested slice.
- Allocation-free gameplay requires sufficient capacity and allocation-free callbacks. Full Entitas feature and throughput parity remain goals, not guarantees.
- The host owns simulation construction, ticking, and disposal.

Use the folder at [Assets/CascadeEngine](Assets/CascadeEngine) in your project. Gameplay examples remain outside the package. The [contract tests](Assets/Tests) provide further executable usage examples.
