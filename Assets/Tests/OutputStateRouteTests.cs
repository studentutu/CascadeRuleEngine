#nullable enable

using System;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    public sealed class OutputStateRouteTests
    {
        [Test]
        public void SameOutputStateTypeUsesSeparateBucketsPerSimulation()
        {
            var first = new FactSimulation(new RouteFeature());
            var second = new FactSimulation(new RouteFeature());

            try
            {
                var firstEntity = first.CreateEntity();
                var secondEntity = second.CreateEntity();

                first.SetStateSilently(firstEntity, new RouteState(10));
                second.SetStateSilently(secondEntity, new RouteState(20));

                Assert.AreEqual(10, first.Get<RouteState>(firstEntity).Value);
                Assert.AreEqual(20, second.Get<RouteState>(secondEntity).Value);
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        [Test]
        public void RegistrationPrioritySelectsSameWinnerRegardlessOfFactOrder()
        {
            var lowThenHigh = new FactSimulation(new PriorityRouteFeature());
            var highThenLow = new FactSimulation(new PriorityRouteFeature());

            try
            {
                var firstEntity = lowThenHigh.CreateEntity();
                lowThenHigh.Emit(firstEntity, new LowPriorityRouteFact(10));
                lowThenHigh.Emit(firstEntity, new HighPriorityRouteFact(99));

                var secondEntity = highThenLow.CreateEntity();
                highThenLow.Emit(secondEntity, new HighPriorityRouteFact(99));
                highThenLow.Emit(secondEntity, new LowPriorityRouteFact(10));

                lowThenHigh.RunTick(ReduceOptions.Default());
                highThenLow.RunTick(ReduceOptions.Default());

                Assert.AreEqual(99, lowThenHigh.Get<RouteState>(firstEntity).Value);
                Assert.AreEqual(99, highThenLow.Get<RouteState>(secondEntity).Value);
            }
            finally
            {
                lowThenHigh.Dispose();
                highThenLow.Dispose();
            }
        }

        [Test]
        public void EqualRegistrationPriorityThrowsBeforeDurableWrite()
        {
            var simulation = new FactSimulation(new TiedPriorityRouteFeature());

            try
            {
                var entity = simulation.CreateEntity();
                simulation.SetStateSilently(entity, new RouteState(5));
                simulation.Emit(entity, new LowPriorityRouteFact(10));
                simulation.Emit(entity, new HighPriorityRouteFact(99));

                Assert.Throws<CommitConflictException>(
                    () => simulation.RunTick(ReduceOptions.Default()));
                Assert.AreEqual(5, simulation.Get<RouteState>(entity).Value);
                Assert.AreEqual(0, simulation.MutationCount);
            }
            finally
            {
                simulation.Dispose();
            }
        }

        private sealed class RouteFeature : FactFeature
        {
            public RouteFeature()
            {
                Output<RouteState>("Route")
                    .AffectedBy<RouteFact>(0)
                    .CommitWith<RouteCommitter>();
            }
        }

        private sealed class RouteCommitter : IOutputCommitter<RouteState>
        {
            public CommitDecision<RouteState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<RouteState> previous)
                => CommitDecision<RouteState>.Unchanged();
        }

        private sealed class PriorityRouteFeature : FactFeature
        {
            public PriorityRouteFeature()
            {
                Output<RouteState>("PriorityRoute")
                    .AffectedBy<LowPriorityRouteFact>(priority: 10)
                    .AffectedBy<HighPriorityRouteFact>(priority: 100)
                    .ConflictPolicy(CommitConflictPolicy.PriorityWinnerOrThrowOnTie)
                    .CommitWith<PriorityRouteCommitter>();
            }
        }

        private sealed class TiedPriorityRouteFeature : FactFeature
        {
            public TiedPriorityRouteFeature()
            {
                Output<RouteState>("TiedPriorityRoute")
                    .AffectedBy<LowPriorityRouteFact>(priority: 100)
                    .AffectedBy<HighPriorityRouteFact>(priority: 100)
                    .ConflictPolicy(CommitConflictPolicy.PriorityWinnerOrThrowOnTie)
                    .CommitWith<PriorityRouteCommitter>();
            }
        }

        private sealed class PriorityRouteCommitter : IOutputCommitter<RouteState>
        {
            public CommitDecision<RouteState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<RouteState> previous)
            {
                var facts = ctx.Facts(entity);
                if (facts.TryGetLatest<HighPriorityRouteFact>(out var high))
                {
                    return CommitDecision<RouteState>.Set(new RouteState(high.Value));
                }

                return facts.TryGetLatest<LowPriorityRouteFact>(out var low)
                    ? CommitDecision<RouteState>.Set(new RouteState(low.Value))
                    : CommitDecision<RouteState>.Unchanged();
            }
        }

        private readonly struct RouteFact : IFact, IEquatable<RouteFact>
        {
            public bool Equals(RouteFact other)
                => true;

            public override bool Equals(object? obj)
                => obj is RouteFact;

            public override int GetHashCode()
                => 0;

            public void Dispose()
            {
            }
        }

        private readonly struct LowPriorityRouteFact : IFact, IEquatable<LowPriorityRouteFact>
        {
            public LowPriorityRouteFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(LowPriorityRouteFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is LowPriorityRouteFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        private readonly struct HighPriorityRouteFact : IFact, IEquatable<HighPriorityRouteFact>
        {
            public HighPriorityRouteFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(HighPriorityRouteFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is HighPriorityRouteFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        private readonly struct RouteState : IOutputState, IEquatable<RouteState>
        {
            public RouteState(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(RouteState other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is RouteState other && Equals(other);

            public override int GetHashCode()
                => Value;
        }
    }
}
