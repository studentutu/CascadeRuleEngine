#nullable enable

using System;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    public sealed class FactSimulationOwnershipTests
    {
        [SetUp]
        public void ResetTeardownProbes()
        {
            ResourceReducer.Teardown = new Resource();
            ResourceCommitter.Teardown = new Resource();
            TransactionReducer.Teardown = new Resource();
            BatchReducer.Teardown = new Resource();
            StateReducer.Teardown = new Resource();
        }

        [Test]
        public void AdmissionFailureLeavesPayloadCallerOwnedAndNextTickClean()
        {
            using var simulation = new FactSimulation(new ResourceFeature(), new CascadeSettings(2, 1, 1));
            var entity = simulation.CreateEntity();
            var accepted = new Resource();
            var rejected = new Resource();
            simulation.Emit(entity, new ResourceFact(accepted));
            Assert.Throws<InvalidOperationException>(() => simulation.Emit(entity, new OtherResourceFact(rejected)));
            Assert.AreEqual(1, ((IReduceContext)simulation).Facts(entity).All<ResourceFact>().Length);
            Assert.AreEqual(0, rejected.Disposals);
            var result = simulation.RunTick(new ReduceOptions { MaxMilliseconds = 0 });
            Assert.AreEqual(1, result.AcceptedFacts);
            Assert.AreEqual(1, accepted.Disposals);
            Assert.AreEqual(0, rejected.Disposals);
        }

        [Test]
        public void ThrowingFactDoesNotPreventOtherFactsFromClearingOrRepeatDisposal()
        {
            using var simulation = new FactSimulation(new ResourceFeature());
            var first = new Resource { ThrowOnDispose = true };
            var second = new Resource();
            var third = new Resource();
            var entity = simulation.CreateEntity();
            simulation.Emit(entity, new ResourceFact(first));
            simulation.Emit(entity, new ResourceFact(second));
            simulation.Emit(simulation.CreateEntity(), new OtherResourceFact(third));
            Assert.Throws<InvalidOperationException>(() => simulation.RunTick(new ReduceOptions { MaxMilliseconds = 0 }));
            Assert.AreEqual(1, first.Disposals);
            Assert.AreEqual(1, second.Disposals);
            Assert.AreEqual(1, third.Disposals);
            Assert.AreEqual(0, simulation.RunTick(new ReduceOptions { MaxMilliseconds = 0 }).AcceptedFacts);
            Assert.AreEqual(1, first.Disposals);
        }

        [Test]
        public void CleanupFailureAfterCommitPreservesStateAndMutationJournal()
        {
            var feature = new ResourceFeature();
            using var simulation = new FactSimulation(feature);
            var resource = new Resource { ThrowOnDispose = true };
            var entity = simulation.CreateEntity();
            simulation.Emit(entity, new ResourceFact(resource));
            Assert.Throws<InvalidOperationException>(() => simulation.RunTick(new ReduceOptions { MaxMilliseconds = 0 }));
            Assert.AreEqual(1, simulation.State.Get<ResourceState>(entity).Value);
            Assert.AreEqual(1, simulation.MutationCount);
            Assert.IsTrue(simulation.LastResult.Complete);
            Assert.AreEqual(simulation.Tick, simulation.LastResult.Tick);
            var mutations = 0;
            simulation.ForEachMutation(feature.Result, (EntityRef _, in StateMutation<ResourceState> mutation) => mutations++);
            Assert.AreEqual(1, mutations);
        }

        [Test]
        public void FailedReductionPreservesOriginalErrorAndFinishesCleanup()
        {
            using var simulation = new FactSimulation(new ResourceFeature());
            var first = new Resource { ThrowOnDispose = true, ThrowOnReduce = true };
            var second = new Resource();
            var entity = simulation.CreateEntity();
            simulation.Emit(entity, new ResourceFact(first));
            simulation.Emit(entity, new ResourceFact(second));
            var error = Assert.Throws<AggregateException>(() => simulation.RunTick(new ReduceOptions { MaxMilliseconds = 0 }));
            StringAssert.Contains("reducer failed", error!.ToString());
            StringAssert.Contains("disposal failed", error.ToString());
            Assert.AreEqual(1, first.Disposals);
            Assert.AreEqual(1, second.Disposals);
            Assert.AreEqual(0, simulation.RunTick(new ReduceOptions { MaxMilliseconds = 0 }).AcceptedFacts);
        }

        [Test]
        public void IncrementalPauseKeepsFactsAliveUntilTerminalDispose()
        {
            var simulation = new FactSimulation(new ResourceFeature());
            var first = new Resource();
            var second = new Resource();
            var entity = simulation.CreateEntity();
            simulation.Emit(entity, new ResourceFact(first));
            simulation.Emit(entity, new ResourceFact(second));
            Assert.IsFalse(simulation.RunTickIncremental(new ReduceOptions { MaxFacts = 1, MaxMilliseconds = 0 }, out _));
            Assert.AreEqual(0, first.Disposals);
            Assert.AreEqual(0, second.Disposals);
            simulation.Dispose();
            simulation.Dispose();
            Assert.AreEqual(1, first.Disposals);
            Assert.AreEqual(1, second.Disposals);
        }

        [Test]
        public void TerminalCleanupAttemptsEveryOwnerAndUnbindsStaticRoutesDespiteErrors()
        {
            var feature = new ResourceFeature();
            var simulation = new FactSimulation(feature);
            var resource = new Resource { ThrowOnDispose = true };
            var entity = simulation.CreateEntity();
            simulation.Emit(entity, new ResourceFact(resource));
            Assert.Throws<InvalidOperationException>(() => simulation.Dispose());
            Assert.DoesNotThrow(() => simulation.Dispose());
            Assert.AreEqual(1, resource.Disposals);
            Assert.Throws<ObjectDisposedException>(() => simulation.CreateEntity());
            Assert.Throws<ObjectDisposedException>(() => new FactSimulation(feature));
            Assert.Throws<InvalidOperationException>(() => FactEmitRouteCache<ResourceFact>.Require(feature.Registry));
            Assert.Throws<InvalidOperationException>(() => OutputStateRouteCache<ResourceState>.Require(simulation));
            Assert.AreEqual(0, feature.Registry.Outputs.Count);
        }

        [Test]
        public void FeatureTeardownVisitsEveryRegistrationAndChildAfterCallbackFailures()
        {
            var feature = new ParentFeature();
            var simulation = new FactSimulation(feature);
            ResourceReducer.Teardown.ThrowOnDispose = true;
            ResourceCommitter.Teardown.ThrowOnDispose = true;
            TransactionReducer.Teardown.ThrowOnDispose = true;
            BatchReducer.Teardown.ThrowOnDispose = true;
            StateReducer.Teardown.ThrowOnDispose = true;
            var stateResource = new Resource { ThrowOnDispose = true };
            var secondStateResource = new Resource();
            simulation.SetStateSilently(simulation.CreateEntity(), new ResourceState(1, stateResource));
            simulation.SetStateSilently(simulation.CreateEntity(), new ResourceState(2, secondStateResource));
            var error = Assert.Throws<AggregateException>(() => simulation.Dispose());
            Assert.AreEqual(6, error!.Flatten().InnerExceptions.Count);
            Assert.AreEqual(1, ResourceReducer.Teardown.Disposals);
            Assert.AreEqual(1, ResourceCommitter.Teardown.Disposals);
            Assert.AreEqual(1, TransactionReducer.Teardown.Disposals);
            Assert.AreEqual(1, BatchReducer.Teardown.Disposals);
            Assert.AreEqual(1, StateReducer.Teardown.Disposals);
            Assert.AreEqual(1, stateResource.Disposals);
            Assert.AreEqual(1, secondStateResource.Disposals);
            Assert.IsTrue(feature.Child.IsDisposed);
            Assert.AreEqual(0, feature.Registry.Outputs.Count);
            Assert.Throws<InvalidOperationException>(() => FactEmitRouteCache<ResourceFact>.Require(feature.Registry));
            Assert.Throws<InvalidOperationException>(() => OutputStateRouteCache<ResourceState>.Require(simulation));
            Assert.DoesNotThrow(() => simulation.Dispose());
            Assert.DoesNotThrow(() => feature.Dispose());
        }

        [Test]
        public void ExternalFeatureDisposalCannotPreventSimulationFromUnbindingStateRoutes()
        {
            var feature = new ResourceFeature();
            var simulation = new FactSimulation(feature);
            feature.Dispose();
            simulation.Dispose();
            Assert.Throws<InvalidOperationException>(() => OutputStateRouteCache<ResourceState>.Require(simulation));
        }

        [Test]
        public void FailedCommitDisposesFactsAndDiscardsEarlierQueuedDecisions()
        {
            using var simulation = new FactSimulation(new ResourceFeature());
            var first = new Resource();
            var second = new Resource { ThrowOnCommit = true };
            var entity = simulation.CreateEntity();
            simulation.Emit(entity, new ResourceFact(first));
            simulation.Emit(simulation.CreateEntity(), new ResourceFact(second));
            Assert.Throws<InvalidOperationException>(() => simulation.RunTick(new ReduceOptions { MaxMilliseconds = 0 }));
            Assert.AreEqual(1, first.Disposals);
            Assert.AreEqual(1, second.Disposals);
            Assert.IsFalse(simulation.State.Has<ResourceState>(entity));
            Assert.AreEqual(0, simulation.RunTick(new ReduceOptions { MaxMilliseconds = 0 }).MutationCount);
        }

        [Test]
        public void FactDisposerCannotEmitOrRecursivelyTickOrTearDownTheSimulation()
        {
            using var simulation = new FactSimulation(new ResourceFeature());
            var entity = simulation.CreateEntity();
            var rejected = new Resource();
            var resource = new Resource
            {
                OnDispose = () =>
                {
                    Assert.Throws<InvalidOperationException>(() => simulation.Emit(entity, new ResourceFact(rejected)));
                    Assert.Throws<InvalidOperationException>(() => simulation.RunTick());
                    Assert.Throws<InvalidOperationException>(() => simulation.Dispose());
                    Assert.Throws<InvalidOperationException>(() => simulation.Warmup(new WarmupCapacityHints()));
                }
            };
            simulation.Emit(entity, new ResourceFact(resource));
            Assert.IsTrue(simulation.RunTick(new ReduceOptions { MaxMilliseconds = 0 }).Complete);
            Assert.AreEqual(1, resource.Disposals);
            Assert.AreEqual(0, rejected.Disposals);
        }

        [Test]
        public void DuplicateAndRetiredEntityRejectionsDoNotAcquireAnotherResourceLease()
        {
            using var simulation = new FactSimulation(new ResourceFeature());
            var entity = simulation.CreateEntity();
            var resource = new Resource();
            simulation.Emit(entity, new ResourceFact(resource));
            simulation.Emit(entity, new ResourceFact(resource));
            var result = simulation.RunTick(new ReduceOptions { MaxMilliseconds = 0 });
            Assert.AreEqual(1, result.DeduplicatedFacts);
            Assert.AreEqual(1, resource.Disposals);
            simulation.DestroyEntity(entity);
            simulation.RunTick(new ReduceOptions { MaxMilliseconds = 0 });
            var rejected = new Resource();
            simulation.Emit(entity, new ResourceFact(rejected));
            simulation.Dispose();
            Assert.AreEqual(0, rejected.Disposals);
        }

        [Test]
        public void ResourceFactPipelineAllocatesZeroBytesFor512EntitiesAfterWarmup()
        {
            const int count = 512;
            using var simulation = new FactSimulation(new ResourceFeature(), new CascadeSettings(count, 1, 1)
            {
                MaxMillisecondsPerStep = 0
            });
            var entities = new EntityRef[count];
            var resources = new Resource[count];
            for (var i = 0; i < count; i++)
            {
                entities[i] = simulation.CreateEntity();
                resources[i] = new Resource();
            }
            RunResourceTick(simulation, entities, resources);
            var allocated = AllocationProbe.Count(() => RunResourceTick(simulation, entities, resources));
            Assert.AreEqual(0, allocated);
            for (var i = 0; i < count; i++) Assert.AreEqual(1, resources[i].Disposals);
            TestContext.WriteLine($"Resource-owning fact pipeline for 512 entities: steady-state={allocated} allocation events.");
        }

        private static void RunResourceTick(FactSimulation simulation, EntityRef[] entities, Resource[] resources)
        {
            for (var i = 0; i < entities.Length; i++)
            {
                resources[i].Disposals = 0;
                simulation.Emit(entities[i], new ResourceFact(resources[i]));
            }
            simulation.RunTick();
        }

        private sealed class Resource
        {
            internal int Disposals;
            internal bool ThrowOnDispose;
            internal bool ThrowOnReduce;
            internal bool ThrowOnCommit;
            internal Action? OnDispose;

            internal void Dispose()
            {
                Disposals++;
                OnDispose?.Invoke();
                if (ThrowOnDispose) throw new InvalidOperationException("disposal failed");
            }
        }

        private readonly struct ResourceFact : IFact<ResourceFact>
        {
            internal ResourceFact(Resource resource) => Resource = resource;
            internal Resource Resource { get; }
            public bool Equals(ResourceFact other) => ReferenceEquals(Resource, other.Resource);
            public void Dispose() => Resource.Dispose();
        }

        private readonly struct OtherResourceFact : IFact<OtherResourceFact>
        {
            internal OtherResourceFact(Resource resource) => Resource = resource;
            internal Resource Resource { get; }
            public bool Equals(OtherResourceFact other) => ReferenceEquals(Resource, other.Resource);
            public void Dispose() => Resource.Dispose();
        }

        private readonly struct ResourceState : IOutputState<ResourceState>, IDisposable
        {
            private readonly Resource? _resource;
            internal ResourceState(int value, Resource? resource = null)
            {
                Value = value;
                _resource = resource;
            }
            internal int Value { get; }
            public bool Equals(ResourceState other) => Value == other.Value;
            public void Dispose() => _resource?.Dispose();
        }

        private sealed class ResourceFeature : FactFeature
        {
            internal ResourceFeature()
            {
                Reduce<ResourceFact>().With<ResourceReducer>();
                Result = Output<ResourceState>("Resource")
                    .AffectedBy<ResourceFact>(0)
                    .AffectedBy<OtherResourceFact>(0)
                    .CommitWith<ResourceCommitter>();
            }

            internal OutputState<ResourceState> Result { get; }
        }

        private sealed class ResourceReducer : IFactReducer<ResourceFact>, IDisposable
        {
            internal static Resource Teardown = new Resource();
            public void Dispose() => Teardown.Dispose();
            public void Reduce(IReduceContext context, EntityRef entity, in ResourceFact fact)
            {
                if (fact.Resource.ThrowOnReduce) throw new InvalidOperationException("reducer failed");
            }
        }

        private sealed class ResourceCommitter : IOutputCommitter<ResourceState>, IDisposable
        {
            internal static Resource Teardown = new Resource();
            public void Dispose() => Teardown.Dispose();
            public CommitDecision<ResourceState> Commit(ICommitContext context, EntityRef entity, in Optional<ResourceState> previous)
            {
                var facts = context.Facts(entity).All<ResourceFact>();
                for (var i = 0; i < facts.Length; i++)
                {
                    if (facts[i].Resource.Disposals != 0) throw new InvalidOperationException("fact disposed before commit");
                    if (facts[i].Resource.ThrowOnCommit) throw new InvalidOperationException("committer failed");
                }
                return CommitDecision<ResourceState>.Set(new ResourceState(facts.Length));
            }
        }

        private sealed class ParentFeature : FactFeature
        {
            internal ParentFeature()
            {
                Child = new ResourceFeature();
                SubFeature(Child);
                ReduceWhen<ResourceFact, OtherResourceFact>().With<TransactionReducer>();
                ReduceBatchWhen<ResourceFact, OtherResourceFact>().With<BatchReducer>();
                ReduceState<ResourceState, StateReducer>();
            }
            internal ResourceFeature Child { get; }
        }

        private sealed class TransactionReducer : ITransactionalReducer, IDisposable
        {
            internal static Resource Teardown = new Resource();
            public void Dispose() => Teardown.Dispose();
            public void Reduce(IReduceContext context, EntityRef entity) { }
        }

        private sealed class BatchReducer : IBatchTransactionalReducer, IDisposable
        {
            internal static Resource Teardown = new Resource();
            public void Dispose() => Teardown.Dispose();
            public void ReduceBatch(IReduceContext context, ReadOnlySpan<EntityRef> entities) { }
        }

        private sealed class StateReducer : ITransactionalReducer, IDisposable
        {
            internal static Resource Teardown = new Resource();
            public void Dispose() => Teardown.Dispose();
            public void Reduce(IReduceContext context, EntityRef entity) { }
        }
    }
}
