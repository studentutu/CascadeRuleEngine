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

        private sealed class RouteFeature : FactFeature
        {
            public RouteFeature()
            {
                Output<RouteState>("Route")
                    .AffectedBy<RouteFact>()
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
