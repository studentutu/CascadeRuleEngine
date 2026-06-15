#nullable enable

using System;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    public sealed class FactSimulationTransactionalReducerTests
    {
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

        private sealed class EntityPairFeature : FactFeature
        {
            public EntityPairFeature()
            {
                ReduceWhen<EntityPairLeftFact, EntityPairRightFact>()
                    .With<EntityPairReducer>();

                Result = Output<EntityPairResultState>("EntityPairResult")
                    .AffectedBy<EntityPairResolvedFact>()
                    .CommitWith<EntityPairCommitter>();
            }

            public OutputState<EntityPairResultState> Result { get; }
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
                    .AffectedBy<BatchOnlyResultFact>()
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
                    .AffectedBy<ClosureObservedFact>()
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
    }
}
