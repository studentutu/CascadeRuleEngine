#nullable enable

using System;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    public sealed class FactSimulationStateReducerTests
    {
        [SetUp]
        public void SetUp()
        {
            NavigationReducer.InvocationCount = 0;
            NavigationReducer.LastBlockerCount = 0;
            SpawnReducer.CreatedEntity = default;
            FirstResumeReducer.InvocationCount = 0;
            SecondResumeReducer.InvocationCount = 0;
            DestroyFollowupReducer.InvocationCount = 0;
            DestroyFollowupReducer.SawDeadFact = false;
            DeadReducer.InvocationCount = 0;
            LifecycleStateReducer.InvocationCount = 0;
            LifecycleStateReducer.SawDeadFact = false;
        }

        [Test]
        public void CommittedActiveStateTriggersOnlyEligibleEntityAndCanQueryOtherEntity()
        {
            var feature = new NavigationFeature();
            var simulation = new FactSimulation(feature);
            var activeBot = simulation.CreateEntity();
            var dormantBot = simulation.CreateEntity();
            var blocker = simulation.CreateEntity();

            simulation.SetStateSilently(activeBot, new ActiveState());
            simulation.SetStateSilently(activeBot, new BotState(2));
            simulation.SetStateSilently(activeBot, new PositionState(0));
            simulation.SetStateSilently(dormantBot, new BotState(2));
            simulation.SetStateSilently(dormantBot, new PositionState(0));
            simulation.SetStateSilently(blocker, new BlockerState());

            var result = simulation.RunTick(NoTimeLimit());

            Assert.IsTrue(result.Complete);
            Assert.AreEqual(1, NavigationReducer.InvocationCount);
            Assert.AreEqual(1, NavigationReducer.LastBlockerCount);
            Assert.AreEqual(1, result.TransactionalReducerInvocations);
            Assert.AreEqual(1, result.ProcessedWorkItems);
            Assert.AreEqual(3, simulation.Get<PositionState>(activeBot).Value);
            Assert.AreEqual(0, simulation.Get<PositionState>(dormantBot).Value);
            Assert.AreEqual(1, result.MutationCount);
        }

        [Test]
        public void StateReducerRequiresItsTriggerStateToBeRegisteredAsOutput()
        {
            var feature = new MissingStateOutputFeature();
            var exception = Assert.Throws<InvalidOperationException>(
                () => new FactSimulation(feature));

            StringAssert.Contains(nameof(ActiveState), exception.Message);
            StringAssert.Contains(nameof(NavigationReducer), exception.Message);
            feature.Dispose();
        }

        [Test]
        public void StateReducerCanBeRegisteredBeforeTriggerOutputInSeparateSubFeature()
        {
            var simulation = new FactSimulation(new ModularStateFeature());
            var entity = simulation.CreateEntity();
            simulation.SetStateSilently(entity, new ActiveState());

            simulation.RunTick(NoTimeLimit());

            Assert.AreEqual(1, PresenceOnlyReducer.InvocationCount);
        }

        [Test]
        public void StateCreatedAtCommitTriggersNextTickAndDeletedStateStopsFutureTicks()
        {
            var feature = new NavigationFeature();
            var simulation = new FactSimulation(feature);
            var bot = simulation.CreateEntity();
            simulation.SetStateSilently(bot, new BotState(1));
            simulation.SetStateSilently(bot, new PositionState(0));

            simulation.Emit(bot, new ActivateFact());
            simulation.RunTick(NoTimeLimit());

            Assert.AreEqual(0, NavigationReducer.InvocationCount);
            Assert.IsTrue(simulation.Has<ActiveState>(bot));
            Assert.AreEqual(0, simulation.Get<PositionState>(bot).Value);

            simulation.RunTick(NoTimeLimit());

            Assert.AreEqual(1, NavigationReducer.InvocationCount);
            Assert.AreEqual(1, simulation.Get<PositionState>(bot).Value);

            NavigationReducer.InvocationCount = 0;
            simulation.Emit(bot, new DeactivateFact());
            simulation.RunTick(NoTimeLimit());

            Assert.AreEqual(1, NavigationReducer.InvocationCount);
            Assert.IsFalse(simulation.Has<ActiveState>(bot));
            Assert.AreEqual(1, simulation.Get<PositionState>(bot).Value);

            NavigationReducer.InvocationCount = 0;
            simulation.RunTick(NoTimeLimit());

            Assert.AreEqual(0, NavigationReducer.InvocationCount);
            Assert.AreEqual(1, simulation.Get<PositionState>(bot).Value);
        }

        [Test]
        public void WorkBudgetResumesStateReducersWithoutDuplicateInvocationOrPartialCommit()
        {
            var feature = new NavigationFeature();
            var simulation = new FactSimulation(feature);
            for (var i = 0; i < 3; i++)
            {
                var entity = simulation.CreateEntity();
                simulation.SetStateSilently(entity, new ActiveState());
                simulation.SetStateSilently(entity, new BotState(1));
                simulation.SetStateSilently(entity, new PositionState(0));
            }

            var options = new ReduceOptions
            {
                MaxFacts = 100,
                MaxWorkItems = 1,
                MaxMilliseconds = 0
            };

            var calls = 0;
            SimulationResult result;
            do
            {
                calls++;
                var complete = simulation.RunTickIncremental(options, out result);
                if (!complete)
                {
                    for (var entityId = 0; entityId < 3; entityId++)
                    {
                        Assert.AreEqual(0, simulation.Get<PositionState>(new EntityRef(entityId)).Value);
                    }
                }
            }
            while (!result.Complete && calls < 10);

            Assert.IsTrue(result.Complete);
            Assert.AreEqual(4, calls);
            Assert.AreEqual(3, NavigationReducer.InvocationCount);
            Assert.AreEqual(3, result.ProcessedWorkItems);
            Assert.AreEqual(3, result.MutationCount);
            for (var entityId = 0; entityId < 3; entityId++)
            {
                Assert.AreEqual(1, simulation.Get<PositionState>(new EntityRef(entityId)).Value);
            }
        }

        [Test]
        public void WorkBudgetResumesRemainingReducersForAlreadyPoppedFact()
        {
            var feature = new ResumeFeature();
            var simulation = new FactSimulation(feature);
            var entity = simulation.CreateEntity();
            simulation.Emit(entity, new ResumeStartFact());
            var options = new ReduceOptions
            {
                MaxFacts = 100,
                MaxWorkItems = 1,
                MaxMilliseconds = 0
            };

            Assert.IsFalse(simulation.RunTickIncremental(options, out _));
            Assert.AreEqual(1, FirstResumeReducer.InvocationCount);
            Assert.AreEqual(0, SecondResumeReducer.InvocationCount);
            Assert.IsFalse(simulation.TryGet<ResumeState>(entity, out _));

            Assert.IsFalse(simulation.RunTickIncremental(options, out _));
            Assert.AreEqual(1, FirstResumeReducer.InvocationCount);
            Assert.AreEqual(1, SecondResumeReducer.InvocationCount);
            Assert.IsFalse(simulation.TryGet<ResumeState>(entity, out _));

            Assert.IsTrue(simulation.RunTickIncremental(options, out var complete));
            Assert.IsTrue(complete.Complete);
            Assert.AreEqual(2, simulation.Get<ResumeState>(entity).Count);
            Assert.AreEqual(1, complete.MutationCount);
        }

        [Test]
        public void FailedFullTickRollsBackReducerSideDestruction()
        {
            var feature = new LifecycleFeature();
            var simulation = new FactSimulation(feature);
            var entity = simulation.CreateEntity();
            simulation.SetStateSilently(entity, new LifecycleState(7));
            simulation.Emit(entity, new DestroyRequestedFact());

            Assert.Throws<CascadeReductionException>(
                () => simulation.RunTick(new ReduceOptions
                {
                    MaxFacts = 1,
                    MaxWorkItems = 100,
                    MaxMilliseconds = 0
                }));

            Assert.IsTrue(simulation.TryGetEntity(entity.Value, out var found));
            Assert.AreEqual(entity, found);
            Assert.IsFalse(simulation.IsDestroyed(entity));
            Assert.AreEqual(7, simulation.Get<LifecycleState>(entity).Value);
            Assert.AreEqual(0, simulation.MutationCount);
        }

        [Test]
        public void IncrementalDestructionKeepsCommittedStateUntilClosureThenPublishesOneDelete()
        {
            var feature = new LifecycleFeature();
            var simulation = new FactSimulation(feature);
            var entity = simulation.CreateEntity();
            simulation.SetStateSilently(entity, new LifecycleState(7));
            simulation.Emit(entity, new DestroyRequestedFact());
            var options = new ReduceOptions
            {
                MaxFacts = 1,
                MaxWorkItems = 100,
                MaxMilliseconds = 0
            };

            Assert.IsFalse(simulation.RunTickIncremental(options, out var incomplete));
            Assert.IsFalse(incomplete.Complete);
            Assert.IsTrue(simulation.IsDestroyed(entity));
            Assert.IsFalse(simulation.TryGetEntity(entity.Value, out _));
            Assert.AreEqual(7, simulation.Get<LifecycleState>(entity).Value);
            Assert.AreEqual(0, simulation.MutationCount);
            Assert.Throws<InvalidOperationException>(
                () => simulation.SetStateSilently(entity, new LifecycleState(9)));

            SimulationResult complete;
            while (!simulation.RunTickIncremental(options, out complete))
            {
                Assert.AreEqual(7, simulation.Get<LifecycleState>(entity).Value);
                Assert.AreEqual(0, simulation.MutationCount);
            }

            Assert.IsTrue(complete.Complete);
            Assert.IsFalse(simulation.TryGet<LifecycleState>(entity, out _));
            Assert.AreEqual(1, complete.MutationCount);
            Assert.AreEqual(1, DestroyFollowupReducer.InvocationCount);
            Assert.AreEqual(1, DeadReducer.InvocationCount);
            Assert.AreEqual(1, LifecycleStateReducer.InvocationCount);
            Assert.IsTrue(DestroyFollowupReducer.SawDeadFact);
            Assert.IsTrue(LifecycleStateReducer.SawDeadFact);

            var deletes = 0;
            simulation.ForEachMutation(
                feature.Lifecycle,
                (EntityRef changedEntity, in StateMutation<LifecycleState> mutation) =>
                {
                    deletes++;
                    Assert.AreEqual(entity, changedEntity);
                    Assert.IsTrue(mutation.HadPrevious);
                    Assert.AreEqual(7, mutation.Previous.Value);
                    Assert.IsFalse(mutation.HasNext);
                });
            Assert.AreEqual(1, deletes);
        }

        [Test]
        public void EmittingDeadFactRunsLifecycleReducersBeforeDeletingDurableState()
        {
            var feature = new LifecycleFeature();
            var simulation = new FactSimulation(feature);
            var entity = simulation.CreateEntity();
            simulation.SetStateSilently(entity, new LifecycleState(7));

            simulation.Emit(entity, new DeadFact());

            Assert.IsTrue(simulation.IsDestroyed(entity));
            Assert.IsFalse(simulation.TryGetEntity(entity.Value, out _));
            Assert.AreEqual(7, simulation.Get<LifecycleState>(entity).Value);

            var result = simulation.RunTick(NoTimeLimit());

            Assert.IsTrue(result.Complete);
            Assert.AreEqual(1, DeadReducer.InvocationCount);
            Assert.AreEqual(1, LifecycleStateReducer.InvocationCount);
            Assert.IsTrue(LifecycleStateReducer.SawDeadFact);
            Assert.IsFalse(simulation.TryGet<LifecycleState>(entity, out _));
            Assert.AreEqual(1, result.MutationCount);
        }

        [Test]
        public void UnifiedSettingsBoundConcurrentEntitiesAndReuseGenerationalIds()
        {
            var settings = new CascadeSettings(
                maxEntities: 1,
                maxFactsPerEntity: 4,
                maxFactsPerTypePerEntity: 1)
            {
                MaxWorkItemsPerStep = 16,
                MaxWorkItemsPerTick = 32,
                MaxPasses = 8,
                MaxMillisecondsPerStep = 0,
                MaxCausalDepth = 4
            };
            var simulation = new FactSimulation(new LifecycleFeature(), settings);
            var first = simulation.CreateEntity();
            simulation.SetStateSilently(first, new LifecycleState(1));

            Assert.Throws<InvalidOperationException>(() => simulation.CreateEntity());

            simulation.DestroyEntity(first);
            simulation.RunTick();
            var boundedCapacity = simulation.CaptureCapacitySnapshot(settings.MaxEntities);
            Assert.AreEqual(
                settings.MaxEntities,
                boundedCapacity.MinimumFactBucketEntityCapacity);
            Assert.AreEqual(
                settings.MaxFactsPerTypePerEntity,
                boundedCapacity.MinimumFactListCapacity);
            var previous = first;

            for (var i = 0; i < 32; i++)
            {
                var entity = simulation.CreateEntity();
                Assert.AreEqual(first.Value, entity.Value);
                Assert.Greater(entity.Generation, previous.Generation);
                Assert.AreNotEqual(previous, entity);
                Assert.IsTrue(simulation.IsDestroyed(previous));
                Assert.IsTrue(simulation.TryGetEntity(entity.Value, out var currentById));
                Assert.AreEqual(entity, currentById);
                simulation.SetStateSilently(entity, new LifecycleState(i + 2));
                simulation.DestroyEntity(entity);
                simulation.RunTick();
                previous = entity;
            }

            Assert.AreEqual(
                boundedCapacity,
                simulation.CaptureCapacitySnapshot(settings.MaxEntities));

            var current = simulation.CreateEntity();
            Assert.AreEqual(first.Value, current.Value);
            Assert.Greater(current.Generation, previous.Generation);
            simulation.SetStateSilently(current, new LifecycleState(99));
            simulation.Emit(first, new DeadFact());
            var staleResult = simulation.RunTick();

            Assert.AreEqual(1, staleResult.RejectedDestroyedEntityFacts);
            Assert.IsFalse(simulation.IsDestroyed(current));
            Assert.AreEqual(99, simulation.Get<LifecycleState>(current).Value);
        }

        [Test]
        public void UnifiedSettingsEnforceCumulativeTickWorkAcrossIncrementalSteps()
        {
            var settings = new CascadeSettings(
                maxEntities: 1,
                maxFactsPerEntity: 3,
                maxFactsPerTypePerEntity: 1)
            {
                MaxWorkItemsPerStep = 1,
                MaxWorkItemsPerTick = 1,
                MaxPasses = 4,
                MaxMillisecondsPerStep = 0,
                MaxCausalDepth = 4
            };
            var simulation = new FactSimulation(new ResumeFeature(), settings);
            var entity = simulation.CreateEntity();
            simulation.Emit(entity, new ResumeStartFact());

            Assert.IsFalse(simulation.RunTickIncremental(out var incomplete));
            Assert.IsFalse(incomplete.Complete);

            var exception = Assert.Throws<CascadeReductionException>(
                () => simulation.RunTickIncremental(out _));

            Assert.AreEqual("maximum tick work item count exceeded", exception!.BudgetReason);
            Assert.IsFalse(simulation.TryGet<ResumeState>(entity, out _));
        }

        [Test]
        public void ReducerCreatedEntityCanReceiveFactsAndCommitStateInSameTick()
        {
            var feature = new LifecycleFeature();
            var simulation = new FactSimulation(feature);
            var parent = simulation.CreateEntity();
            simulation.Emit(parent, new SpawnRequestedFact(11));

            var result = simulation.RunTick(NoTimeLimit());
            var child = SpawnReducer.CreatedEntity;

            Assert.IsTrue(result.Complete);
            Assert.IsTrue(simulation.TryGetEntity(child.Value, out var found));
            Assert.AreEqual(child, found);
            Assert.AreEqual(11, simulation.Get<ChildState>(child).Value);
        }

        [Test]
        public void FailedTickInvalidatesReducerCreatedEntityGeneration()
        {
            var feature = new LifecycleFeature();
            var simulation = new FactSimulation(feature);
            var parent = simulation.CreateEntity();
            simulation.Emit(parent, new SpawnRequestedFact(11));

            Assert.Throws<CascadeReductionException>(
                () => simulation.RunTick(new ReduceOptions
                {
                    MaxFacts = 1,
                    MaxWorkItems = 100,
                    MaxMilliseconds = 0
                }));

            var child = SpawnReducer.CreatedEntity;
            Assert.IsFalse(simulation.TryGetEntity(child.Value, out _));
            Assert.IsTrue(simulation.IsDestroyed(child));
            Assert.IsFalse(simulation.TryGet<ChildState>(child, out _));

            var replacement = simulation.CreateEntity();
            Assert.AreEqual(child.Value, replacement.Value);
            Assert.Greater(replacement.Generation, child.Generation);
            Assert.AreNotEqual(child, replacement);
            Assert.IsTrue(simulation.IsDestroyed(child));
        }

        [Test]
        public void WarmedStateTriggerPathAllocatesZeroBytesAtSteadyState()
        {
            const int entityCount = 512;
            var feature = new NavigationFeature();
            var simulation = new FactSimulation(feature);
            simulation.Warmup(new WarmupCapacityHints
            {
                EntityCapacity = entityCount,
                FactQueueCapacity = entityCount,
                FactsPerEntityPerTypeCapacity = 1,
                QueryEntityCapacity = entityCount,
                TransactionEntityCapacity = entityCount,
                BatchEntityCapacity = entityCount,
                CommitActionCapacity = entityCount,
                OutputStateCapacityPerOutput = entityCount,
                MutationCapacityPerOutput = entityCount,
                FactListCapacityMode = FactListCapacityMode.Fixed
            });

            for (var i = 0; i < entityCount; i++)
            {
                var entity = simulation.CreateEntity();
                simulation.SetStateSilently(entity, new ActiveState());
                simulation.SetStateSilently(entity, new BotState(1));
                simulation.SetStateSilently(entity, new PositionState(0));
            }

            var options = NoTimeLimit();
            var firstUseBytes = MeasureAllocatedBytes(() => simulation.RunTick(options));
            var steadyStateBytes = MeasureAllocatedBytes(() => simulation.RunTick(options));

            TestContext.WriteLine(
                $"State-trigger allocation measurement for {entityCount} entities: first-use={firstUseBytes} bytes, steady-state={steadyStateBytes} bytes.");
            Assert.GreaterOrEqual(firstUseBytes, 0);
            Assert.AreEqual(0, steadyStateBytes);
        }

        private static ReduceOptions NoTimeLimit()
            => new ReduceOptions
            {
                MaxMilliseconds = 0
            };

        private static long MeasureAllocatedBytes(Action action)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            action();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }

        private sealed class NavigationFeature : FactFeature
        {
            public NavigationFeature()
            {
                ReduceState<ActiveState, NavigationReducer>();

                Active = Output<ActiveState>("Active")
                    .AffectedBy<ActivateFact>(0)
                    .AffectedBy<DeactivateFact>(0)
                    .ConflictPolicy(CommitConflictPolicy.FoldAll)
                    .CommitWith<ActiveCommitter>();

                Bot = Output<BotState>("Bot")
                    .AffectedBy<BootstrapFact>(0)
                    .CommitWith<BotCommitter>();

                Blocker = Output<BlockerState>("Blocker")
                    .AffectedBy<BootstrapFact>(0)
                    .CommitWith<BlockerCommitter>();

                Position = Output<PositionState>("Position")
                    .AffectedBy<MoveResolvedFact>(0)
                    .ConflictPolicy(CommitConflictPolicy.FoldAll)
                    .CommitWith<PositionCommitter>();
            }

            public OutputState<ActiveState> Active { get; }
            public OutputState<BotState> Bot { get; }
            public OutputState<BlockerState> Blocker { get; }
            public OutputState<PositionState> Position { get; }
        }

        private sealed class MissingStateOutputFeature : FactFeature
        {
            public MissingStateOutputFeature()
            {
                ReduceState<ActiveState, NavigationReducer>();
            }
        }

        private sealed class ModularStateFeature : FactFeature
        {
            public ModularStateFeature()
            {
                PresenceOnlyReducer.InvocationCount = 0;
                SubFeature(new StateReducerSubFeature());
                SubFeature(new StateOutputSubFeature());
            }
        }

        private sealed class StateReducerSubFeature : FactFeature
        {
            public StateReducerSubFeature()
            {
                ReduceState<ActiveState, PresenceOnlyReducer>();
            }
        }

        private sealed class StateOutputSubFeature : FactFeature
        {
            public StateOutputSubFeature()
            {
                Output<ActiveState>("ModularActive")
                    .AffectedBy<BootstrapFact>(0)
                    .CommitWith<ActiveNoopCommitter>();
            }
        }

        private sealed class PresenceOnlyReducer : ITransactionalReducer
        {
            internal static int InvocationCount;

            public void Reduce(IReduceContext ctx, EntityRef entity)
            {
                ctx.GetState<ActiveState>(entity);
                InvocationCount++;
            }
        }

        private sealed class NavigationReducer : ITransactionalReducer
        {
            internal static int InvocationCount;
            internal static int LastBlockerCount;

            public void Reduce(IReduceContext ctx, EntityRef entity)
            {
                InvocationCount++;
                ctx.GetState<ActiveState>(entity);

                if (ctx.Facts(entity).Has<DeactivateFact>())
                {
                    return;
                }

                if (!ctx.TryGetState<BotState>(entity, out var bot)
                    || !ctx.TryGetState<PositionState>(entity, out var position))
                {
                    return;
                }

                LastBlockerCount = ctx.Query.With<BlockerState>().Count;
                ctx.Emit(entity, new MoveResolvedFact(position.Value + bot.Step + LastBlockerCount));
            }
        }

        private sealed class ActiveCommitter : IOutputCommitter<ActiveState>
        {
            public CommitDecision<ActiveState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<ActiveState> previous)
            {
                var facts = ctx.Facts(entity);
                if (facts.Has<DeactivateFact>())
                {
                    return CommitDecision<ActiveState>.Delete();
                }

                return facts.Has<ActivateFact>()
                    ? CommitDecision<ActiveState>.Set(new ActiveState())
                    : CommitDecision<ActiveState>.Unchanged();
            }
        }

        private sealed class ActiveNoopCommitter : IOutputCommitter<ActiveState>
        {
            public CommitDecision<ActiveState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<ActiveState> previous)
                => CommitDecision<ActiveState>.Unchanged();
        }

        private sealed class BotCommitter : IOutputCommitter<BotState>
        {
            public CommitDecision<BotState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<BotState> previous)
                => CommitDecision<BotState>.Unchanged();
        }

        private sealed class BlockerCommitter : IOutputCommitter<BlockerState>
        {
            public CommitDecision<BlockerState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<BlockerState> previous)
                => CommitDecision<BlockerState>.Unchanged();
        }

        private sealed class PositionCommitter : IOutputCommitter<PositionState>
        {
            public CommitDecision<PositionState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<PositionState> previous)
            {
                return ctx.Facts(entity).TryGetLatest<MoveResolvedFact>(out var resolved)
                    ? CommitDecision<PositionState>.Set(new PositionState(resolved.Value))
                    : CommitDecision<PositionState>.Unchanged();
            }
        }

        private sealed class LifecycleFeature : FactFeature
        {
            public LifecycleFeature()
            {
                Reduce<DestroyRequestedFact>()
                    .With<DestroyReducer>();

                Reduce<DestroyFollowupFact>()
                    .With<DestroyFollowupReducer>();

                Reduce<DeadFact>()
                    .With<DeadReducer>();

                ReduceState<LifecycleState, LifecycleStateReducer>();

                Reduce<SpawnRequestedFact>()
                    .With<SpawnReducer>();

                Lifecycle = Output<LifecycleState>("Lifecycle")
                    .AffectedBy<BootstrapFact>(0)
                    .AffectedBy<DestroyFollowupFact>(0)
                    .CommitWith<LifecycleCommitter>();

                Child = Output<ChildState>("Child")
                    .AffectedBy<ChildCreatedFact>(0)
                    .CommitWith<ChildCommitter>();
            }

            public OutputState<LifecycleState> Lifecycle { get; }
            public OutputState<ChildState> Child { get; }
        }

        private sealed class ResumeFeature : FactFeature
        {
            public ResumeFeature()
            {
                Reduce<ResumeStartFact>()
                    .With<FirstResumeReducer>()
                    .With<SecondResumeReducer>();

                Result = Output<ResumeState>("Resume")
                    .AffectedBy<ResumeFirstFact>(0)
                    .AffectedBy<ResumeSecondFact>(0)
                    .CommitWith<ResumeCommitter>();
            }

            public OutputState<ResumeState> Result { get; }
        }

        private sealed class DestroyReducer : IFactReducer<DestroyRequestedFact>
        {
            public void Reduce(IReduceContext ctx, EntityRef entity, in DestroyRequestedFact fact)
            {
                ctx.DestroyEntity(entity);
                ctx.Emit(entity, new DestroyFollowupFact());
            }
        }

        private sealed class DestroyFollowupReducer : IFactReducer<DestroyFollowupFact>
        {
            internal static int InvocationCount;
            internal static bool SawDeadFact;

            public void Reduce(IReduceContext ctx, EntityRef entity, in DestroyFollowupFact fact)
            {
                InvocationCount++;
                SawDeadFact = ctx.Facts(entity).Has<DeadFact>();
            }
        }

        private sealed class DeadReducer : IFactReducer<DeadFact>
        {
            internal static int InvocationCount;

            public void Reduce(IReduceContext ctx, EntityRef entity, in DeadFact fact)
            {
                InvocationCount++;
                ctx.GetState<LifecycleState>(entity);
            }
        }

        private sealed class LifecycleStateReducer : ITransactionalReducer
        {
            internal static int InvocationCount;
            internal static bool SawDeadFact;

            public void Reduce(IReduceContext ctx, EntityRef entity)
            {
                InvocationCount++;
                SawDeadFact = ctx.Facts(entity).Has<DeadFact>();
                ctx.GetState<LifecycleState>(entity);
            }
        }

        private sealed class SpawnReducer : IFactReducer<SpawnRequestedFact>
        {
            internal static EntityRef CreatedEntity;

            public void Reduce(IReduceContext ctx, EntityRef entity, in SpawnRequestedFact fact)
            {
                CreatedEntity = ctx.CreateEntity();
                ctx.Emit(CreatedEntity, new ChildCreatedFact(fact.Value));
            }
        }

        private sealed class LifecycleCommitter : IOutputCommitter<LifecycleState>
        {
            public CommitDecision<LifecycleState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<LifecycleState> previous)
            {
                return ctx.Facts(entity).Has<DestroyFollowupFact>()
                    ? CommitDecision<LifecycleState>.Set(new LifecycleState(99))
                    : CommitDecision<LifecycleState>.Unchanged();
            }
        }

        private sealed class ChildCommitter : IOutputCommitter<ChildState>
        {
            public CommitDecision<ChildState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<ChildState> previous)
            {
                return ctx.Facts(entity).TryGetLatest<ChildCreatedFact>(out var created)
                    ? CommitDecision<ChildState>.Set(new ChildState(created.Value))
                    : CommitDecision<ChildState>.Unchanged();
            }
        }

        private sealed class FirstResumeReducer : IFactReducer<ResumeStartFact>
        {
            internal static int InvocationCount;

            public void Reduce(IReduceContext ctx, EntityRef entity, in ResumeStartFact fact)
            {
                InvocationCount++;
                ctx.Emit(entity, new ResumeFirstFact());
            }
        }

        private sealed class SecondResumeReducer : IFactReducer<ResumeStartFact>
        {
            internal static int InvocationCount;

            public void Reduce(IReduceContext ctx, EntityRef entity, in ResumeStartFact fact)
            {
                InvocationCount++;
                ctx.Emit(entity, new ResumeSecondFact());
            }
        }

        private sealed class ResumeCommitter : IOutputCommitter<ResumeState>
        {
            public CommitDecision<ResumeState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<ResumeState> previous)
            {
                var facts = ctx.Facts(entity);
                var count = facts.Has<ResumeFirstFact>() ? 1 : 0;
                count += facts.Has<ResumeSecondFact>() ? 1 : 0;
                return CommitDecision<ResumeState>.Set(new ResumeState(count));
            }
        }

        private readonly struct ActiveState : IOutputState, IEquatable<ActiveState>
        {
            public bool Equals(ActiveState other) => true;
            public override bool Equals(object? obj) => obj is ActiveState;
            public override int GetHashCode() => 0;
        }

        private readonly struct BotState : IOutputState, IEquatable<BotState>
        {
            public BotState(int step) => Step = step;
            public int Step { get; }
            public bool Equals(BotState other) => Step == other.Step;
            public override bool Equals(object? obj) => obj is BotState other && Equals(other);
            public override int GetHashCode() => Step;
        }

        private readonly struct BlockerState : IOutputState, IEquatable<BlockerState>
        {
            public bool Equals(BlockerState other) => true;
            public override bool Equals(object? obj) => obj is BlockerState;
            public override int GetHashCode() => 0;
        }

        private readonly struct PositionState : IOutputState, IEquatable<PositionState>
        {
            public PositionState(int value) => Value = value;
            public int Value { get; }
            public bool Equals(PositionState other) => Value == other.Value;
            public override bool Equals(object? obj) => obj is PositionState other && Equals(other);
            public override int GetHashCode() => Value;
        }

        private readonly struct LifecycleState : IOutputState, IEquatable<LifecycleState>
        {
            public LifecycleState(int value) => Value = value;
            public int Value { get; }
            public bool Equals(LifecycleState other) => Value == other.Value;
            public override bool Equals(object? obj) => obj is LifecycleState other && Equals(other);
            public override int GetHashCode() => Value;
        }

        private readonly struct ChildState : IOutputState, IEquatable<ChildState>
        {
            public ChildState(int value) => Value = value;
            public int Value { get; }
            public bool Equals(ChildState other) => Value == other.Value;
            public override bool Equals(object? obj) => obj is ChildState other && Equals(other);
            public override int GetHashCode() => Value;
        }

        private readonly struct ResumeState : IOutputState, IEquatable<ResumeState>
        {
            public ResumeState(int count) => Count = count;
            public int Count { get; }
            public bool Equals(ResumeState other) => Count == other.Count;
            public override bool Equals(object? obj) => obj is ResumeState other && Equals(other);
            public override int GetHashCode() => Count;
        }

        private readonly struct ActivateFact : IFact, IEquatable<ActivateFact>
        {
            public bool Equals(ActivateFact other) => true;
            public override bool Equals(object? obj) => obj is ActivateFact;
            public override int GetHashCode() => 0;
            public void Dispose() { }
        }

        private readonly struct DeactivateFact : IFact, IEquatable<DeactivateFact>
        {
            public bool Equals(DeactivateFact other) => true;
            public override bool Equals(object? obj) => obj is DeactivateFact;
            public override int GetHashCode() => 0;
            public void Dispose() { }
        }

        private readonly struct MoveResolvedFact : IFact, IEquatable<MoveResolvedFact>
        {
            public MoveResolvedFact(int value) => Value = value;
            public int Value { get; }
            public bool Equals(MoveResolvedFact other) => Value == other.Value;
            public override bool Equals(object? obj) => obj is MoveResolvedFact other && Equals(other);
            public override int GetHashCode() => Value;
            public void Dispose() { }
        }

        private readonly struct BootstrapFact : IFact, IEquatable<BootstrapFact>
        {
            public bool Equals(BootstrapFact other) => true;
            public override bool Equals(object? obj) => obj is BootstrapFact;
            public override int GetHashCode() => 0;
            public void Dispose() { }
        }

        private readonly struct DestroyRequestedFact : IFact, IEquatable<DestroyRequestedFact>
        {
            public bool Equals(DestroyRequestedFact other) => true;
            public override bool Equals(object? obj) => obj is DestroyRequestedFact;
            public override int GetHashCode() => 0;
            public void Dispose() { }
        }

        private readonly struct DestroyFollowupFact : IFact, IEquatable<DestroyFollowupFact>
        {
            public bool Equals(DestroyFollowupFact other) => true;
            public override bool Equals(object? obj) => obj is DestroyFollowupFact;
            public override int GetHashCode() => 0;
            public void Dispose() { }
        }

        private readonly struct SpawnRequestedFact : IFact, IEquatable<SpawnRequestedFact>
        {
            public SpawnRequestedFact(int value) => Value = value;
            public int Value { get; }
            public bool Equals(SpawnRequestedFact other) => Value == other.Value;
            public override bool Equals(object? obj) => obj is SpawnRequestedFact other && Equals(other);
            public override int GetHashCode() => Value;
            public void Dispose() { }
        }

        private readonly struct ChildCreatedFact : IFact, IEquatable<ChildCreatedFact>
        {
            public ChildCreatedFact(int value) => Value = value;
            public int Value { get; }
            public bool Equals(ChildCreatedFact other) => Value == other.Value;
            public override bool Equals(object? obj) => obj is ChildCreatedFact other && Equals(other);
            public override int GetHashCode() => Value;
            public void Dispose() { }
        }

        private readonly struct ResumeStartFact : IFact, IEquatable<ResumeStartFact>
        {
            public bool Equals(ResumeStartFact other) => true;
            public override bool Equals(object? obj) => obj is ResumeStartFact;
            public override int GetHashCode() => 0;
            public void Dispose() { }
        }

        private readonly struct ResumeFirstFact : IFact, IEquatable<ResumeFirstFact>
        {
            public bool Equals(ResumeFirstFact other) => true;
            public override bool Equals(object? obj) => obj is ResumeFirstFact;
            public override int GetHashCode() => 0;
            public void Dispose() { }
        }

        private readonly struct ResumeSecondFact : IFact, IEquatable<ResumeSecondFact>
        {
            public bool Equals(ResumeSecondFact other) => true;
            public override bool Equals(object? obj) => obj is ResumeSecondFact;
            public override int GetHashCode() => 0;
            public void Dispose() { }
        }
    }
}
