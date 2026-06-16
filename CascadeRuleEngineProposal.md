# Cascade rule engine

## Goal

Improve ECS in C# Entitas project with over 1500 systems and over 500 components which makes maintenance a hell, specifically

States issues:

- order of system execution matters and even 1 misfired system makes a whole lot of issues,
- heavily rely on temporal resolution as 1 request created at the end of simulation can only be resolved on the next simulation,
- a single core component change makes 1500 edits across all systems,
- all systems are constantly running even when the actual query is not fired,
- budgeting is hard as 1 relevant entity needs to run all systems while non-relevant entities should be completely ignored,
- overall performance is bottleneck as adding components is not a fast operation (requests always become spikes),
- we have a multi step sub-tick resolution just to fully resolve movement or inventory change,
- non-ecs direct changes become very hard to implement (inventory, drag-and-drop, hierarchical streaming, custom lod switching).

We are tired of this. We need a concrete solution.
React/Virtual-DOM has hundreds or even thousands of unique objects all over the place, and it still renders lightning-fast even when actively scrolling or making change in a single object. We need the same concept applied to the Components!

What if we preserve ECS approach and apply React/Virtual-Dom/Cascade concept:
Facts as Components -> Reducers as Systems (they produce other facts but never write into the actual output components) -> single Feature.cs to hold registration of all of the Reducers (Systems), similar to the Features in Entitas -> ReduceAll (run until no more facts across entities with built-in budgets and guardrails, just be sure the full ecs-entitas API parity exists such as query for component/fact/state/other entities inside the reducer) + Commit (this is unknowns territory for now which we need to resolve) -> dirty entity with modified output state components. COnsumers can then poll in the domain modules/ECS view systems for the specific entity's state.

What I need you to do is to make an example usage of the proposed API (we need to start from the actual use and go backwards from there). We need to find out what commit stage should be doing and how it will actually be used.

Basically:

- instead of systems, we have reducers (which don't even begin to be executed until fact appears and a dictionary of facts to reducers for the current SimulationTick executed, mapped reducers for each entity (reducers don't mutate output state directly)
- finally committers project the closed fact set into durable output state. Output components are created/updated/deleted after full reduction (till no more facts exist or some limit on reduction passes is reached)
- instead of all components being written/read, we divide the input/processing/temporal requests as facts and output components (on the entity)
- after commit, work is done, entity state is updated, and can be polled by domain logic (UI/some view ECS systems, some managers)
- full feature API parity to the Entitas (poll entity for its facts/output components)
- a single place to register all reducers and outputs, preferably akin to Entitas Feature.cs where all systems are registered in one file or sub-features.
- ideally, eliminate useless constant work of exiting all systems/reduces (as only dirty facts are resolved) and adding new reducers should be easy for the developers (no more issues stated above)
- so full pipeline looks like this:

```text
Input
  -> fact_1
  -> ReducerMap[fact_1] -> reducer_A(context, fact_1)
  -> fact_2
  -> ReducerMap[fact_2] -> reducer_B(context, fact_2)
  -> fact_3
  -> no more unprocessed facts, run commit phase
  -> commit touched entities and affected outputs
  -> publish typed output mutations for consumers
```

Similar to NRules but tightly constrained against zero-allocation and output properties.

---

## Proposal Overview

The concrete solution is to **stop treating temporary requests as Entitas components**.

Keep Entitas-style entity identity and query ergonomics, but introduce a separate transactional layer:

```text
Input facts
  -> fact work queue
  -> reducers emit more facts
  -> reduction reaches closure
  -> committers project facts into durable output components
  -> typed output mutations are published
```

The important design choice:

> Reducers do not write output components.
> Committers are the only code allowed to write output components.

That gives you one deterministic place to merge many facts into one final component.

Current design decisions:

```text
Facts are set-like within one tick per entity: duplicate entity/fact/payload keys are deduplicated.
Reducers read committed output state and accumulated tick facts only.
Reducers never read staged output state because staged output does not exist.
Intermediate reducer results must be facts.
Same tick closure is mandatory when the graph can close within the configured budget.
Equal priority conflicts are logic errors and throw with both conflicting facts attached.
Missing output state is real: consumers must use HasState/TryGetState or mutation create/delete flags.
Destroyed entities are permanent. New facts for destroyed entities are rejected before reduction.
Entity creation/destruction is part of the core lifecycle API, not a host-side convention.
MVP consumer output is ForEachMutation(output), not a dirty entity queue.
```

The implementation must stay C# 8 / Unity compatible. Any snippet using newer syntax is descriptive only until rewritten into package code.

---

## 1. Example usage first

### Gameplay code creates facts, not components

```csharp
public sealed class PlayerInputDomain
{
    private readonly IFactSimulation _simulation;

    public PlayerInputDomain(IFactSimulation simulation)
    {
        _simulation = simulation;
    }

    public void RequestMove(EntityRef player, GridDirection direction)
    {
        _simulation.Emit(player, new MoveRequestedFact
        {
            Direction = direction,
            Source = InputSource.Player,
            Priority = FactPriority.PlayerVisible
        });
    }

    public void RequestInventoryDrag(
        EntityRef player,
        InventorySlotRef from,
        InventorySlotRef to)
    {
        _simulation.Emit(player, new InventoryDragRequestedFact
        {
            From = from,
            To = to,
            Priority = FactPriority.PlayerVisible
        });
    }
}
```

No Entitas component is added here. These are transient facts in a tick-local fact store.

---

### Simulation tick

```csharp
public sealed class GameSimulationLoop
{
    private readonly IFactSimulation _simulation;
    private readonly GameplayCascadeSchema _schema;

    public GameSimulationLoop(IFactSimulation simulation, GameplayCascadeSchema schema)
    {
        _simulation = simulation;
        _schema = schema;
    }

    public void Simulate()
    {
        _simulation.RunTick(new ReduceOptions
        {
            MaxFacts = 50_000,
            MaxPasses = 64,
            MaxMilliseconds = 8
        });

        _simulation.ForEachMutation(_schema.Position, (entity, change) =>
        {
            if (!change.HasNext)
                return;

            PositionState position = change.Next;
            // Notify view, physics proxy, streaming, etc.
        });

        _simulation.ForEachMutation(_schema.Inventory, (entity, change) =>
        {
            if (!change.HasNext)
                return;

            InventoryState inventory = change.Next;
            // Notify UI, inventory screen, drag-and-drop module, etc.
        });
    }
}
```

From the outside, the usage becomes:

```text
emit facts
run tick
read committed state
route typed output mutations
```

---

## 2. Feature registration

This replaces giant Entitas `Feature.cs` chains.

```csharp
public sealed class GameplayFactFeature : FactFeature
{
    public GameplayFactFeature()
    {
        SubFeature(new MovementFeature());
        SubFeature(new InventoryFeature());
        SubFeature(new StreamingFeature());
        SubFeature(new LodFeature());
    }
}
```

Example sub-feature:

```csharp
public sealed class MovementFeature : FactFeature
{
    public MovementFeature()
    {
        Reduce<MoveRequestedFact>()
            .With<MoveRequestReducer>();

        Reduce<MoveCandidateFact>()
            .With<MovementCollisionReducer>();

        Reduce<MoveBlockedFact>()
            .With<MovementFailureReducer>();

        Reduce<MoveResolvedFact>()
            .With<MovementAnimationReducer>();

        Reduce<MoveResolvedFact>()
            .With<MovementAudioReducer>();

        ReduceWhen<MoveCandidateFact, MovementBlockCheckFact>()
            .With<MovementResolutionReducer>();

        ReduceBatchWhen<SpeedByInputFact, SpeedByEffectsFact, RotationFact, GravityFact, InertiaFact>()
            .With<PhysicsResolutionReducer>();

        Output<PositionState>()
            .AffectedBy<PositionResolvedFact>()
            .AffectedBy<TeleportResolvedFact>()
            .CommitWith<PositionCommitter>();

        Output<MovementState>()
            .AffectedBy<MoveRequestedFact>()
            .AffectedBy<MoveResolvedFact>()
            .AffectedBy<MoveBlockedFact>()
            .CommitWith<MovementStateCommitter>();
    }
}
```

Inventory feature:

```csharp
public sealed class InventoryFeature : FactFeature
{
    public InventoryFeature()
    {
        Reduce<InventoryDragRequestedFact>()
            .With<InventoryDragReducer>();

        Reduce<InventoryMoveCandidateFact>()
            .With<InventoryValidationReducer>();

        Reduce<InventoryMoveRejectedFact>()
            .With<InventoryFailureReducer>();

        Reduce<InventoryMoveResolvedFact>()
            .With<InventoryAnimationReducer>();

        Output<InventoryState>()
            .AffectedBy<InventoryMoveResolvedFact>()
            .AffectedBy<InventoryItemAddedFact>()
            .AffectedBy<InventoryItemRemovedFact>()
            .CommitWith<InventoryCommitter>();

        Output<InventoryUiState>()
            .AffectedBy<InventoryDragRequestedFact>()
            .AffectedBy<InventoryMoveResolvedFact>()
            .AffectedBy<InventoryMoveRejectedFact>()
            .CommitWith<InventoryUiCommitter>();
    }
}
```

The registry builds two maps:

```csharp
FactType -> immediate reducers
FactType -> transactional reducer wait lists
TransactionalReducer -> required fact mask
FactType -> Affected Output Committers
```

So no global system scan happens.

Reducer registration has three forms:

```text
Reduce<TFact>()                  runs once per newly accepted fact
ReduceWhen<TA, TB, ...>()        runs once per entity when the required fact set exists
ReduceBatchWhen<TA, TB, ...>()   runs once per tick over the eligible entity set when required facts exist
```

Transactional reducers solve the "missing or misordered fact" problem. They do not run because one fact arrived; they run because all required facts for the entity or batch are present in the tick fact store. After a transactional reducer fires for a given entity/fact-set, it is marked fired so it cannot loop without producing a new distinct fact key.

Batch transactional reducers are the escape hatch for physics-like work:

```text
Input
  -> SpeedByInputFact
  -> SpeedByEffectsFact
  -> RotationFact
  -> GravityFact
  -> InertiaFact
  -> PhysicsResolutionReducer runs once over eligible entities
  -> PositionResolvedFact per changed entity
  -> PositionCommitter writes PositionState once
```

This is not a return to "run all systems." The batch reducer receives only entities that became eligible through facts in the current tick.

---

## 3. Facts versus output components

### Facts are transient (input/change)

Facts exist only during reduction.
Within one tick, facts are set-like and deduplicated by entity, fact type, and payload value. Causal metadata is diagnostic and must not accidentally make duplicates distinct. If gameplay needs "two identical shots", emit one fact with `Amount = 2` or include an intentional disambiguating value in the payload. Do not rely on duplicate queue entries.

```csharp
public interface IFact
{
}
```

Example movement facts:

```csharp
public readonly record struct MoveRequestedFact : IFact
{
    public required GridDirection Direction { get; init; }
    public required InputSource Source { get; init; }
    public required FactPriority Priority { get; init; }
}

public readonly record struct MoveCandidateFact : IFact
{
    public required GridPosition From { get; init; }
    public required GridPosition To { get; init; }
    public required GridDirection Direction { get; init; }
    public required FactPriority Priority { get; init; }
}

public readonly record struct MoveBlockedFact : IFact
{
    public required GridPosition From { get; init; }
    public required GridPosition BlockedPosition { get; init; }
    public required BlockReason Reason { get; init; }
}

public readonly record struct MoveResolvedFact : IFact
{
    public required GridPosition From { get; init; }
    public required GridPosition To { get; init; }
    public required GridDirection Direction { get; init; }
}
```

Inventory facts:

```csharp
public readonly record struct InventoryDragRequestedFact : IFact
{
    public required InventorySlotRef From { get; init; }
    public required InventorySlotRef To { get; init; }
    public required FactPriority Priority { get; init; }
}

public readonly record struct InventoryMoveCandidateFact : IFact
{
    public required InventorySlotRef From { get; init; }
    public required InventorySlotRef To { get; init; }
    public required ItemStack Stack { get; init; }
}

public readonly record struct InventoryMoveRejectedFact : IFact
{
    public required InventorySlotRef From { get; init; }
    public required InventorySlotRef To { get; init; }
    public required InventoryRejectReason Reason { get; init; }
}

public readonly record struct InventoryMoveResolvedFact : IFact
{
    public required InventorySlotRef From { get; init; }
    public required InventorySlotRef To { get; init; }
    public required ItemStack Stack { get; init; }
}
```

---

### Output components are durable (state mutation)

These are the things consumers poll.
An output state is absent until a committer creates it. Default struct values are not secretly valid state. Consumers and reducers must use `HasState` / `TryGetState` unless the output is guaranteed by bootstrap.

```csharp
public interface IOutputState
{
}
```

```csharp
public readonly record struct PositionState : IOutputState
{
    public required GridPosition Cell { get; init; }
    public required GridDirection Facing { get; init; }
}

public readonly record struct MovementState : IOutputState
{
    public required bool IsMoving { get; init; }
    public required GridPosition? Target { get; init; }
    public required BlockReason? LastBlockReason { get; init; }
}

public readonly record struct InventoryState : IOutputState
{
    public required ImmutableArray<ItemStack> Slots { get; init; }
    public required int Version { get; init; }
}
```

Reducers read these as immutable state snapshots. They never mutate them.

---

## 4. Reducer API

A reducer runs only when its triggering fact exists.

```csharp
public interface IFactReducer<TFact>
    where TFact : struct, IFact
{
    void Reduce(IReduceContext ctx, EntityRef entity, in TFact fact);
}
```

Transactional reducers run when their declared fact set exists:

```csharp
public interface ITransactionalReducer
{
    void Reduce(ITransactionReduceContext ctx, EntityRef entity);
}

public interface IBatchTransactionalReducer
{
    void ReduceBatch(IBatchReduceContext ctx, ReadOnlySpan<EntityRef> entities);
}
```

The context should feel like Entitas, but transactional:

```csharp
public interface IReduceContext
{
    SimulationTick Tick { get; }

    IEntityFactView Facts(EntityRef entity);

    bool HasState<TState>(EntityRef entity)
        where TState : struct, IOutputState;

    TState GetState<TState>(EntityRef entity)
        where TState : struct, IOutputState;

    bool TryGetState<TState>(EntityRef entity, out TState state)
        where TState : struct, IOutputState;

    IEntityQuery Query { get; }

    EntityRef CreateEntity();

    bool IsDestroyed(EntityRef entity);

    void DestroyEntity(EntityRef entity);

    void Emit<TFact>(EntityRef entity, in TFact fact)
        where TFact : struct, IFact;

}
```

Lifecycle rules:

```text
CreateEntity allocates a live entity id immediately and returns it to the reducer.
New entities have no output state until committers set output state.
Facts may be emitted to newly created entities in the same tick.
DestroyEntity is permanent and tombstones the entity immediately.
Facts emitted to destroyed entities are rejected before the reduction loop.
Already queued facts for entities destroyed earlier in the same tick are skipped before reducer dispatch.
Committers do not run for destroyed entities unless explicitly registered for lifecycle cleanup output.
```

Host and consumer lifecycle parity:

```text
IFactSimulation.CreateEntity can be called outside reduction before or after RunTick.
IFactSimulation.DestroyEntity can be called outside reduction before or after RunTick.
Destroying an entity deletes its output states and publishes typed delete mutations.
Reducer-side creation can participate in the same tick by receiving emitted facts.
Consumer-side creation/destruction affects the next tick unless it is called before RunTick.
Entity ids are handles owned by the Cascade runtime; destroyed ids are not reused in the MVP.
```

Entity fact view:

```csharp
public interface IEntityFactView
{
    bool Has<TFact>()
        where TFact : struct, IFact;

    bool TryGetLatest<TFact>(out TFact fact)
        where TFact : struct, IFact;

    ReadOnlySpan<TFact> All<TFact>()
        where TFact : struct, IFact;

    bool HasAll(FactMask requiredFacts);
}
```

Query API:

```csharp
public interface IEntityQuery
{
    EntityQueryResult With<TState>()
        where TState : struct, IOutputState;

    EntityQueryResult With<TStateA, TStateB>()
        where TStateA : struct, IOutputState
        where TStateB : struct, IOutputState;

    EntityQueryResult WithFact<TFact>()
        where TFact : struct, IFact;
}
```

`EntityQueryResult` must be allocation-free in the hot path. Do not ship `IEnumerable<EntityRef>` as the core query primitive; it is too easy to allocate and hide work.

---

## 5. Movement reducer example

```csharp
public sealed class MoveRequestReducer : IFactReducer<MoveRequestedFact>
{
    public void Reduce(
        IReduceContext ctx,
        EntityRef entity,
        in MoveRequestedFact fact)
    {
        if (!ctx.TryGetState<PositionState>(entity, out PositionState position))
            return;

        GridPosition target = position.Cell.Step(fact.Direction);

        ctx.Emit(entity, new MoveCandidateFact
        {
            From = position.Cell,
            To = target,
            Direction = fact.Direction,
            Priority = fact.Priority
        });
    }
}
```

Collision reducer:

```csharp
public sealed class MovementCollisionReducer : IFactReducer<MoveCandidateFact>
{
    public void Reduce(
        IReduceContext ctx,
        EntityRef entity,
        in MoveCandidateFact fact)
    {
        if (IsBlocked(ctx, fact.To, out BlockReason reason))
        {
            ctx.Emit(entity, new MoveBlockedFact
            {
                From = fact.From,
                BlockedPosition = fact.To,
                Reason = reason
            });

            return;
        }

        ctx.Emit(entity, new MoveResolvedFact
        {
            From = fact.From,
            To = fact.To,
            Direction = fact.Direction
        });
    }

    private static bool IsBlocked(
        IReduceContext ctx,
        GridPosition position,
        out BlockReason reason)
    {
        foreach (EntityRef other in ctx.Query.Nearby(position, radius: 0))
        {
            if (!ctx.TryGetState<BlockerState>(other, out BlockerState blocker))
                continue;

            if (!blocker.BlocksMovement)
                continue;

            reason = BlockReason.Occupied;
            return true;
        }

        reason = default;
        return false;
    }
}
```

Notice what did **not** happen:

```csharp
// Not allowed:
entity.ReplacePosition(...);
entity.isMoving = true;
entity.AddMoveResolved(...);
```

The reducer only emits more facts.

---

## 6. Inventory reducer example

```csharp
public sealed class InventoryDragReducer
    : IFactReducer<InventoryDragRequestedFact>
{
    public void Reduce(
        IReduceContext ctx,
        EntityRef entity,
        in InventoryDragRequestedFact fact)
    {
        if (!ctx.TryGetState<InventoryState>(entity, out InventoryState inventory))
            return;

        ItemStack stack = inventory.Slots[fact.From.Index];

        if (stack.IsEmpty)
        {
            ctx.Emit(entity, new InventoryMoveRejectedFact
            {
                From = fact.From,
                To = fact.To,
                Reason = InventoryRejectReason.EmptySourceSlot
            });

            return;
        }

        ctx.Emit(entity, new InventoryMoveCandidateFact
        {
            From = fact.From,
            To = fact.To,
            Stack = stack
        });
    }
}
```

Validation reducer:

```csharp
public sealed class InventoryValidationReducer
    : IFactReducer<InventoryMoveCandidateFact>
{
    public void Reduce(
        IReduceContext ctx,
        EntityRef entity,
        in InventoryMoveCandidateFact fact)
    {
        InventoryState inventory = ctx.GetState<InventoryState>(entity);

        ItemStack target = inventory.Slots[fact.To.Index];

        if (!target.IsEmpty && !target.CanStackWith(fact.Stack))
        {
            ctx.Emit(entity, new InventoryMoveRejectedFact
            {
                From = fact.From,
                To = fact.To,
                Reason = InventoryRejectReason.IncompatibleTargetSlot
            });

            return;
        }

        ctx.Emit(entity, new InventoryMoveResolvedFact
        {
            From = fact.From,
            To = fact.To,
            Stack = fact.Stack
        });
    }
}
```

Again, no output mutation.

---

## 7. Committer API

This is the missing piece.

A committer projects completed facts into durable output state.

```csharp
public interface IOutputCommitter<TState>
    where TState : struct, IOutputState
{
    CommitDecision<TState> Commit(
        ICommitContext ctx,
        EntityRef entity,
        in Optional<TState> previous);
}
```

Commit context:

```csharp
public interface ICommitContext
{
    SimulationTick Tick { get; }

    IEntityFactView Facts(EntityRef entity);

    bool HasState<TState>(EntityRef entity)
        where TState : struct, IOutputState;

    TState GetState<TState>(EntityRef entity)
        where TState : struct, IOutputState;

    bool TryGetState<TState>(EntityRef entity, out TState state)
        where TState : struct, IOutputState;

    IEntityQuery Query { get; }

    void Publish<TEvent>(EntityRef entity, in TEvent evt)
        where TEvent : struct, ICommitEvent;
}
```

Commit result:

```csharp
public readonly record struct CommitDecision<TState>
    where TState : struct, IOutputState
{
    public bool Changed { get; init; }
    public bool Remove { get; init; }
    public TState Value { get; init; }

    public static CommitDecision<TState> Unchanged()
    {
        return new CommitDecision<TState>
        {
            Changed = false,
            Remove = false,
            Value = default
        };
    }

    public static CommitDecision<TState> Set(TState value)
    {
        return new CommitDecision<TState>
        {
            Changed = true,
            Remove = false,
            Value = value
        };
    }

    public static CommitDecision<TState> Delete()
    {
        return new CommitDecision<TState>
        {
            Changed = true,
            Remove = true,
            Value = default
        };
    }
}
```

---

## 8. Movement commit example

```csharp
public sealed class PositionCommitter : IOutputCommitter<PositionState>
{
    public CommitDecision<PositionState> Commit(
        ICommitContext ctx,
        EntityRef entity,
        in Optional<PositionState> previous)
    {
        if (!previous.HasValue)
            return CommitDecision<PositionState>.Unchanged();

        PositionState old = previous.Value;

        if (!ctx.Facts(entity).TryGetLatest<MoveResolvedFact>(out MoveResolvedFact move))
            return CommitDecision<PositionState>.Unchanged();

        PositionState next = old with
        {
            Cell = move.To,
            Facing = move.Direction
        };

        if (next.Equals(old))
            return CommitDecision<PositionState>.Unchanged();

        ctx.Publish(entity, new PositionChangedEvent
        {
            From = old.Cell,
            To = next.Cell
        });

        return CommitDecision<PositionState>.Set(next);
    }
}
```

Movement visual state:

```csharp
public sealed class MovementStateCommitter : IOutputCommitter<MovementState>
{
    public CommitDecision<MovementState> Commit(
        ICommitContext ctx,
        EntityRef entity,
        in Optional<MovementState> previous)
    {
        MovementState old = previous.GetValueOrDefault(new MovementState
        {
            IsMoving = false,
            Target = null,
            LastBlockReason = null
        });

        IEntityFactView facts = ctx.Facts(entity);

        if (facts.TryGetLatest<MoveResolvedFact>(out MoveResolvedFact resolved))
        {
            MovementState next = old with
            {
                IsMoving = true,
                Target = resolved.To,
                LastBlockReason = null
            };

            return next.Equals(old)
                ? CommitDecision<MovementState>.Unchanged()
                : CommitDecision<MovementState>.Set(next);
        }

        if (facts.TryGetLatest<MoveBlockedFact>(out MoveBlockedFact blocked))
        {
            MovementState next = old with
            {
                IsMoving = false,
                Target = null,
                LastBlockReason = blocked.Reason
            };

            return next.Equals(old)
                ? CommitDecision<MovementState>.Unchanged()
                : CommitDecision<MovementState>.Set(next);
        }

        return CommitDecision<MovementState>.Unchanged();
    }
}
```

The reducer chain can be multi-step, but the final output write happens once.

---

## 9. Inventory commit example

This is where commit matters most.

Several facts may affect the same `InventoryState`. The committer folds them into one final output.

```csharp
public sealed class InventoryCommitter : IOutputCommitter<InventoryState>
{
    public CommitDecision<InventoryState> Commit(
        ICommitContext ctx,
        EntityRef entity,
        in Optional<InventoryState> previous)
    {
        if (!previous.HasValue)
            return CommitDecision<InventoryState>.Unchanged();

        InventoryState old = previous.Value;
        InventoryStateBuilder builder = InventoryStateBuilder.From(old);

        ReadOnlySpan<InventoryMoveResolvedFact> moves =
            ctx.Facts(entity).All<InventoryMoveResolvedFact>();

        if (moves.Length == 0)
            return CommitDecision<InventoryState>.Unchanged();

        foreach (InventoryMoveResolvedFact move in moves)
        {
            ApplyMove(builder, move);
        }

        InventoryState next = builder.Build(version: old.Version + 1);

        if (next.Equals(old))
            return CommitDecision<InventoryState>.Unchanged();

        ctx.Publish(entity, new InventoryChangedEvent
        {
            PreviousVersion = old.Version,
            NewVersion = next.Version
        });

        return CommitDecision<InventoryState>.Set(next);
    }

    private static void ApplyMove(
        InventoryStateBuilder builder,
        in InventoryMoveResolvedFact move)
    {
        ItemStack source = builder[move.From.Index];
        ItemStack target = builder[move.To.Index];

        if (target.IsEmpty)
        {
            builder[move.From.Index] = ItemStack.Empty;
            builder[move.To.Index] = move.Stack;
            return;
        }

        if (target.CanStackWith(move.Stack))
        {
            builder[move.From.Index] = ItemStack.Empty;
            builder[move.To.Index] = target.StackWith(move.Stack);
            return;
        }

        // This should normally not happen because validation reducer rejects it.
        // Committer remains defensive.
    }
}
```

Committers should be defensive because they are the final integrity boundary.

---

## 10. What commit actually does

The commit stage should do this:

```text
1. Stop reduction.
2. Check whether the reduction graph closed successfully.
3. Find touched entities.
4. Find output components affected by facts on those entities.
5. Run only the relevant committers.
6. Merge all same-output facts into one final output component.
7. Write output components once.
8. Publish typed output mutations and optional commit events.
9. Leave next-tick fact scheduling to the host loop or an explicit next-tick queue.
```

In code:

```csharp
public sealed class FactSimulation : IFactSimulation
{
    public SimulationResult RunTick(ReduceOptions options)
    {
        using TickScope tick = BeginTick();

        ReduceResult reduceResult = ReduceAll(tick, options);

        if (!reduceResult.IsComplete)
        {
            HandleIncompleteReduction(tick, reduceResult, options);
        }

        CommitResult commitResult = Commit(tick, reduceResult, options);

        return new SimulationResult
        {
            Tick = tick.Id,
            Complete = reduceResult.IsComplete,
            MutationCount = commitResult.MutationCount
        };
    }
}
```

Commit should **not** run gameplay logic and should not emit same-tick facts.

Allowed:

```csharp
ctx.Publish(entity, new InventoryChangedEvent { ... });
```

If a continuous process needs follow-up work next tick, schedule it explicitly after `RunTick` from the host loop or from a declared next-tick queue. Do not hide new reduction work inside commit.

---

## 11. Reduction loop

The engine should run reducers through a fact queue.

```csharp
private ReduceResult ReduceAll(TickScope tick, ReduceOptions options)
{
    while (tick.WorkQueue.HasItems)
    {
        if (tick.Budget.IsExceeded(options))
            return ReduceResult.Incomplete(tick);

        QueuedFact queued = tick.WorkQueue.PopNext();

        if (tick.Entities.IsDestroyed(queued.Entity))
            continue;

        ReducerList reducers = _registry.GetReducers(queued.FactType);

        foreach (IFactReducer reducer in reducers)
        {
            reducer.ReduceUntyped(tick.Context, queued.Entity, queued.Fact);
        }

        tick.ScheduleReadyTransactionalReducers(queued.Entity, queued.FactType);
    }

    RunReadyTransactionalReducers(tick, options);

    return ReduceResult.Complete(tick);
}
```

A newly emitted fact is added only if it is new.

```csharp
public void Emit<TFact>(EntityRef entity, in TFact fact)
    where TFact : struct, IFact
{
    if (_entities.IsDestroyed(entity))
        return;

    FactKey key = FactKey.Create(entity, fact);

    if (!_factSet.Add(key))
        return;

    _facts.Add(entity, fact);

    _workQueue.Enqueue(entity, fact);

    _touchedEntities.Add(entity);
    _transactionalScheduler.OnFactAccepted(entity, typeof(TFact));

    foreach (OutputRegistration output in _registry.GetAffectedOutputs<TFact>())
    {
        _touchedOutputs.Add(entity, output.OutputType);
    }
}
```

This is critical. The engine should deduplicate facts.

Without deduplication, reducer cycles become catastrophic.

Transactional scheduling:

```text
When a fact is accepted, update the entity's fact mask.
For each transactional reducer waiting on that fact type, check the required mask.
If the mask is complete and the reducer has not fired for that entity this tick, queue the transactional reducer.
For batch reducers, add the entity to the reducer's eligible entity set.
After the immediate fact queue drains, run ready transactional reducers.
If transactional reducers emit new facts, return to the immediate fact queue.
Repeat until both queues are empty or guardrails fail.
```

This gives same-tick closure without relying on fact arrival order.

---

## 12. Reducer order is irrelevant only with these rules

Order-independence is not automatic. You get it by enforcing these rules:

```text
Reducers are pure.
Reducers are idempotent.
Reducers only emit facts.
Facts are immutable.
Fact emission is deduplicated.
Reducers read committed state plus accumulated facts.
Reducers do not delete facts.
Reducers do not write output state.
Conflicts are resolved only in committers.
```

That means this is valid:

```text
MoveRequested
  -> MoveCandidate
  -> MoveResolved
```

And this is also valid:

```text
MoveRequested
  -> MoveCandidate
  -> MoveBlocked
```

But if both happen, the `PositionCommitter` or `MovementStateCommitter` owns the merge policy. Priority may select a winner; equal priority is a logic error.

Example:

```csharp
public enum MovementResolution
{
    None,
    Resolved,
    Blocked,
    Conflicting
}
```

```csharp
public sealed class MovementStateCommitter : IOutputCommitter<MovementState>
{
    public CommitDecision<MovementState> Commit(
        ICommitContext ctx,
        EntityRef entity,
        in Optional<MovementState> previous)
    {
        IEntityFactView facts = ctx.Facts(entity);

        bool hasResolved = facts.Has<MoveResolvedFact>();
        bool hasBlocked = facts.Has<MoveBlockedFact>();

        if (hasResolved && hasBlocked)
        {
            if (!TrySelectHighestPriority(facts, out MovementResolution winner))
            {
                throw new CommitConflictException(
                    entity,
                    typeof(MovementState),
                    "MoveResolvedFact and MoveBlockedFact have equal priority.");
            }

            return CommitWinner(previous, winner);
        }

        // Normal commit logic...
        return CommitDecision<MovementState>.Unchanged();
    }
}
```

Do not silently keep previous state for contradictory facts. That recreates the hidden-order ECS failure mode.

---

## 13. Commit conflict policy should be registered with the output

```csharp
Output<PositionState>()
    .AffectedBy<MoveResolvedFact>()
    .AffectedBy<TeleportResolvedFact>()
    .ConflictPolicy(CommitConflictPolicy.PriorityWinnerOrThrowOnTie)
    .CommitWith<PositionCommitter>();
```

For inventory:

```csharp
Output<InventoryState>()
    .AffectedBy<InventoryMoveResolvedFact>()
    .AffectedBy<InventoryItemAddedFact>()
    .AffectedBy<InventoryItemRemovedFact>()
    .ConflictPolicy(CommitConflictPolicy.FoldAllInStableFactOrder)
    .CommitWith<InventoryCommitter>();
```

For marker/event-like dirty state:

```csharp
Output<AudioCueState>()
    .AffectedBy<FootstepCueFact>()
    .AffectedBy<DryFireCueFact>()
    .ConflictPolicy(CommitConflictPolicy.CollapseToSingleMarker)
    .CommitWith<AudioCueCommitter>();
```

Different output components need different commit semantics.

---

## 14. Typed output mutation API

Consumers should not poll the entire world. MVP output is property-specific mutation routing:

```csharp
public readonly struct StateMutation<TState>
    where TState : struct, IOutputState
{
    public bool HadPrevious { get; }
    public TState Previous { get; }
    public bool HasNext { get; }
    public TState Next { get; }
}

public delegate void StateMutationHandler<TState>(
    EntityRef entity,
    in StateMutation<TState> mutation)
    where TState : struct, IOutputState;
```

Example consumer (example of Entitas ECS):

```csharp
public sealed class PositionViewSystem : IExecuteSystem
{
    private readonly IFactSimulation _simulation;
    private readonly GameplayCascadeSchema _schema;

    public void Execute()
    {
        _simulation.ForEachMutation(_schema.Position, OnPositionChanged);
    }

    private void OnPositionChanged(EntityRef entity, in StateMutation<PositionState> mutation)
    {
        if (!mutation.HasNext)
        {
            DestroyTransformProxy(entity);
            return;
        }

        UpdateTransform(entity, mutation.Next);
    }
}
```

This gives consumers the React-style effect without making them scan unrelated outputs.

Creation, update, and deletion are all visible through the same typed mutation stream:

```text
create output state   HadPrevious=false, HasNext=true
update output state   HadPrevious=true,  HasNext=true
delete output state   HadPrevious=true,  HasNext=false
```

A dirty entity queue with changed-output masks can be added later if profiling proves it is needed. It is not part of the MVP API.

---

## 15. Full example pipeline

Input:

```csharp
simulation.Emit(player, new MoveRequestedFact
{
    Direction = GridDirection.Right,
    Source = InputSource.Player,
    Priority = FactPriority.PlayerVisible
});
```

Reduction:

```text
MoveRequestedFact
  -> MoveRequestReducer
  -> SpeedByInputFact

ActiveEffectsFact
  -> SpeedEffectsReducer
  -> SpeedByEffectsFact

RotationRequestedFact
  -> RotationReducer
  -> RotationFact

GravitySampleFact
  -> GravityReducer
  -> GravityFact

PreviousPositionState + Speed/Rotation/Gravity facts
  -> InertiaReducer
  -> InertiaFact

SpeedByInputFact + SpeedByEffectsFact + RotationFact + GravityFact + InertiaFact
  -> PhysicsResolutionReducer (batch transactional reducer, once per tick)
  -> PositionResolvedFact
  -> MovementResolvedFact
```

Commit:

```text
PositionState affected by PositionResolvedFact / TeleportResolvedFact
MovementState affected by MoveRequestedFact / MovementResolvedFact / MoveBlockedFact
AnimationState affected by MovementResolvedFact
```

Final typed mutation output:

```text
ForEachMutation(PositionState)   -> update transform/physics proxy for changed entities
ForEachMutation(MovementState)   -> update movement UI/debug state
ForEachMutation(AnimationState)  -> update animation bridge
```

Consumers:

```csharp
PositionViewSystem consumes PositionState mutations.
AnimationSystem consumes AnimationState mutations.
StreamingSystem consumes PositionState mutations.
Inventory UI consumes nothing from this tick.
```

---

## 16. Budgeting model

Budgeting should happen at the fact queue level, not the system level.

```csharp
public sealed class ReduceOptions
{
    public required int MaxFacts { get; init; }
    public required int MaxPasses { get; init; }
    public required int MaxMilliseconds { get; init; }
}
```

Meaning:

```text
If the tick's fact graph does not close inside the configured budget, commit nothing.
Surface the failure with reducer/fact diagnostics.
Do not publish partial durable state in the MVP.
```

This prevents half-resolved inventory or half-resolved movement from leaking into durable state. Reducer queue ordering is not a priority policy; fact priority belongs to closed-fact conflict resolution in committers.

---

## 17. Guardrails

You need these from day one.

```csharp
public sealed class FactGuardrails
{
    public int MaxFactsPerEntity { get; init; } = 512;
    public int MaxFactsPerTypePerEntity { get; init; } = 64;
    public int MaxReducerInvocationsPerTick { get; init; } = 100_000;
    public int MaxTransactionalReducerInvocationsPerTick { get; init; } = 50_000;
    public int MaxCausalDepth { get; init; } = 32;
    public bool DetectCycles { get; init; } = true;
    public bool FailOnEqualPriorityConflict { get; init; } = true;
    public bool CountDeduplicatedFacts { get; init; } = true;
    public bool CountRejectedDestroyedEntityFacts { get; init; } = true;
}
```

Each fact should carry causal metadata internally:

```csharp
public readonly record struct FactMeta
{
    public required FactId Id { get; init; }
    public required FactId? ParentId { get; init; }
    public required EntityRef Entity { get; init; }
    public required SimulationTick Tick { get; init; }
    public required int Depth { get; init; }
}
```

The user-facing fact structs do not need to expose this unless useful.
Do not include `FactMeta.Id` or `ParentId` in the default deduplication key; metadata is for diagnostics and causal traces.

---

## 18. Recommended naming

Avoid calling everything a component. Use strict names:

```text
Fact              transient input/intermediate data
Reducer           fact -> more facts
OutputState       durable committed state
Committer         facts -> output state
StateMutation     typed output diff for one state and one entity
CommitEvent       post-commit notification
StateStore        durable output component storage
FactStore         tick-local transient fact storage
```

This avoids the current Entitas problem where requests, state, tags, events, and outputs all become "components".

---

## 19. Minimal API skeleton

```csharp
public interface IFactSimulation
{
    ICommittedStateStore State { get; }

    EntityRef CreateEntity();

    void DestroyEntity(EntityRef entity);

    void Emit<TFact>(EntityRef entity, in TFact fact)
        where TFact : struct, IFact;

    SimulationResult RunTick(ReduceOptions options);

    void ForEachMutation<TState>(
        OutputState<TState> output,
        StateMutationHandler<TState> handler)
        where TState : struct, IOutputState;
}
```

```csharp
public interface ICommittedStateStore
{
    bool Has<TState>(EntityRef entity)
        where TState : struct, IOutputState;

    TState Get<TState>(EntityRef entity)
        where TState : struct, IOutputState;

    bool TryGet<TState>(EntityRef entity, out TState state)
        where TState : struct, IOutputState;
}
```

```csharp
public abstract class FactFeature
{
    protected ReducerRegistrationBuilder<TFact> Reduce<TFact>()
        where TFact : struct, IFact;

    protected TransactionalReducerRegistrationBuilder ReduceWhen(params FactType[] requiredFacts);

    protected BatchTransactionalReducerRegistrationBuilder ReduceBatchWhen(params FactType[] requiredFacts);

    protected OutputRegistrationBuilder<TState> Output<TState>()
        where TState : struct, IOutputState;

    protected void SubFeature(FactFeature feature);
}
```

```csharp
public sealed class ReducerRegistrationBuilder<TFact>
    where TFact : struct, IFact
{
    public ReducerRegistrationBuilder<TFact> With<TReducer>()
        where TReducer : IFactReducer<TFact>;
}
```

```csharp
public sealed class TransactionalReducerRegistrationBuilder
{
    public TransactionalReducerRegistrationBuilder With<TReducer>()
        where TReducer : ITransactionalReducer;
}

public sealed class BatchTransactionalReducerRegistrationBuilder
{
    public BatchTransactionalReducerRegistrationBuilder With<TReducer>()
        where TReducer : IBatchTransactionalReducer;
}
```

```csharp
public sealed class OutputRegistrationBuilder<TState>
    where TState : struct, IOutputState
{
    public OutputRegistrationBuilder<TState> AffectedBy<TFact>()
        where TFact : struct, IFact;

    public OutputRegistrationBuilder<TState> ConflictPolicy(
        CommitConflictPolicy policy);

    public void CommitWith<TCommitter>()
        where TCommitter : IOutputCommitter<TState>;
}
```

---

## 20. What this solves directly

| Current issue                                 | New model                                                                             |
| --------------------------------------------- | ------------------------------------------------------------------------------------- |
| System execution order matters                | Reducers are fact-triggered and idempotent; final conflicts handled by committers     |
| Request created late resolves next simulation | Reduction runs to closure in the same tick                                            |
| One core component change causes mass edits   | Output state is projected in one committer; reducers depend on stable read/query APIs |
| All systems run constantly                    | Only reducers mapped to existing facts execute                                        |
| Budgeting is hard and messy                   | Budget the fact queue by priority/entity relevance                                    |
| Adding request components causes spikes       | Facts are stored in transient append-only stores, not Entitas structural components   |
| Multi-step sub-tick resolution                | The reducer loop is the sub-tick resolution engine                                    |
| Direct non-ECS changes are awkward            | Domain modules emit facts and read committed state, works more like database          |
| Consumers over-poll (no inherit state mask)   | Typed mutation streams tell consumers exactly which output state changed              |

---

## 21. The key rule

The commit stage is not optional glue. It is the reconciliation layer.

The proposed architecture should be:

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
