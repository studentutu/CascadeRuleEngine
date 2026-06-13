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
Facts as Components -> Reducers as Systems (they produce other facts but never write into the actual output components) -> single Feature.cs to hold registration of all of the Reducers (Systems), similar to the Features in Entitas -> ReduceAll (run until no more facts across entities with a build in budget, guardrails and prioritization of specific-relevant-flagged entities, just be sure the full ecs-entitas API parity exists such as query for component/fact/state/other entities inside the reducer) + Commit (this is unknowns territory for now which we need to resolve) -> dirty entity with modified output state components. COnsumers can then poll in the domain modules/ECS view systems for the specific entity's state.

What I need you to do is to make an example usage of the proposed API (we need to start from the actual use and go backwards from there). We need to find out what commit stage should be doing and how it will actually be used.

Basically:

- instead of systems, we have reducers (which don't even begin to be executed until fact appears and a dictionary of facts to reducers for the current SimulationTick executed, mapped reducers for each entity (reducers don't mutate output state directly, so order of execution  is irrelevant)
- finally commit stage executes stage changes (multiple facts commit a single output component). Output components are created after full reduction (till no more facts exist or some limit on reduction passes is reached)
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
  -> commit touched entities (we can even make a publishing queue of the dirty entities, so that consumers can just subscribe to it and filter what they need)
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
  -> dirty output queue is published
```

The important design choice:

> Reducers do not write output components.
> Committers are the only code allowed to write output components.

That gives you one deterministic place to merge many facts into one final component.

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

    public GameSimulationLoop(IFactSimulation simulation)
    {
        _simulation = simulation;
    }

    public void Simulate()
    {
        SimulationResult result = _simulation.RunTick(new ReduceOptions
        {
            MaxFacts = 50_000,
            MaxPasses = 64,
            MaxMilliseconds = 8,
            BudgetMode = BudgetMode.PriorityFirst,
            IncompleteCommitMode = IncompleteCommitMode.DoNotCommitIncompleteEntities
        });

        // Remark: this is way to broad of an consumer example, prefer 'IDirtyEntityQueue.Consume<PositionState>()' instead in the actual production.
        foreach (DirtyEntity dirty in result.DirtyEntities)
        {
            // TODO: mvp example, this is bad design choice for single consumer to look on all changes.
            if (dirty.HasChanged<PositionState>())
            {
                PositionState position = _simulation.State.Get<PositionState>(dirty.Entity);
                // Notify view, physics proxy, streaming, etc.
            }

            if (dirty.HasChanged<InventoryState>())
            {
                InventoryState inventory = _simulation.State.Get<InventoryState>(dirty.Entity);
                // Notify UI, inventory screen, drag-and-drop module, etc.
            }
        }
    }
}
```

From the outside, the usage becomes:

```text
emit facts
run tick
read committed state
react to dirty state
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

        Output<PositionState>()
            .AffectedBy<MoveResolvedFact>()
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
FactType -> Reducers
FactType -> Affected Output Committers
```

So no global system scan happens.

---

## 3. Facts versus output components

### Facts are transient (input/change)

Facts exist only during reduction.

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

    EntityQuery Query { get; }

    void Emit<TFact>(EntityRef entity, in TFact fact)
        where TFact : struct, IFact;

    void EmitGlobal<TFact>(in TFact fact)
        where TFact : struct, IFact;

    void Touch(EntityRef entity);
}
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
}
```

Query API:

```csharp
public interface EntityQuery
{
    IEnumerable<EntityRef> With<TState>()
        where TState : struct, IOutputState;

    IEnumerable<EntityRef> With<TStateA, TStateB>()
        where TStateA : struct, IOutputState
        where TStateB : struct, IOutputState;

    IEnumerable<EntityRef> WithFact<TFact>()
        where TFact : struct, IFact;
}
```

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

    EntityQuery Query { get; }

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
8. Publish dirty entities and commit events.
9. Prepare next facts, e.g. continuous movement with following pipeline: input -> fact -> reduction -> commit -> fact.
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
            DirtyEntities = commitResult.DirtyEntities,
            Events = commitResult.Events
        };
    }
}
```

Commit should **not** run gameplay logic. It can emit more facts for the next tick.

Allowed:

```csharp
ctx.Publish(entity, new InventoryChangedEvent { ... });
```

```csharp
ctx.Emit(entity, new InertiaFact()); // bad during commit
```

But use this sparingly.

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

        QueuedFact queued = tick.WorkQueue.PopHighestPriority();

        ReducerList reducers = _registry.GetReducers(queued.FactType);

        foreach (IFactReducer reducer in reducers)
        {
            reducer.ReduceUntyped(tick.Context, queued.Entity, queued.Fact);
        }
    }

    return ReduceResult.Complete(tick);
}
```

A newly emitted fact is added only if it is new.

```csharp
public void Emit<TFact>(EntityRef entity, in TFact fact)
    where TFact : struct, IFact
{
    FactKey key = FactKey.Create(entity, fact);

    if (!_factSet.Add(key))
        return;

    _facts.Add(entity, fact);

    FactPriority priority = ResolvePriority(entity, fact);

    _workQueue.Enqueue(entity, fact, priority);

    _touchedEntities.Add(entity);

    foreach (OutputRegistration output in _registry.GetAffectedOutputs<TFact>())
    {
        _touchedOutputs.Add(entity, output.OutputType);
    }
}
```

This is critical. The engine should deduplicate facts.

Without deduplication, reducer cycles become catastrophic.

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

But if both happen, the `PositionCommitter` or `MovementStateCommitter` owns the merge policy.

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

        // TODO: this needs to be resolved by priority and only when priority is the same throw (with exception on the facts and their priority)!
        if (hasResolved && hasBlocked)
        {
            // Deterministic policy.
            // Either fail, prefer block, prefer highest priority, or choose by causal request id.
            throw new CommitConflictException(
                entity,
                typeof(MovementState),
                "MoveResolvedFact and MoveBlockedFact both exist.");
        }

        // Normal commit logic...
        return CommitDecision<MovementState>.Unchanged();
    }
}
```

For production, do not throw in release builds unless you want hard simulation failure. Instead:

```csharp
CommitConflictPolicy.PreferFailureFacts
CommitConflictPolicy.PreferHighestPriority
CommitConflictPolicy.RequireSingleResolution
CommitConflictPolicy.KeepPreviousState
```

Make the policy explicit per output.

---

## 13. Commit conflict policy should be registered with the output

```csharp
Output<PositionState>()
    .AffectedBy<MoveResolvedFact>()
    .AffectedBy<TeleportResolvedFact>()
    .ConflictPolicy(CommitConflictPolicy.RequireSingleWinner)
    .CommitWith<PositionCommitter>();
```

For inventory:

```csharp
Output<InventoryState>()
    .AffectedBy<InventoryMoveResolvedFact>()
    .AffectedBy<InventoryItemAddedFact>()
    .AffectedBy<InventoryItemRemovedFact>()
    .ConflictPolicy(CommitConflictPolicy.FoldAllInDeterministicOrder)
    .CommitWith<InventoryCommitter>();
```

For UI:

```csharp
Output<InventoryUiState>()
    .AffectedBy<InventoryDragRequestedFact>()
    .AffectedBy<InventoryMoveResolvedFact>()
    .AffectedBy<InventoryMoveRejectedFact>()
    .ConflictPolicy(CommitConflictPolicy.LatestFactWins)
    .CommitWith<InventoryUiCommitter>();
```

Different output components need different commit semantics.

---

## 14. Dirty publishing queue

Consumers should not poll the entire world.

```csharp
public readonly record struct DirtyEntity
{
    public required EntityRef Entity { get; init; }
    public required ComponentTypeMask ChangedOutputs { get; init; }

    public bool HasChanged<TState>()
        where TState : struct, IOutputState
    {
        return ChangedOutputs.Contains<TState>();
    }
}
```

Example consumer:

```csharp
public sealed class InventoryUiPresenter
{
    private readonly IFactSimulation _simulation;

    public void OnSimulationCommitted(SimulationResult result)
    {
        foreach (DirtyEntity dirty in result.DirtyEntities)
        {
            if (!dirty.HasChanged<InventoryState>())
                continue;

            InventoryState inventory =
                _simulation.State.Get<InventoryState>(dirty.Entity);

            RefreshInventoryPanel(dirty.Entity, inventory);
        }
    }
}
```

View ECS system can also consume dirty queues:

```csharp
public sealed class PositionViewSystem
{
    private readonly ICommittedStateStore _state;
    private readonly IDirtyEntityQueue _dirty;

    public void Execute()
    {
        foreach (DirtyEntity dirty in _dirty.Consume<PositionState>())
        {
            PositionState position = _state.Get<PositionState>(dirty.Entity);
            UpdateTransform(dirty.Entity, position);
        }
    }
}
```

This gives you the “React render only dirty subtree” effect.

---

## 15. Entitas bridge

You can keep Entitas for legacy consumers, but write to it only during commit.

```csharp
public sealed class EntitasOutputSink : IOutputSink
{
    private readonly GameContext _game;

    public void Set<TState>(EntityRef entity, in TState state)
        where TState : struct, IOutputState
    {
        GameEntity entitasEntity = _game.GetEntityWithEntityId(entity.Id);

        switch (state)
        {
            case PositionState position:
                entitasEntity.ReplacePosition(
                    position.Cell.X,
                    position.Cell.Y,
                    position.Facing);
                break;

            case InventoryState inventory:
                entitasEntity.ReplaceInventory(
                    inventory.Slots,
                    inventory.Version);
                break;
        }
    }

    public void Remove<TState>(EntityRef entity)
        where TState : struct, IOutputState
    {
        GameEntity entitasEntity = _game.GetEntityWithEntityId(entity.Id);

        if (typeof(TState) == typeof(PositionState) && entitasEntity.hasPosition)
            entitasEntity.RemovePosition();

        if (typeof(TState) == typeof(InventoryState) && entitasEntity.hasInventory)
            entitasEntity.RemoveInventory();
    }
}
```

Long-term, durable output state can live outside Entitas entirely. During migration, mirror committed output state back into Entitas components.

The expensive operation becomes:

```text
many temporary facts -> one output component replace
```

instead of:

```text
many temporary requests -> many Entitas add/remove/replace operations
```

---

## 16. Full example pipeline

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
  -> MoveCandidateFact

MoveCandidateFact
  -> MovementCollisionReducer
  -> MoveResolvedFact

MoveResolvedFact
  -> MovementAnimationReducer
  -> PlayMoveAnimationFact
```

Commit:

```text
PositionState affected by MoveResolvedFact
MovementState affected by MoveRequestedFact / MoveResolvedFact
AnimationState affected by PlayMoveAnimationFact
```

Final dirty result:

```csharp
DirtyEntity
{
    Entity = player,
    ChangedOutputs =
    {
        PositionState,
        MovementState,
        AnimationState
    }
}
```

Consumers:

```csharp
PositionViewSystem consumes PositionState dirty entities.
AnimationSystem consumes AnimationState dirty entities.
StreamingSystem consumes PositionState dirty entities.
Inventory UI ignores this entity entirely.
```

---

## 17. Budgeting model

Budgeting should happen at the fact queue level, not the system level.

```csharp
public sealed class ReduceOptions
{
    public required int MaxFacts { get; init; }
    public required int MaxPasses { get; init; }
    public required int MaxMilliseconds { get; init; }

    public required BudgetMode BudgetMode { get; init; }
    public required IncompleteCommitMode IncompleteCommitMode { get; init; }

    public EntityPriorityProvider? PriorityProvider { get; init; }
}
```

Modes:

```csharp
public enum BudgetMode
{
    Fifo,
    PriorityFirst,
    RelevantEntitiesFirst,
    VisibleEntitiesFirst
}
```

Incomplete commit modes:

```csharp
public enum IncompleteCommitMode
{
    Throw,
    DoNotCommitAnything,
    DoNotCommitIncompleteEntities,
    CommitClosedOutputsOnly
}
```

Recommended default:

```csharp
IncompleteCommitMode.DoNotCommitIncompleteEntities
```

Meaning:

```text
If an entity's fact graph did not close, do not publish partial output for that entity.
Completed entities can still commit.
Unfinished facts are carried or dropped according to explicit policy.
```

This prevents half-resolved inventory or half-resolved movement from leaking into durable state.

---

## 18. Guardrails

You need these from day one.

```csharp
public sealed class FactGuardrails
{
    public int MaxFactsPerEntity { get; init; } = 512;
    public int MaxFactsPerTypePerEntity { get; init; } = 64;
    public int MaxReducerInvocationsPerTick { get; init; } = 100_000;
    public int MaxCausalDepth { get; init; } = 32;
    public bool DetectCycles { get; init; } = true;
    public bool FailOnCommitConflictInDebug { get; init; } = true;
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
    public required FactPriority Priority { get; init; }
    public required int Depth { get; init; }
}
```

The user-facing fact structs do not need to expose this unless useful.

---

## 19. Recommended naming

Avoid calling everything a component. Use strict names:

```text
Fact              transient input/intermediate data
Reducer           fact -> more facts
OutputState       durable committed state
Committer         facts -> output state
DirtyEntity       published committed diff
CommitEvent       post-commit notification
StateStore        durable output component storage
FactStore         tick-local transient fact storage
```

This avoids the current Entitas problem where requests, state, tags, events, and outputs all become “components”.

---

## 20. Minimal API skeleton

```csharp
public interface IFactSimulation
{
    ICommittedStateStore State { get; }

    void Emit<TFact>(EntityRef entity, in TFact fact)
        where TFact : struct, IFact;

    SimulationResult RunTick(ReduceOptions options);
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

## 21. What this solves directly

| Current issue                                 | New model                                                                             |
| --------------------------------------------- | ------------------------------------------------------------------------------------- |
| System execution order matters                | Reducers are fact-triggered and idempotent; final conflicts handled by committers     |
| Request created late resolves next simulation | Reduction runs to closure in the same tick                                            |
| One core component change causes mass edits   | Output state is projected in one committer; reducers depend on stable read/query APIs |
| All systems run constantly                    | Only reducers mapped to existing facts execute                                        |
| Budgeting is hard                             | Budget the fact queue by priority/entity relevance                                    |
| Adding request components causes spikes       | Facts are stored in transient append-only stores, not Entitas structural components   |
| Multi-step sub-tick resolution                | The reducer loop is the sub-tick resolution engine                                    |
| Direct non-ECS changes are awkward            | Domain modules emit facts and read committed state, works more like database          |
| Consumers over-poll                           | Dirty queue tells consumers exactly which entities/output states changed              |

---

## 22. The key rule

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
