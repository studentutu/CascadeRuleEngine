#nullable enable

using System;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    [Category("PublicContract")]
    public sealed class FactSimulationDisposalTests
    {
        [Test]
        public void AcceptedFactsAreDisposedWhenTickFactStoreClears()
        {
            DisposableFact.DisposeCount = 0;

            using var simulation = new FactSimulation(new DisposableFactFeature());
            var entity = simulation.CreateEntity();

            simulation.Emit(entity, new DisposableFact(7));
            var result = simulation.RunTick(ReduceOptions.Default());

            Assert.AreEqual(1, result.AcceptedFacts);
            Assert.AreEqual(1, DisposableFact.DisposeCount);
        }

        [Test]
        public void DisposeDisposesQueuedFactsExactlyOnce()
        {
            DisposableFact.DisposeCount = 0;

            using var simulation = new FactSimulation(new DisposableFactFeature());
            var entity = simulation.CreateEntity();

            simulation.Emit(entity, new DisposableFact(7));
            simulation.Dispose();
            simulation.Dispose();

            Assert.AreEqual(1, DisposableFact.DisposeCount);
        }

        [Test]
        public void DisposeAfterTickDoesNotDisposeFactsAgain()
        {
            DisposableFact.DisposeCount = 0;

            using var simulation = new FactSimulation(new DisposableFactFeature());
            var entity = simulation.CreateEntity();

            simulation.Emit(entity, new DisposableFact(7));
            simulation.RunTick(ReduceOptions.Default());
            simulation.Dispose();

            Assert.AreEqual(1, DisposableFact.DisposeCount);
        }

        [Test]
        public void DisposeDisposesCurrentOutputStateExactlyOnce()
        {
            DisposableState.DisposeCount = 0;

            using var simulation = new FactSimulation(new DisposableStateFeature());
            var entity = simulation.CreateEntity();

            simulation.SetStateSilently(entity, new DisposableState(3));
            simulation.Dispose();
            simulation.Dispose();

            Assert.AreEqual(1, DisposableState.DisposeCount);
        }

        [Test]
        public void DisposeIsTerminalAndRejectsPublicSimulationUse()
        {
            using var simulation = new FactSimulation(new EmptyFactFeature());

            simulation.Dispose();

            Assert.Throws<ObjectDisposedException>(() => simulation.CreateEntity());
            Assert.Throws<ObjectDisposedException>(() => simulation.RunTick(ReduceOptions.Default()));
        }

        [Test]
        public void DisposingFeatureExternallyRejectsSimulationUse()
        {
            var feature = new EmptyFactFeature();
            using var simulation = new FactSimulation(feature);

            feature.Dispose();

            Assert.Throws<ObjectDisposedException>(() => simulation.CreateEntity());
            simulation.Dispose();
        }

        [Test]
        public void ComposedFeatureTransfersOwnershipAndDisposesRegistrationsOnce()
        {
            DisposableReducer.DisposeCount = 0;
            DisposableStateCommitter.DisposeCount = 0;

            var feature = new ParentDisposableFeature();
            Assert.Throws<InvalidOperationException>(() => new FactSimulation(feature.Child));

            using var simulation = new FactSimulation(feature);

            simulation.Dispose();
            simulation.Dispose();

            Assert.AreEqual(1, DisposableReducer.DisposeCount);
            Assert.AreEqual(1, DisposableStateCommitter.DisposeCount);
            Assert.Throws<ObjectDisposedException>(() => new FactSimulation(feature));
            Assert.Throws<ObjectDisposedException>(() => new FactSimulation(feature.Child));
        }

        private sealed class EmptyFactFeature : FactFeature
        {
        }

        private sealed class DisposableFactFeature : FactFeature
        {
            public DisposableFactFeature()
            {
                Reduce<DisposableFact>()
                    .With<DisposableFactReducer>();
            }
        }

        private sealed class ParentDisposableFeature : FactFeature
        {
            internal ParentDisposableFeature()
            {
                Child = new DisposableStateFeature();
                SubFeature(Child);
            }

            internal DisposableStateFeature Child { get; }
        }

        private sealed class DisposableStateFeature : FactFeature
        {
            public DisposableStateFeature()
            {
                Reduce<DisposableFact>()
                    .With<DisposableReducer>();

                Output<DisposableState>("Disposable")
                    .AffectedBy<DisposableFact>(0)
                    .CommitWith<DisposableStateCommitter>();
            }
        }

        private sealed class DisposableReducer : IFactReducer<DisposableFact>, IDisposable
        {
            internal static int DisposeCount;

            public void Reduce(IReduceContext ctx, EntityRef entity, in DisposableFact fact)
            {
            }

            public void Dispose()
                => DisposeCount++;
        }

        private sealed class DisposableFactReducer : IFactReducer<DisposableFact>
        {
            public void Reduce(IReduceContext ctx, EntityRef entity, in DisposableFact fact)
            {
            }
        }

        private sealed class DisposableStateCommitter : IOutputCommitter<DisposableState>, IDisposable
        {
            internal static int DisposeCount;

            public CommitDecision<DisposableState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<DisposableState> previous)
            {
                return CommitDecision<DisposableState>.Unchanged();
            }

            public void Dispose()
                => DisposeCount++;
        }

        private readonly struct DisposableState : IOutputState, IDisposable, IEquatable<DisposableState>
        {
            internal static int DisposeCount;

            internal DisposableState(int value)
            {
                Value = value;
            }

            private int Value { get; }

            public bool Equals(DisposableState other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is DisposableState other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
                => DisposeCount++;
        }

        private readonly struct DisposableFact : IFact, IEquatable<DisposableFact>
        {
            internal static int DisposeCount;

            internal DisposableFact(int value)
            {
                Value = value;
            }

            private int Value { get; }

            public bool Equals(DisposableFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is DisposableFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
                => DisposeCount++;
        }

    }
}
