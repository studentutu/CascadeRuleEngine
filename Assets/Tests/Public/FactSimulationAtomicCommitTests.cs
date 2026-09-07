#nullable enable

using System;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    [Category("PublicContract")]
    public sealed class FactSimulationAtomicCommitTests
    {
        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void EqualityFailurePreservesEveryOutputAndLifecycle(bool reverseOutputs, bool reverseEntities)
        {
            using var simulation = new FactSimulation(new AtomicFeature(reverseOutputs));
            var first = simulation.CreateEntity();
            var second = simulation.CreateEntity();
            var dead = simulation.CreateEntity();
            foreach (var entity in new[] { first, second, dead })
            {
                simulation.SetStateSilently(entity, new AtomicState(10));
                simulation.SetStateSilently(entity, new EqualityState(20));
            }
            simulation.DestroyEntity(dead);
            simulation.Emit(reverseEntities ? second : first, new AtomicFact());
            simulation.Emit(reverseEntities ? first : second, new AtomicFact());
            EqualityState.Throw = true;
            try
            {
                Assert.Throws<InvalidOperationException>(() => simulation.RunTick(new ReduceOptions { MaxMilliseconds = 0 }));
            }
            finally
            {
                EqualityState.Throw = false;
            }
            foreach (var entity in new[] { first, second, dead })
            {
                Assert.AreEqual(10, simulation.Get<AtomicState>(entity).Value);
                Assert.AreEqual(20, simulation.Get<EqualityState>(entity).Value);
                Assert.IsFalse(simulation.IsDestroyed(entity));
            }
            Assert.AreEqual(0, simulation.MutationCount);
            Assert.AreEqual(0, simulation.RunTick(new ReduceOptions { MaxMilliseconds = 0 }).AcceptedFacts);
        }

        [Test]
        public void ExactCapacityReplacementAndDeletionPublishReplayableFinalChanges()
        {
            var feature = new AtomicFeature(false);
            using var simulation = new FactSimulation(feature, new CascadeSettings(2, 2, 1));
            var live = simulation.CreateEntity();
            var dead = simulation.CreateEntity();
            simulation.SetStateSilently(live, new AtomicState(10));
            simulation.SetStateSilently(dead, new AtomicState(20));
            simulation.Emit(live, new AtomicFact());
            simulation.DestroyEntity(dead);
            simulation.DestroyEntity(dead);
            var result = simulation.RunTick(new ReduceOptions { MaxMilliseconds = 0 });
            Assert.AreEqual(3, result.MutationCount);
            var count = 0;
            StateMutationHandler<AtomicState> handler = (EntityRef _, in StateMutation<AtomicState> mutation) => count++;
            simulation.ForEachMutation(feature.Result, handler);
            simulation.ForEachMutation(feature.Result, handler);
            Assert.AreEqual(4, count);
            var recycled = simulation.CreateEntity();
            Assert.AreEqual(dead.Value, recycled.Value);
            Assert.AreNotEqual(dead.Generation, recycled.Generation);
            Assert.IsFalse(simulation.Has<AtomicState>(recycled));
        }

        private sealed class AtomicFeature : FactFeature
        {
            public AtomicFeature(bool reverse)
            {
                if (reverse) RegisterEquality();
                Result = Output<AtomicState>("Atomic").AffectedBy<AtomicFact>(0).CommitWith<AtomicCommitter>();
                if (!reverse) RegisterEquality();
            }

            public OutputState<AtomicState> Result { get; }
            private void RegisterEquality()
                => Output<EqualityState>("Equality").AffectedBy<AtomicFact>(0).CommitWith<EqualityCommitter>();
        }

        private readonly struct AtomicFact : IFact<AtomicFact>
        {
            public bool Equals(AtomicFact other) => true;
        }

        private readonly struct AtomicState : IOutputState<AtomicState>
        {
            public AtomicState(int value) => Value = value;
            public int Value { get; }
            public bool Equals(AtomicState other) => Value == other.Value;
        }

        private readonly struct EqualityState : IOutputState<EqualityState>
        {
            public static bool Throw;
            public EqualityState(int value) => Value = value;
            public int Value { get; }
            public bool Equals(EqualityState other)
                => Throw ? throw new InvalidOperationException("state equality failed") : Value == other.Value;
        }

        private sealed class AtomicCommitter : IOutputCommitter<AtomicState>
        {
            public CommitDecision<AtomicState> Commit(ICommitContext context, EntityRef entity, in Optional<AtomicState> previous)
                => CommitDecision<AtomicState>.Set(new AtomicState(11));
        }

        private sealed class EqualityCommitter : IOutputCommitter<EqualityState>
        {
            public CommitDecision<EqualityState> Commit(ICommitContext context, EntityRef entity, in Optional<EqualityState> previous)
                => CommitDecision<EqualityState>.Set(new EqualityState(21));
        }
    }
}

