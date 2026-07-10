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

        [TestCase(true)]
        [TestCase(false)]
        public void CommittersReadOnePreviousStateSnapshotRegardlessOfOutputRegistrationOrder(
            bool sourceOutputFirst)
        {
            var simulation = new FactSimulation(new SnapshotIsolationFeature(sourceOutputFirst));

            try
            {
                var first = simulation.CreateEntity();
                var second = simulation.CreateEntity();
                simulation.SetStateSilently(first, new SnapshotSourceState(10));
                simulation.SetStateSilently(second, new SnapshotSourceState(100));
                simulation.SetStateSilently(first, new SnapshotObserverState(second, -1));
                simulation.SetStateSilently(second, new SnapshotObserverState(first, -1));

                simulation.Emit(first, new SnapshotCommitFact());
                simulation.Emit(second, new SnapshotCommitFact());

                var result = simulation.RunTick(ReduceOptions.Default());

                Assert.AreEqual(11, simulation.Get<SnapshotSourceState>(first).Value);
                Assert.AreEqual(101, simulation.Get<SnapshotSourceState>(second).Value);
                Assert.AreEqual(100, simulation.Get<SnapshotObserverState>(first).ObservedValue);
                Assert.AreEqual(10, simulation.Get<SnapshotObserverState>(second).ObservedValue);
                Assert.AreEqual(4, result.MutationCount);
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

        private sealed class SnapshotIsolationFeature : FactFeature
        {
            public SnapshotIsolationFeature(bool sourceOutputFirst)
            {
                if (sourceOutputFirst)
                {
                    RegisterSourceOutput();
                    RegisterObserverOutput();
                    return;
                }

                RegisterObserverOutput();
                RegisterSourceOutput();
            }

            private void RegisterSourceOutput()
            {
                Output<SnapshotSourceState>("SnapshotSource")
                    .AffectedBy<SnapshotCommitFact>(0)
                    .CommitWith<SnapshotSourceCommitter>();
            }

            private void RegisterObserverOutput()
            {
                Output<SnapshotObserverState>("SnapshotObserver")
                    .AffectedBy<SnapshotCommitFact>(0)
                    .CommitWith<SnapshotObserverCommitter>();
            }
        }

        private sealed class SnapshotSourceCommitter : IOutputCommitter<SnapshotSourceState>
        {
            public CommitDecision<SnapshotSourceState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<SnapshotSourceState> previous)
            {
                return previous.HasValue
                    ? CommitDecision<SnapshotSourceState>.Set(
                        new SnapshotSourceState(previous.Value.Value + 1))
                    : CommitDecision<SnapshotSourceState>.Unchanged();
            }
        }

        private sealed class SnapshotObserverCommitter : IOutputCommitter<SnapshotObserverState>
        {
            public CommitDecision<SnapshotObserverState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<SnapshotObserverState> previous)
            {
                if (!previous.HasValue)
                {
                    return CommitDecision<SnapshotObserverState>.Unchanged();
                }

                var target = previous.Value.Target;
                var observed = ctx.GetState<SnapshotSourceState>(target).Value;
                return CommitDecision<SnapshotObserverState>.Set(
                    new SnapshotObserverState(target, observed));
            }
        }

        private readonly struct SnapshotCommitFact : IFact<SnapshotCommitFact>
        {
            public bool Equals(SnapshotCommitFact other)
                => true;
        }

        private readonly struct SnapshotSourceState : IOutputState<SnapshotSourceState>
        {
            public SnapshotSourceState(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(SnapshotSourceState other)
                => Value == other.Value;
        }

        private readonly struct SnapshotObserverState : IOutputState<SnapshotObserverState>
        {
            public SnapshotObserverState(EntityRef target, int observedValue)
            {
                Target = target;
                ObservedValue = observedValue;
            }

            public EntityRef Target { get; }
            public int ObservedValue { get; }

            public bool Equals(SnapshotObserverState other)
                => Target == other.Target && ObservedValue == other.ObservedValue;
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
