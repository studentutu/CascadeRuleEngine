#nullable enable

using System;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    public sealed class FactSimulationWarmupTests
    {
        [Test]
        public void WarmupPreventsCapacityGrowthDuringRepresentativeTick()
        {
            const int entityCount = 512;

            var feature = new WarmupFeature();
            var simulation = new FactSimulation(feature);
            var hints = new WarmupCapacityHints
            {
                EntityCapacity = entityCount,
                FactQueueCapacity = entityCount * 2,
                FactsPerEntityPerTypeCapacity = 2,
                QueryEntityCapacity = entityCount,
                TransactionEntityCapacity = entityCount,
                BatchEntityCapacity = entityCount,
                CommitActionCapacity = entityCount,
                OutputStateCapacityPerOutput = entityCount,
                MutationCapacityPerOutput = entityCount
            };

            simulation.Warmup(hints);
            var before = simulation.CaptureCapacitySnapshot(entityCount);

            Assert.AreEqual(6, before.FactBucketCount);
            Assert.GreaterOrEqual(before.FactQueueCapacity, entityCount * 2);
            Assert.GreaterOrEqual(before.FactTouchedEntityCapacity, entityCount);
            Assert.GreaterOrEqual(before.FactCounterEntityCapacity, entityCount);
            Assert.GreaterOrEqual(before.MinimumFactBucketEntityCapacity, entityCount);
            Assert.GreaterOrEqual(before.MinimumFactBucketTouchedEntityCapacity, entityCount);
            Assert.GreaterOrEqual(before.MinimumFactListCapacity, hints.FactsPerEntityPerTypeCapacity);
            Assert.GreaterOrEqual(before.QueryBufferCapacity, entityCount);
            Assert.GreaterOrEqual(before.TransactionBufferCapacity, entityCount);
            Assert.GreaterOrEqual(before.BatchBufferCapacity, entityCount);
            Assert.GreaterOrEqual(before.CommitActionCapacity, entityCount);
            Assert.GreaterOrEqual(before.MinimumStateCapacityHint, entityCount);
            Assert.GreaterOrEqual(before.MinimumMutationCapacity, entityCount);

            for (var i = 0; i < entityCount; i++)
            {
                var entity = simulation.CreateEntity();
                simulation.SetStateSilently(entity, new WarmupBootstrapState(i));
                simulation.Emit(entity, new WarmupStartFact(i));
                simulation.Emit(entity, new WarmupPairFact(i));
            }

            var result = simulation.RunTick(new ReduceOptions
            {
                MaxMilliseconds = 0
            });

            Assert.IsTrue(result.Complete);
            Assert.AreEqual(entityCount * 5, result.AcceptedFacts);
            Assert.AreEqual(entityCount, result.ReducerInvocations);
            Assert.AreEqual(entityCount + 1, result.TransactionalReducerInvocations);
            Assert.AreEqual(entityCount, result.MutationCount);

            var mutations = 0;
            simulation.ForEachMutation(
                feature.Result,
                (EntityRef entity, in StateMutation<WarmupResultState> mutation) =>
                {
                    mutations++;
                    Assert.IsTrue(mutation.HasNext);
                    Assert.AreEqual(entity.Value, mutation.Next.Value);
                    Assert.AreEqual(3, mutation.Next.SourceCount);
                });

            Assert.AreEqual(entityCount, mutations);
            Assert.AreEqual(before, simulation.CaptureCapacitySnapshot(entityCount));
        }

        public sealed class WarmupFeature : FactFeature
        {
            public WarmupFeature()
            {
                Reduce<WarmupStartFact>()
                    .With<WarmupStartReducer>();

                ReduceWhen<WarmupStartFact, WarmupPairFact>()
                    .With<WarmupPairReducer>();

                ReduceBatchWhen<WarmupStartFact, WarmupPairFact>()
                    .With<WarmupBatchReducer>();

                Result = Output<WarmupResultState>("WarmupResult")
                    .AffectedBy<WarmupDerivedFact>()
                    .AffectedBy<WarmupTransactionalFact>()
                    .AffectedBy<WarmupBatchFact>()
                    .CommitWith<WarmupResultCommitter>();

                Bootstrap = Output<WarmupBootstrapState>("WarmupBootstrap")
                    .AffectedBy<WarmupUnusedFact>()
                    .CommitWith<WarmupBootstrapCommitter>();
            }

            public OutputState<WarmupResultState> Result { get; }
            public OutputState<WarmupBootstrapState> Bootstrap { get; }
        }

        public sealed class WarmupStartReducer : IFactReducer<WarmupStartFact>
        {
            public void Reduce(IReduceContext ctx, EntityRef entity, in WarmupStartFact fact)
            {
                if (!ctx.TryGetState<WarmupBootstrapState>(entity, out var bootstrap))
                {
                    throw new InvalidOperationException("Warmup test expected bootstrap state.");
                }

                if (ctx.Query.With<WarmupBootstrapState>().Count == 0)
                {
                    throw new InvalidOperationException("Warmup test expected state query results.");
                }

                if (ctx.Query.WithFact<WarmupPairFact>().Count == 0)
                {
                    throw new InvalidOperationException("Warmup test expected fact query results.");
                }

                ctx.Emit(entity, new WarmupDerivedFact(bootstrap.Value));
            }
        }

        public sealed class WarmupPairReducer : ITransactionalReducer
        {
            public void Reduce(IReduceContext ctx, EntityRef entity)
            {
                ctx.Emit(entity, new WarmupTransactionalFact(entity.Value));
            }
        }

        public sealed class WarmupBatchReducer : IBatchTransactionalReducer
        {
            public void ReduceBatch(IReduceContext ctx, ReadOnlySpan<EntityRef> entities)
            {
                for (var i = 0; i < entities.Length; i++)
                {
                    var entity = entities[i];
                    ctx.Emit(entity, new WarmupBatchFact(entity.Value));
                }
            }
        }

        public sealed class WarmupResultCommitter : IOutputCommitter<WarmupResultState>
        {
            public CommitDecision<WarmupResultState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<WarmupResultState> previous)
            {
                var facts = ctx.Facts(entity);
                var sourceCount = 0;
                if (facts.Has<WarmupDerivedFact>())
                {
                    sourceCount++;
                }

                if (facts.Has<WarmupTransactionalFact>())
                {
                    sourceCount++;
                }

                if (facts.Has<WarmupBatchFact>())
                {
                    sourceCount++;
                }

                return CommitDecision<WarmupResultState>.Set(
                    new WarmupResultState(entity.Value, sourceCount));
            }
        }

        public sealed class WarmupBootstrapCommitter : IOutputCommitter<WarmupBootstrapState>
        {
            public CommitDecision<WarmupBootstrapState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<WarmupBootstrapState> previous)
            {
                return CommitDecision<WarmupBootstrapState>.Unchanged();
            }
        }

        public readonly struct WarmupBootstrapState : IOutputState, IEquatable<WarmupBootstrapState>
        {
            public WarmupBootstrapState(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(WarmupBootstrapState other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is WarmupBootstrapState other && Equals(other);

            public override int GetHashCode()
                => Value;
        }

        public readonly struct WarmupResultState : IOutputState, IEquatable<WarmupResultState>
        {
            public WarmupResultState(int value, int sourceCount)
            {
                Value = value;
                SourceCount = sourceCount;
            }

            public int Value { get; }
            public int SourceCount { get; }

            public bool Equals(WarmupResultState other)
                => Value == other.Value && SourceCount == other.SourceCount;

            public override bool Equals(object? obj)
                => obj is WarmupResultState other && Equals(other);

            public override int GetHashCode()
                => unchecked((Value * 397) ^ SourceCount);
        }

        public readonly struct WarmupStartFact : IFact, IEquatable<WarmupStartFact>
        {
            public WarmupStartFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(WarmupStartFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is WarmupStartFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        public readonly struct WarmupPairFact : IFact, IEquatable<WarmupPairFact>
        {
            public WarmupPairFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(WarmupPairFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is WarmupPairFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        public readonly struct WarmupDerivedFact : IFact, IEquatable<WarmupDerivedFact>
        {
            public WarmupDerivedFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(WarmupDerivedFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is WarmupDerivedFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        public readonly struct WarmupTransactionalFact : IFact, IEquatable<WarmupTransactionalFact>
        {
            public WarmupTransactionalFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(WarmupTransactionalFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is WarmupTransactionalFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        public readonly struct WarmupBatchFact : IFact, IEquatable<WarmupBatchFact>
        {
            public WarmupBatchFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(WarmupBatchFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is WarmupBatchFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        public readonly struct WarmupUnusedFact : IFact, IEquatable<WarmupUnusedFact>
        {
            public bool Equals(WarmupUnusedFact other)
                => true;

            public override bool Equals(object? obj)
                => obj is WarmupUnusedFact;

            public override int GetHashCode()
                => 0;

            public void Dispose()
            {
            }
        }
    }

}
