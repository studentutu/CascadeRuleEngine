#nullable enable

using System;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    public sealed class FactSimulationTransactionalReducerTests
    {
        [Test]
        public void GenericAndExtendedTransactionalRegistrationStoresRequiredFacts()
        {
            var feature = new ArityFeature();
            try
            {
                Assert.AreEqual(3, feature.Registry.TransactionalReducers.Count);
                Assert.AreEqual(3, feature.Registry.TransactionalReducers[0].RequiredFactIds.Length);
                Assert.AreEqual(4, feature.Registry.TransactionalReducers[1].RequiredFactIds.Length);
                Assert.AreEqual(5, feature.Registry.TransactionalReducers[2].RequiredFactIds.Length);

                Assert.AreEqual(3, feature.Registry.BatchTransactionalReducers.Count);
                Assert.AreEqual(3, feature.Registry.BatchTransactionalReducers[0].RequiredFactIds.Length);
                Assert.AreEqual(4, feature.Registry.BatchTransactionalReducers[1].RequiredFactIds.Length);
                Assert.AreEqual(5, feature.Registry.BatchTransactionalReducers[2].RequiredFactIds.Length);
            }
            finally
            {
                feature.Dispose();
            }
        }

        [Test]
        public void EntityScopedTransactionalReducerRunsOnceWhenTwoRequiredFactsExist()
        {
            EntityPairReducer.Reset();

            var feature = new EntityPairFeature();
            var simulation = new FactSimulation(feature);
            var entity = simulation.CreateEntity();

            simulation.Emit(entity, new EntityPairLeftFact(4));
            simulation.Emit(entity, new EntityPairRightFact(5));

            var result = simulation.RunTick(new ReduceOptions
            {
                MaxMilliseconds = 0
            });

            Assert.IsTrue(result.Complete);
            Assert.AreEqual(3, result.AcceptedFacts);
            Assert.AreEqual(0, result.ReducerInvocations);
            Assert.AreEqual(1, result.TransactionalReducerInvocations);
            Assert.AreEqual(1, result.MutationCount);
            Assert.AreEqual(1, EntityPairReducer.InvocationCount);
            Assert.IsTrue(simulation.TryGet<EntityPairResultState>(entity, out var state));
            Assert.AreEqual(9, state.Value);
        }

        [Test]
        public void BatchTransactionalReducerReceivesOnlyEligibleEntities()
        {
            BatchOnlyEligibleReducer.Reset();

            var feature = new BatchOnlyFeature();
            var simulation = new FactSimulation(feature);
            var first = simulation.CreateEntity();
            var incomplete = simulation.CreateEntity();
            var second = simulation.CreateEntity();

            simulation.Emit(first, new BatchOnlyLeftFact(10));
            simulation.Emit(first, new BatchOnlyRightFact(100));
            simulation.Emit(incomplete, new BatchOnlyLeftFact(20));
            simulation.Emit(second, new BatchOnlyLeftFact(30));
            simulation.Emit(second, new BatchOnlyRightFact(300));

            var result = simulation.RunTick(new ReduceOptions
            {
                MaxMilliseconds = 0
            });

            Assert.IsTrue(result.Complete);
            Assert.AreEqual(7, result.AcceptedFacts);
            Assert.AreEqual(0, result.ReducerInvocations);
            Assert.AreEqual(1, result.TransactionalReducerInvocations);
            Assert.AreEqual(2, result.MutationCount);
            Assert.AreEqual(1, BatchOnlyEligibleReducer.CallCount);
            Assert.AreEqual(2, BatchOnlyEligibleReducer.LastEntityCount);

            Assert.IsTrue(simulation.TryGet<BatchOnlyResultState>(first, out var firstState));
            Assert.AreEqual(2, firstState.BatchSize);
            Assert.AreEqual(110, firstState.Value);

            Assert.IsFalse(simulation.TryGet<BatchOnlyResultState>(incomplete, out var incompleteState));
            Assert.AreEqual(default(BatchOnlyResultState), incompleteState);

            Assert.IsTrue(simulation.TryGet<BatchOnlyResultState>(second, out var secondState));
            Assert.AreEqual(2, secondState.BatchSize);
            Assert.AreEqual(330, secondState.Value);
        }

        [Test]
        public void BatchTransactionalReducerFiresOncePerEntityWhenEntitiesBecomeEligibleOnDifferentPasses()
        {
            ClosureBatchReducer.Reset();
            DelayedRightBatchReducer.Reset();

            var feature = new DelayedClosureFeature();
            var simulation = new FactSimulation(feature);
            var onePass = simulation.CreateEntity();
            var twoPass = simulation.CreateEntity();
            var incomplete = simulation.CreateEntity();

            simulation.Emit(onePass, new OnePassInputFact(1));
            simulation.Emit(twoPass, new TwoPassInputFact(2));
            simulation.Emit(twoPass, new DelayedRightMarkerFact(2));
            simulation.Emit(incomplete, new TwoPassInputFact(3));

            var result = simulation.RunTick(new ReduceOptions
            {
                MaxMilliseconds = 0
            });

            Assert.IsTrue(result.Complete);
            Assert.AreEqual(11, result.AcceptedFacts);
            Assert.AreEqual(3, result.ReducerInvocations);
            Assert.AreEqual(3, result.TransactionalReducerInvocations);
            Assert.AreEqual(2, result.MutationCount);

            Assert.AreEqual(2, ClosureBatchReducer.CallCount);
            Assert.AreEqual(1, ClosureBatchReducer.EntityCountForCall(0));
            Assert.AreEqual(onePass.Value, ClosureBatchReducer.EntityForCall(0, 0));
            Assert.AreEqual(1, ClosureBatchReducer.EntityCountForCall(1));
            Assert.AreEqual(twoPass.Value, ClosureBatchReducer.EntityForCall(1, 0));

            Assert.AreEqual(1, DelayedRightBatchReducer.CallCount);
            Assert.AreEqual(1, DelayedRightBatchReducer.LastEntityCount);
            Assert.AreEqual(twoPass.Value, DelayedRightBatchReducer.LastEntity);

            Assert.IsTrue(simulation.TryGet<ClosureResultState>(onePass, out var onePassState));
            Assert.AreEqual(1, onePassState.BatchCall);
            Assert.AreEqual(1, onePassState.RightStrategy);

            Assert.IsTrue(simulation.TryGet<ClosureResultState>(twoPass, out var twoPassState));
            Assert.AreEqual(2, twoPassState.BatchCall);
            Assert.AreEqual(2, twoPassState.RightStrategy);

            Assert.IsFalse(simulation.TryGet<ClosureResultState>(incomplete, out var incompleteState));
            Assert.AreEqual(default(ClosureResultState), incompleteState);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void ForbiddenFactSuppressesNegativeReducerRegardlessOfArrivalOrder(
            bool forbiddenFirst)
        {
            NegativeRuleReducer.InvocationCount = 0;
            var simulation = new FactSimulation(new NegativeRuleFeature());
            var entity = simulation.CreateEntity();

            if (forbiddenFirst)
            {
                simulation.Emit(entity, new NegativeBlockedFact());
            }

            simulation.Emit(entity, new NegativeLeftFact(4));

            if (!forbiddenFirst)
            {
                simulation.Emit(entity, new NegativeBlockedFact());
            }

            var result = simulation.RunTick(new ReduceOptions
            {
                MaxMilliseconds = 0
            });

            Assert.IsTrue(result.Complete);
            Assert.AreEqual(0, NegativeRuleReducer.InvocationCount);
            Assert.AreEqual(0, result.TransactionalReducerInvocations);
            Assert.AreEqual(0, result.ReducerInvocations);
            Assert.IsFalse(simulation.TryGet<NegativeResultState>(entity, out _));
        }

        [Test]
        public void NegativeReducerRunsAfterPositiveClosureWhenForbiddenFactIsAbsent()
        {
            NegativeRuleReducer.InvocationCount = 0;
            var simulation = new FactSimulation(new NegativeRuleFeature());
            var entity = simulation.CreateEntity();

            simulation.Emit(entity, new NegativeLeftFact(4));

            var result = simulation.RunTick(new ReduceOptions
            {
                MaxMilliseconds = 0
            });

            Assert.IsTrue(result.Complete);
            Assert.AreEqual(1, NegativeRuleReducer.InvocationCount);
            Assert.AreEqual(1, result.ReducerInvocations);
            Assert.AreEqual(0, result.TransactionalReducerInvocations);
            Assert.AreEqual(4, simulation.Get<NegativeResultState>(entity).Value);
        }

        [Test]
        public void ForbiddenFactDerivedByLaterPositiveReducerSuppressesNegativeReducer()
        {
            NegativeRuleReducer.InvocationCount = 0;
            DerivedForbiddenFactReducer.InvocationCount = 0;
            var simulation = new FactSimulation(new DerivedForbiddenFactFeature());
            var entity = simulation.CreateEntity();

            simulation.Emit(entity, new NegativeLeftFact(4));

            var result = simulation.RunTick(new ReduceOptions
            {
                MaxMilliseconds = 0
            });

            Assert.IsTrue(result.Complete);
            Assert.AreEqual(1, DerivedForbiddenFactReducer.InvocationCount);
            Assert.AreEqual(0, NegativeRuleReducer.InvocationCount);
            Assert.AreEqual(1, result.ReducerInvocations);
            Assert.IsFalse(simulation.TryGet<NegativeResultState>(entity, out _));
        }

        [Test]
        public void NegativeReducerPreservesDistinctTriggerFactMultiplicity()
        {
            NegativeRuleReducer.InvocationCount = 0;
            var simulation = new FactSimulation(new NegativeRuleFeature());
            var entity = simulation.CreateEntity();

            simulation.Emit(entity, new NegativeLeftFact(4));
            simulation.Emit(entity, new NegativeLeftFact(5));

            var result = simulation.RunTick(new ReduceOptions
            {
                MaxMilliseconds = 0
            });

            Assert.IsTrue(result.Complete);
            Assert.AreEqual(2, NegativeRuleReducer.InvocationCount);
            Assert.AreEqual(2, result.ReducerInvocations);
            Assert.AreEqual(5, simulation.Get<NegativeResultState>(entity).Value);
        }

        [Test]
        public void IncrementalNegativePhaseSealsLateHostInputUntilClosure()
        {
            NegativeRuleReducer.InvocationCount = 0;
            NegativeStartReducer.InvocationCount = 0;
            var simulation = new FactSimulation(new IncrementalNegativeRuleFeature());
            var entity = simulation.CreateEntity();
            simulation.Emit(entity, new NegativeLeftFact(4));
            var options = new ReduceOptions
            {
                MaxFacts = 100,
                MaxWorkItems = 1,
                MaxMilliseconds = 0
            };

            Assert.IsFalse(simulation.RunTickIncremental(options, out _));
            Assert.AreEqual(1, NegativeStartReducer.InvocationCount);
            Assert.AreEqual(0, NegativeRuleReducer.InvocationCount);

            var exception = Assert.Throws<InvalidOperationException>(
                () => simulation.Emit(entity, new NegativeBlockedFact()));
            StringAssert.Contains("input is sealed", exception.Message);

            SimulationResult result;
            do
            {
                simulation.RunTickIncremental(options, out result);
            }
            while (!result.Complete);

            Assert.AreEqual(1, NegativeRuleReducer.InvocationCount);
            Assert.AreEqual(4, simulation.Get<NegativeResultState>(entity).Value);
        }

        [Test]
        public void WorkBudgetResumesBetweenNegativeReducersWithoutDuplicateInvocation()
        {
            CountingNegativeReducer.InvocationCount = 0;
            var simulation = new FactSimulation(new IncrementalNegativeReducersFeature());
            var entity = simulation.CreateEntity();
            simulation.Emit(entity, new NegativeLeftFact(4));
            var options = new ReduceOptions
            {
                MaxFacts = 100,
                MaxWorkItems = 1,
                MaxMilliseconds = 0
            };

            Assert.IsFalse(simulation.RunTickIncremental(options, out var partial));
            Assert.AreEqual(1, partial.ReducerInvocations);
            Assert.AreEqual(1, CountingNegativeReducer.InvocationCount);

            Assert.IsTrue(simulation.RunTickIncremental(options, out var complete));
            Assert.AreEqual(2, complete.ReducerInvocations);
            Assert.AreEqual(2, CountingNegativeReducer.InvocationCount);
        }

        [Test]
        public void ContradictoryNegativeRegistrationFailsDuringFeatureConstruction()
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => new ContradictoryNegativeRuleFeature());

            StringAssert.Contains("trigger on and forbid", exception.Message);
        }

        [Test]
        public void StaleEntityCannotInjectForbiddenFactIntoReusedSlot()
        {
            NegativeRuleReducer.InvocationCount = 0;
            var simulation = new FactSimulation(new NegativeRuleFeature());
            var stale = simulation.CreateEntity();
            simulation.DestroyEntity(stale);
            simulation.RunTick(new ReduceOptions
            {
                MaxMilliseconds = 0
            });

            var current = simulation.CreateEntity();
            Assert.AreEqual(stale.Value, current.Value);
            Assert.AreNotEqual(stale.Generation, current.Generation);
            simulation.Emit(stale, new NegativeBlockedFact());

            simulation.Emit(current, new NegativeLeftFact(4));
            var result = simulation.RunTick(new ReduceOptions
            {
                MaxMilliseconds = 0
            });

            Assert.AreEqual(1, result.RejectedDestroyedEntityFacts);
            Assert.AreEqual(1, NegativeRuleReducer.InvocationCount);
            Assert.AreEqual(4, simulation.Get<NegativeResultState>(current).Value);
        }

        [Test]
        public void NegativeReducerCannotMutateAnotherNegativeCondition()
        {
            var simulation = new FactSimulation(new InvalidNegativeEmissionFeature());
            var entity = simulation.CreateEntity();
            simulation.Emit(entity, new NegativeLeftFact(4));

            var exception = Assert.Throws<CascadeReductionException>(
                () => simulation.RunTick(new ReduceOptions
                {
                    MaxMilliseconds = 0
                }));

            Assert.IsNotNull(exception.InnerException);
            StringAssert.Contains(
                "sealed Without condition",
                exception.InnerException!.Message);
        }

        private sealed class ArityFeature : FactFeature
        {
            public ArityFeature()
            {
                ReduceWhen<EntityPairLeftFact, EntityPairRightFact, OnePassInputFact>()
                    .With<ArityReducer>();

                ReduceWhen<EntityPairLeftFact, EntityPairRightFact, OnePassInputFact, TwoPassInputFact>()
                    .With<ArityReducer>();

                ReduceWhen<EntityPairLeftFact, EntityPairRightFact, OnePassInputFact, TwoPassInputFact>()
                    .And<DelayedRightMarkerFact>()
                    .With<ArityReducer>();

                ReduceBatchWhen<EntityPairLeftFact, EntityPairRightFact, OnePassInputFact>()
                    .With<ArityBatchReducer>();

                ReduceBatchWhen<EntityPairLeftFact, EntityPairRightFact, OnePassInputFact, TwoPassInputFact>()
                    .With<ArityBatchReducer>();

                ReduceBatchWhen<EntityPairLeftFact, EntityPairRightFact, OnePassInputFact, TwoPassInputFact>()
                    .And<DelayedRightMarkerFact>()
                    .With<ArityBatchReducer>();
            }
        }

        private sealed class ArityReducer : ITransactionalReducer
        {
            public void Reduce(IReduceContext ctx, EntityRef entity)
            {
            }
        }

        private sealed class ArityBatchReducer : IBatchTransactionalReducer
        {
            public void ReduceBatch(IReduceContext ctx, ReadOnlySpan<EntityRef> entities)
            {
            }
        }

        private sealed class EntityPairFeature : FactFeature
        {
            public EntityPairFeature()
            {
                ReduceWhen<EntityPairLeftFact, EntityPairRightFact>()
                    .With<EntityPairReducer>();

                Result = Output<EntityPairResultState>("EntityPairResult")
                    .AffectedBy<EntityPairResolvedFact>(0)
                    .CommitWith<EntityPairCommitter>();
            }

            public OutputState<EntityPairResultState> Result { get; }
        }

        private sealed class NegativeRuleFeature : FactFeature
        {
            public NegativeRuleFeature()
            {
                Reduce<NegativeLeftFact>()
                    .Without<NegativeBlockedFact>()
                    .With<NegativeRuleReducer>();

                Output<NegativeResultState>("NegativeResult")
                    .AffectedBy<NegativeResolvedFact>(0)
                    .CommitWith<NegativeResultCommitter>();
            }
        }

        private sealed class DerivedForbiddenFactFeature : FactFeature
        {
            public DerivedForbiddenFactFeature()
            {
                // Register the negative reducer first to prove registration order cannot bypass positive closure.
                Reduce<NegativeLeftFact>()
                    .Without<NegativeBlockedFact>()
                    .With<NegativeRuleReducer>();

                Reduce<NegativeLeftFact>()
                    .With<DerivedForbiddenFactReducer>();

                Output<NegativeResultState>("DerivedForbiddenNegativeResult")
                    .AffectedBy<NegativeResolvedFact>(0)
                    .CommitWith<NegativeResultCommitter>();
            }
        }

        private sealed class IncrementalNegativeReducersFeature : FactFeature
        {
            public IncrementalNegativeReducersFeature()
            {
                Reduce<NegativeLeftFact>()
                    .Without<NegativeBlockedFact>()
                    .With<CountingNegativeReducer>();

                Reduce<NegativeLeftFact>()
                    .Without<NegativeBlockedFact>()
                    .With<CountingNegativeReducer>();
            }
        }

        private sealed class IncrementalNegativeRuleFeature : FactFeature
        {
            public IncrementalNegativeRuleFeature()
            {
                Reduce<NegativeLeftFact>()
                    .With<NegativeStartReducer>();

                Reduce<NegativeLeftFact>()
                    .Without<NegativeBlockedFact>()
                    .With<NegativeRuleReducer>();

                Output<NegativeResultState>("IncrementalNegativeResult")
                    .AffectedBy<NegativeResolvedFact>(0)
                    .CommitWith<NegativeResultCommitter>();
            }
        }

        private sealed class ContradictoryNegativeRuleFeature : FactFeature
        {
            public ContradictoryNegativeRuleFeature()
            {
                Reduce<NegativeLeftFact>()
                    .Without<NegativeLeftFact>()
                    .With<NegativeRuleReducer>();
            }
        }

        private sealed class InvalidNegativeEmissionFeature : FactFeature
        {
            public InvalidNegativeEmissionFeature()
            {
                Reduce<NegativeLeftFact>()
                    .Without<NegativeBlockedFact>()
                    .With<InvalidNegativeEmissionReducer>();
            }
        }

        private sealed class NegativeStartReducer : IFactReducer<NegativeLeftFact>
        {
            internal static int InvocationCount;

            public void Reduce(
                IReduceContext ctx,
                EntityRef entity,
                in NegativeLeftFact fact)
            {
                InvocationCount++;
            }
        }

        private sealed class DerivedForbiddenFactReducer : IFactReducer<NegativeLeftFact>
        {
            internal static int InvocationCount;

            public void Reduce(
                IReduceContext ctx,
                EntityRef entity,
                in NegativeLeftFact fact)
            {
                InvocationCount++;
                ctx.Emit(entity, new NegativeBlockedFact());
            }
        }

        private sealed class CountingNegativeReducer : IFactReducer<NegativeLeftFact>
        {
            internal static int InvocationCount;

            public void Reduce(
                IReduceContext ctx,
                EntityRef entity,
                in NegativeLeftFact fact)
            {
                InvocationCount++;
            }
        }

        private sealed class NegativeRuleReducer : IFactReducer<NegativeLeftFact>
        {
            internal static int InvocationCount;

            public void Reduce(
                IReduceContext ctx,
                EntityRef entity,
                in NegativeLeftFact fact)
            {
                InvocationCount++;
                ctx.Emit(entity, new NegativeResolvedFact(fact.Value));
            }
        }

        private sealed class InvalidNegativeEmissionReducer :
            IFactReducer<NegativeLeftFact>
        {
            public void Reduce(
                IReduceContext ctx,
                EntityRef entity,
                in NegativeLeftFact fact)
            {
                ctx.Emit(entity, new NegativeBlockedFact());
            }
        }

        private sealed class NegativeResultCommitter :
            IOutputCommitter<NegativeResultState>
        {
            public CommitDecision<NegativeResultState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<NegativeResultState> previous)
            {
                return ctx.Facts(entity).TryGetLatest<NegativeResolvedFact>(out var fact)
                    ? CommitDecision<NegativeResultState>.Set(
                        new NegativeResultState(fact.Value))
                    : CommitDecision<NegativeResultState>.Unchanged();
            }
        }

        private sealed class EntityPairReducer : ITransactionalReducer
        {
            public static int InvocationCount { get; private set; }

            public static void Reset()
            {
                InvocationCount = 0;
            }

            public void Reduce(IReduceContext ctx, EntityRef entity)
            {
                var facts = ctx.Facts(entity);
                if (!facts.TryGetLatest<EntityPairLeftFact>(out var left)
                    || !facts.TryGetLatest<EntityPairRightFact>(out var right))
                {
                    throw new InvalidOperationException("Entity pair reducer ran without both required facts.");
                }

                InvocationCount++;
                ctx.Emit(entity, new EntityPairResolvedFact(left.Value + right.Value));
            }
        }

        private sealed class EntityPairCommitter : IOutputCommitter<EntityPairResultState>
        {
            public CommitDecision<EntityPairResultState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<EntityPairResultState> previous)
            {
                return ctx.Facts(entity).TryGetLatest<EntityPairResolvedFact>(out var fact)
                    ? CommitDecision<EntityPairResultState>.Set(new EntityPairResultState(fact.Value))
                    : CommitDecision<EntityPairResultState>.Unchanged();
            }
        }

        private sealed class BatchOnlyFeature : FactFeature
        {
            public BatchOnlyFeature()
            {
                ReduceBatchWhen<BatchOnlyLeftFact, BatchOnlyRightFact>()
                    .With<BatchOnlyEligibleReducer>();

                Result = Output<BatchOnlyResultState>("BatchOnlyResult")
                    .AffectedBy<BatchOnlyResultFact>(0)
                    .CommitWith<BatchOnlyCommitter>();
            }

            public OutputState<BatchOnlyResultState> Result { get; }
        }

        private sealed class BatchOnlyEligibleReducer : IBatchTransactionalReducer
        {
            public static int CallCount { get; private set; }
            public static int LastEntityCount { get; private set; }

            public static void Reset()
            {
                CallCount = 0;
                LastEntityCount = 0;
            }

            public void ReduceBatch(IReduceContext ctx, ReadOnlySpan<EntityRef> entities)
            {
                CallCount++;
                LastEntityCount = entities.Length;

                for (var i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    var facts = ctx.Facts(entity);
                    if (!facts.TryGetLatest<BatchOnlyLeftFact>(out var left)
                        || !facts.TryGetLatest<BatchOnlyRightFact>(out var right))
                    {
                        throw new InvalidOperationException("Batch reducer received an ineligible entity.");
                    }

                    ctx.Emit(entity, new BatchOnlyResultFact(left.Value + right.Value, entities.Length));
                }
            }
        }

        private sealed class BatchOnlyCommitter : IOutputCommitter<BatchOnlyResultState>
        {
            public CommitDecision<BatchOnlyResultState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<BatchOnlyResultState> previous)
            {
                return ctx.Facts(entity).TryGetLatest<BatchOnlyResultFact>(out var fact)
                    ? CommitDecision<BatchOnlyResultState>.Set(new BatchOnlyResultState(fact.Value, fact.BatchSize))
                    : CommitDecision<BatchOnlyResultState>.Unchanged();
            }
        }

        private sealed class DelayedClosureFeature : FactFeature
        {
            public DelayedClosureFeature()
            {
                Reduce<OnePassInputFact>()
                    .With<OnePassInputReducer>();

                Reduce<TwoPassInputFact>()
                    .With<TwoPassInputReducer>();

                ReduceBatchWhen<ClosureLeftFact, ClosureRightFact>()
                    .With<ClosureBatchReducer>();

                ReduceBatchWhen<ClosureLeftFact, DelayedRightMarkerFact>()
                    .With<DelayedRightBatchReducer>();

                Result = Output<ClosureResultState>("ClosureResult")
                    .AffectedBy<ClosureObservedFact>(0)
                    .CommitWith<ClosureCommitter>();
            }

            public OutputState<ClosureResultState> Result { get; }
        }

        private sealed class OnePassInputReducer : IFactReducer<OnePassInputFact>
        {
            public void Reduce(IReduceContext ctx, EntityRef entity, in OnePassInputFact fact)
            {
                ctx.Emit(entity, new ClosureLeftFact(fact.Value));
                ctx.Emit(entity, new ClosureRightFact(fact.Value, 1));
            }
        }

        private sealed class TwoPassInputReducer : IFactReducer<TwoPassInputFact>
        {
            public void Reduce(IReduceContext ctx, EntityRef entity, in TwoPassInputFact fact)
            {
                ctx.Emit(entity, new ClosureLeftFact(fact.Value));
            }
        }

        private sealed class ClosureBatchReducer : IBatchTransactionalReducer
        {
            private const int MaxObservedCalls = 4;
            private const int MaxEntitiesPerCall = 4;
            private static readonly int[] EntityCounts = new int[MaxObservedCalls];
            private static readonly int[,] EntitiesByCall = new int[MaxObservedCalls, MaxEntitiesPerCall];

            public static int CallCount { get; private set; }

            public static void Reset()
            {
                CallCount = 0;
                Array.Clear(EntityCounts, 0, EntityCounts.Length);
                Array.Clear(EntitiesByCall, 0, EntitiesByCall.Length);
            }

            public static int EntityCountForCall(int callIndex)
                => EntityCounts[callIndex];

            public static int EntityForCall(int callIndex, int entityIndex)
                => EntitiesByCall[callIndex, entityIndex];

            public void ReduceBatch(IReduceContext ctx, ReadOnlySpan<EntityRef> entities)
            {
                var callIndex = CallCount;
                CallCount++;
                EntityCounts[callIndex] = entities.Length;

                for (var i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    EntitiesByCall[callIndex, i] = entity.Value;
                    ctx.Emit(entity, new ClosureObservedFact(callIndex + 1));
                }
            }
        }

        private sealed class DelayedRightBatchReducer : IBatchTransactionalReducer
        {
            public static int CallCount { get; private set; }
            public static int LastEntityCount { get; private set; }
            public static int LastEntity { get; private set; }

            public static void Reset()
            {
                CallCount = 0;
                LastEntityCount = 0;
                LastEntity = -1;
            }

            public void ReduceBatch(IReduceContext ctx, ReadOnlySpan<EntityRef> entities)
            {
                CallCount++;
                LastEntityCount = entities.Length;

                for (var i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    LastEntity = entity.Value;
                    if (!ctx.Facts(entity).TryGetLatest<ClosureLeftFact>(out var left))
                    {
                        throw new InvalidOperationException("Delayed strategy reducer ran without the left fact.");
                    }

                    ctx.Emit(entity, new ClosureRightFact(left.Value, 2));
                }
            }
        }

        private sealed class ClosureCommitter : IOutputCommitter<ClosureResultState>
        {
            public CommitDecision<ClosureResultState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<ClosureResultState> previous)
            {
                var facts = ctx.Facts(entity);
                if (!facts.TryGetLatest<ClosureObservedFact>(out var observed))
                {
                    return CommitDecision<ClosureResultState>.Unchanged();
                }

                if (!facts.TryGetLatest<ClosureRightFact>(out var right))
                {
                    throw new InvalidOperationException("Closure output committed without the right fact.");
                }

                return CommitDecision<ClosureResultState>.Set(
                    new ClosureResultState(observed.BatchCall, right.Strategy));
            }
        }

        private readonly struct EntityPairLeftFact : IFact, IEquatable<EntityPairLeftFact>
        {
            public EntityPairLeftFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(EntityPairLeftFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is EntityPairLeftFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        private readonly struct EntityPairRightFact : IFact, IEquatable<EntityPairRightFact>
        {
            public EntityPairRightFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(EntityPairRightFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is EntityPairRightFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        private readonly struct EntityPairResolvedFact : IFact, IEquatable<EntityPairResolvedFact>
        {
            public EntityPairResolvedFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(EntityPairResolvedFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is EntityPairResolvedFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        private readonly struct EntityPairResultState : IOutputState, IEquatable<EntityPairResultState>
        {
            public EntityPairResultState(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(EntityPairResultState other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is EntityPairResultState other && Equals(other);

            public override int GetHashCode()
                => Value;
        }

        private readonly struct BatchOnlyLeftFact : IFact, IEquatable<BatchOnlyLeftFact>
        {
            public BatchOnlyLeftFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(BatchOnlyLeftFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is BatchOnlyLeftFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        private readonly struct BatchOnlyRightFact : IFact, IEquatable<BatchOnlyRightFact>
        {
            public BatchOnlyRightFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(BatchOnlyRightFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is BatchOnlyRightFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        private readonly struct BatchOnlyResultFact : IFact, IEquatable<BatchOnlyResultFact>
        {
            public BatchOnlyResultFact(int value, int batchSize)
            {
                Value = value;
                BatchSize = batchSize;
            }

            public int Value { get; }
            public int BatchSize { get; }

            public bool Equals(BatchOnlyResultFact other)
                => Value == other.Value && BatchSize == other.BatchSize;

            public override bool Equals(object? obj)
                => obj is BatchOnlyResultFact other && Equals(other);

            public override int GetHashCode()
                => unchecked((Value * 397) ^ BatchSize);

            public void Dispose()
            {
            }
        }

        private readonly struct BatchOnlyResultState : IOutputState, IEquatable<BatchOnlyResultState>
        {
            public BatchOnlyResultState(int value, int batchSize)
            {
                Value = value;
                BatchSize = batchSize;
            }

            public int Value { get; }
            public int BatchSize { get; }

            public bool Equals(BatchOnlyResultState other)
                => Value == other.Value && BatchSize == other.BatchSize;

            public override bool Equals(object? obj)
                => obj is BatchOnlyResultState other && Equals(other);

            public override int GetHashCode()
                => unchecked((Value * 397) ^ BatchSize);
        }

        private readonly struct OnePassInputFact : IFact, IEquatable<OnePassInputFact>
        {
            public OnePassInputFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(OnePassInputFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is OnePassInputFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        private readonly struct TwoPassInputFact : IFact, IEquatable<TwoPassInputFact>
        {
            public TwoPassInputFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(TwoPassInputFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is TwoPassInputFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        private readonly struct DelayedRightMarkerFact : IFact, IEquatable<DelayedRightMarkerFact>
        {
            public DelayedRightMarkerFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(DelayedRightMarkerFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is DelayedRightMarkerFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        private readonly struct ClosureLeftFact : IFact, IEquatable<ClosureLeftFact>
        {
            public ClosureLeftFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(ClosureLeftFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is ClosureLeftFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        private readonly struct ClosureRightFact : IFact, IEquatable<ClosureRightFact>
        {
            public ClosureRightFact(int value, int strategy)
            {
                Value = value;
                Strategy = strategy;
            }

            public int Value { get; }
            public int Strategy { get; }

            public bool Equals(ClosureRightFact other)
                => Value == other.Value && Strategy == other.Strategy;

            public override bool Equals(object? obj)
                => obj is ClosureRightFact other && Equals(other);

            public override int GetHashCode()
                => unchecked((Value * 397) ^ Strategy);

            public void Dispose()
            {
            }
        }

        private readonly struct ClosureObservedFact : IFact, IEquatable<ClosureObservedFact>
        {
            public ClosureObservedFact(int batchCall)
            {
                BatchCall = batchCall;
            }

            public int BatchCall { get; }

            public bool Equals(ClosureObservedFact other)
                => BatchCall == other.BatchCall;

            public override bool Equals(object? obj)
                => obj is ClosureObservedFact other && Equals(other);

            public override int GetHashCode()
                => BatchCall;

            public void Dispose()
            {
            }
        }

        private readonly struct ClosureResultState : IOutputState, IEquatable<ClosureResultState>
        {
            public ClosureResultState(int batchCall, int rightStrategy)
            {
                BatchCall = batchCall;
                RightStrategy = rightStrategy;
            }

            public int BatchCall { get; }
            public int RightStrategy { get; }

            public bool Equals(ClosureResultState other)
                => BatchCall == other.BatchCall && RightStrategy == other.RightStrategy;

            public override bool Equals(object? obj)
                => obj is ClosureResultState other && Equals(other);

            public override int GetHashCode()
                => unchecked((BatchCall * 397) ^ RightStrategy);
        }

        private readonly struct NegativeLeftFact : IFact<NegativeLeftFact>
        {
            public NegativeLeftFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(NegativeLeftFact other)
                => Value == other.Value;
        }

        private readonly struct NegativeBlockedFact : IFact<NegativeBlockedFact>
        {
            public bool Equals(NegativeBlockedFact other)
                => true;
        }

        private readonly struct NegativeResolvedFact : IFact<NegativeResolvedFact>
        {
            public NegativeResolvedFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(NegativeResolvedFact other)
                => Value == other.Value;
        }

        private readonly struct NegativeResultState : IOutputState<NegativeResultState>
        {
            public NegativeResultState(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(NegativeResultState other)
                => Value == other.Value;
        }
    }
}
