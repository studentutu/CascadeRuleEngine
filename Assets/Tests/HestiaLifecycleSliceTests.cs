#nullable enable

using System;
using Hestia;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    public sealed class HestiaLifecycleSliceTests
    {
        [Test]
        public void CrossEntityCreationComponentRemovalAndDestructionRemainSeparateTransactions()
        {
            var feature = new LifecycleFeature();
            using var simulation = new FactSimulation(feature, new CascadeSettings(3, 4, 2));
            var parent = simulation.CreateEntity();
            var source = simulation.CreateEntity();
            simulation.SetStateSilently(source, new HestiaAmmoState(7));
            var resource = new SpawnResource(source);
            simulation.Emit(parent, new SpawnChildFact(resource));
            var slice = new ReduceOptions { MaxWorkItems = 1, MaxMilliseconds = 0 };
            Assert.IsFalse(simulation.RunTickIncremental(slice, out _));
            Assert.AreEqual(0, resource.Disposals);
            var created = Complete(simulation, slice);
            var child = resource.Child;
            Assert.AreEqual(7, simulation.Get<ChildState>(child).Value);
            Assert.AreEqual(1, created.MutationCount);
            Assert.AreEqual(1, resource.Disposals);
            Assert.IsTrue(simulation.TryGetEntity(child.Value, out _));

            simulation.Emit(child, new RemoveChildStateFact());
            var removed = Complete(simulation, slice);
            Assert.AreEqual(1, removed.MutationCount);
            Assert.IsFalse(simulation.Has<ChildState>(child));
            Assert.IsTrue(simulation.TryGetEntity(child.Value, out _));

            simulation.Emit(child, new DeadFact());
            var destroyed = Complete(simulation, slice);
            Assert.AreEqual(0, destroyed.MutationCount);
            Assert.IsFalse(simulation.TryGetEntity(child.Value, out _));
            var replacement = simulation.CreateEntity();
            Assert.AreEqual(child.Value, replacement.Value);
            Assert.AreNotEqual(child.Generation, replacement.Generation);
            simulation.Emit(child, new RemoveChildStateFact());
            Assert.AreEqual(1, Complete(simulation, slice).RejectedDestroyedEntityFacts);
            Assert.IsFalse(simulation.Has<ChildState>(replacement));
            Assert.AreEqual(1, resource.Disposals);
        }

        [Test]
        public void PreparationFailureRollsBackReducerCreatedChildAndCleansAcceptedResourceOnce()
        {
            using var simulation = new FactSimulation(new LifecycleFeature(), new CascadeSettings(3, 4, 2));
            var parent = simulation.CreateEntity();
            var source = simulation.CreateEntity();
            simulation.SetStateSilently(source, new HestiaAmmoState(7));
            var resource = new SpawnResource(source) { FailCommit = true };
            simulation.Emit(parent, new SpawnChildFact(resource));
            simulation.DestroyEntity(parent);
            Assert.Throws<InvalidOperationException>(() => simulation.RunTick(new ReduceOptions { MaxMilliseconds = 0 }));
            Assert.AreEqual(1, resource.Disposals);
            Assert.IsFalse(simulation.IsDestroyed(parent));
            Assert.IsFalse(simulation.TryGetEntity(resource.Child.Value, out _));
            Assert.AreEqual(0, simulation.MutationCount);
            var replacement = simulation.CreateEntity();
            Assert.AreEqual(resource.Child.Value, replacement.Value);
            Assert.AreNotEqual(resource.Child.Generation, replacement.Generation);
            Complete(simulation, new ReduceOptions { MaxMilliseconds = 0 });
            Assert.AreEqual(1, resource.Disposals);
        }

        [Test]
        public void FailedSlabPreparationLeavesRejectedResourceCallerOwned()
        {
            using var simulation = new FactSimulation(new LifecycleFeature());
            simulation.Warmup(new WarmupCapacityHints
            {
                EntityCapacity = 3,
                FactQueueCapacity = 8,
                FactsPerEntityPerTypeCapacity = 1,
                FactListCapacityMode = FactListCapacityMode.Fixed
            });
            var parent = simulation.CreateEntity();
            var source = simulation.CreateEntity();
            simulation.SetStateSilently(source, new HestiaAmmoState(7));
            var accepted = new SpawnResource(source);
            var rejected = new SpawnResource(source);
            simulation.Emit(parent, new SpawnChildFact(accepted));
            Assert.Throws<InvalidOperationException>(() => simulation.Emit(parent, new SpawnChildFact(rejected)));
            Assert.AreEqual(1, simulation.Facts(parent).All<SpawnChildFact>().Length);
            simulation.RunTick(new ReduceOptions { MaxMilliseconds = 0 });
            Assert.AreEqual(1, accepted.Disposals);
            Assert.AreEqual(0, rejected.Disposals);
        }

        private static SimulationResult Complete(FactSimulation simulation, ReduceOptions options)
        {
            for (var i = 0; i < 32; i++)
                if (simulation.RunTickIncremental(options, out var result)) return result;
            throw new AssertionException("Lifecycle continuation stalled.");
        }

        private sealed class LifecycleFeature : FactFeature
        {
            internal LifecycleFeature()
            {
                SubFeature(new HestiaGameSimulationFeature());
                Reduce<SpawnChildFact>().With<SpawnChildReducer>();
                Reduce<ChildValueFact>().With<ObserveChildReducer>();
                Output<ChildState>("Child")
                    .AffectedBy<ChildValueFact>(0)
                    .AffectedBy<RemoveChildStateFact>(0)
                    .CommitWith<ChildCommitter>();
            }
        }
        private sealed class SpawnResource
        {
            internal SpawnResource(EntityRef source) => Source = source;
            internal EntityRef Source;
            internal EntityRef Child;
            internal bool FailCommit;
            internal int Disposals;
        }
        private readonly struct SpawnChildFact : IFact<SpawnChildFact>
        {
            internal SpawnChildFact(SpawnResource resource) => Resource = resource;
            internal SpawnResource Resource { get; }
            public bool Equals(SpawnChildFact other) => ReferenceEquals(Resource, other.Resource);
            public void Dispose() => Resource.Disposals++;
        }
        private readonly struct ChildValueFact : IFact<ChildValueFact>
        {
            internal ChildValueFact(int value, bool fail) { Value = value; Fail = fail; }
            internal int Value { get; }
            internal bool Fail { get; }
            public bool Equals(ChildValueFact other) => Value == other.Value && Fail == other.Fail;
        }
        private readonly struct RemoveChildStateFact : IFact<RemoveChildStateFact>
        { public bool Equals(RemoveChildStateFact other) => true; }
        private readonly struct ChildState : IOutputState<ChildState>
        {
            internal ChildState(int value) => Value = value;
            internal int Value { get; }
            public bool Equals(ChildState other) => Value == other.Value;
        }
        private sealed class SpawnChildReducer : IFactReducer<SpawnChildFact>
        {
            public void Reduce(IReduceContext context, EntityRef entity, in SpawnChildFact fact)
            {
                var value = context.GetState<HestiaAmmoState>(fact.Resource.Source).Current;
                var child = context.CreateEntity();
                fact.Resource.Child = child;
                context.Emit(child, new ChildValueFact(value, fact.Resource.FailCommit));
            }
        }
        private sealed class ObserveChildReducer : IFactReducer<ChildValueFact>
        { public void Reduce(IReduceContext context, EntityRef entity, in ChildValueFact fact) { } }
        private sealed class ChildCommitter : IOutputCommitter<ChildState>
        {
            public CommitDecision<ChildState> Commit(ICommitContext context, EntityRef entity, in Optional<ChildState> previous)
            {
                var facts = context.Facts(entity);
                if (facts.Has<RemoveChildStateFact>()) return CommitDecision<ChildState>.Delete();
                if (!facts.TryGetLatest<ChildValueFact>(out var value)) return CommitDecision<ChildState>.Unchanged();
                if (value.Fail) throw new InvalidOperationException("child preparation failed");
                return CommitDecision<ChildState>.Set(new ChildState(value.Value));
            }
        }
    }
}
