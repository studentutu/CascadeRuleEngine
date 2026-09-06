#nullable enable

using System;
using System.Diagnostics;
using System.Threading;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    public sealed class FactSimulationOverhaulTests
    {
        [SetUp]
        public void Reset()
        {
            SlowCommitter.Calls = 0;
            SlowCommitter.Delay = 0;
            RoutedReducer.Calls = 0;
            RoutedBatch.Calls = 0;
            RoutedBatch.Entities = 0;
        }

        [Test]
        public void ReconciliationSuspendsWithoutPublicationAndReplansOnlyAcceptedInput()
        {
            using var simulation = new FactSimulation(new SlowFeature(), new CascadeSettings(4, 4, 4));
            var entity = simulation.CreateEntity();
            simulation.SetStateSilently(entity, new SlowState(10));
            simulation.Emit(entity, new SlowFact(1));
            SlowCommitter.Delay = 8;
            var slice = new ReduceOptions { MaxMilliseconds = 1 };
            Assert.IsFalse(simulation.RunTickIncremental(slice, out var partial));
            Assert.AreEqual(0, partial.ProcessedWorkItems);
            Assert.AreEqual(10, simulation.Get<SlowState>(entity).Value);
            Assert.AreEqual(0, simulation.MutationCount);
            simulation.Emit(entity, new SlowFact(1));
            SlowCommitter.Delay = 0;
            Assert.IsTrue(simulation.RunTickIncremental(new ReduceOptions { MaxMilliseconds = 0 }, out _));
            Assert.AreEqual(1, SlowCommitter.Calls, "A deduplicated input must not invalidate the plan.");
            Assert.AreEqual(1, simulation.Get<SlowState>(entity).Value);

            simulation.Emit(entity, new SlowFact(2));
            SlowCommitter.Delay = 8;
            Assert.IsFalse(simulation.RunTickIncremental(slice, out _));
            Assert.AreEqual(0, simulation.MutationCount, "Beginning an open tick clears the previous journal.");
            simulation.Emit(entity, new SlowFact(3));
            Assert.IsFalse(simulation.RunTickIncremental(slice, out _));
            Assert.AreEqual(1, simulation.Get<SlowState>(entity).Value);
            var child = simulation.CreateEntity();
            simulation.Emit(child, new SlowFact(7));
            simulation.DestroyEntity(entity);
            SlowCommitter.Delay = 0;
            Complete(simulation, new ReduceOptions { MaxMilliseconds = 0 });
            Assert.IsFalse(simulation.Has<SlowState>(entity));
            Assert.AreEqual(7, simulation.Get<SlowState>(child).Value);
            Assert.AreEqual(2, simulation.MutationCount);
        }

        [Test]
        public void FullTickReconciliationTimeFailurePreservesSnapshot()
        {
            using var simulation = new FactSimulation(new SlowFeature());
            var entity = simulation.CreateEntity();
            simulation.SetStateSilently(entity, new SlowState(10));
            simulation.Emit(entity, new SlowFact(1));
            SlowCommitter.Delay = 8;
            Assert.Throws<CascadeReductionException>(() => simulation.RunTick(new ReduceOptions { MaxMilliseconds = 1 }));
            Assert.AreEqual(10, simulation.Get<SlowState>(entity).Value);
            Assert.AreEqual(0, simulation.MutationCount);
        }

        [Test]
        public void NegativeSealingSurvivesReconciliationPause()
        {
            using var simulation = new FactSimulation(new SlowFeature(true));
            var entity = simulation.CreateEntity();
            simulation.Emit(entity, new SlowFact(1));
            SlowCommitter.Delay = 8;
            Assert.IsFalse(simulation.RunTickIncremental(new ReduceOptions { MaxMilliseconds = 1 }, out _));
            Assert.Throws<InvalidOperationException>(() => simulation.Emit(entity, new SlowFact(2)));
            Assert.Throws<InvalidOperationException>(() => simulation.CreateEntity());
            Assert.Throws<InvalidOperationException>(() => simulation.DestroyEntity(entity));
            SlowCommitter.Delay = 0;
            Complete(simulation, new ReduceOptions { MaxMilliseconds = 0 });
            Assert.AreEqual(1, simulation.Get<SlowState>(entity).Value);
        }

        [TestCase(0)]
        [TestCase(300)]
        public void RoutedSchedulingIgnoresUnrelatedRulesAndResumesWithOneWorkItem(int unrelated)
        {
            const int count = 512;
            using var simulation = new FactSimulation(new RoutedFeature(unrelated), new CascadeSettings(count, 4, 1));
            for (var i = 0; i < count; i++)
            {
                var entity = simulation.CreateEntity();
                simulation.Emit(entity, new RouteLeft());
                simulation.Emit(entity, new RouteRight());
            }
            var result = Complete(simulation, new ReduceOptions { MaxMilliseconds = 0, MaxWorkItems = 1 });
            Assert.AreEqual(count, RoutedReducer.Calls);
            Assert.AreEqual(1, RoutedBatch.Calls);
            Assert.AreEqual(count, RoutedBatch.Entities);
            Assert.AreEqual(count + 1, result.ProcessedWorkItems);
            Assert.AreEqual(count * 2, simulation.EligibilityChecks);
            TestContext.WriteLine($"Routed registrations={unrelated + 1} per stage; eligibility={simulation.EligibilityChecks}; candidate bytes={simulation.CandidateReservedBytes}");
        }

        [Test]
        public void LateRequiredFactRequeuesIncompleteCandidateWithoutRefiringCompletedEntities()
        {
            using var simulation = new FactSimulation(new RoutedFeature(0));
            var first = simulation.CreateEntity();
            var second = simulation.CreateEntity();
            simulation.Emit(first, new RouteLeft());
            simulation.Emit(second, new RouteLeft());
            simulation.Emit(second, new RouteRight());
            Assert.IsFalse(simulation.RunTickIncremental(new ReduceOptions { MaxMilliseconds = 0, MaxWorkItems = 1 }, out _));
            simulation.Emit(first, new RouteRight());
            Complete(simulation, new ReduceOptions { MaxMilliseconds = 0, MaxWorkItems = 1 });
            Assert.AreEqual(2, RoutedReducer.Calls);
            Assert.AreEqual(2, RoutedBatch.Entities);
        }

        [Test]
        public void SettingsLimitsCannotBeRelaxedByPerCallOverrides()
        {
            using var simulation = new FactSimulation(new SlowFeature(), new CascadeSettings(1, 1, 1));
            var entity = simulation.CreateEntity();
            simulation.Emit(entity, new SlowFact(1));
            SlowCommitter.Delay = 8;
            Assert.IsFalse(simulation.RunTickIncremental(new ReduceOptions { MaxMilliseconds = 1 }, out _));
            Assert.Throws<CascadeReductionException>(() => simulation.Emit(entity, new SlowFact(2)));
            Assert.AreEqual(1, simulation.Facts(entity).All<SlowFact>().Length);
            Assert.Throws<InvalidOperationException>(() => simulation.CreateEntity());
            SlowCommitter.Delay = 0;
            Complete(simulation, new ReduceOptions { MaxMilliseconds = 0 });
            Assert.AreEqual(1, simulation.Get<SlowState>(entity).Value);
        }

        [Test]
        public void LegacyEntityGrowthPreservesPendingBatchRowsAndMembershipBoundary()
        {
            using var simulation = new FactSimulation(new RoutedFeature(0, true));
            for (var i = 0; i < 64; i++)
            {
                var entity = simulation.CreateEntity();
                simulation.Emit(entity, new RouteLeft());
                simulation.Emit(entity, new RouteRight());
            }
            Assert.IsFalse(simulation.RunTickIncremental(new ReduceOptions { MaxMilliseconds = 0, MaxWorkItems = 64 }, out _));
            var created = simulation.CreateEntity();
            simulation.Emit(created, new RouteLeft());
            simulation.Emit(created, new RouteRight());
            Complete(simulation, new ReduceOptions { MaxMilliseconds = 0, MaxWorkItems = 1 });
            Assert.AreEqual(65, RoutedReducer.Calls);
            Assert.AreEqual(4, RoutedBatch.Calls);
            Assert.AreEqual(130, RoutedBatch.Entities);
        }

        [Test]
        public void FixedCapacityEntityChurnAllocatesNothingAndKeepsRecycledSlotsBounded()
        {
            const int count = 512;
            using var simulation = new FactSimulation(new SlowFeature(), new CascadeSettings(count, 1, 1));
            var entities = new EntityRef[count];
            var options = new ReduceOptions { MaxMilliseconds = 0 };
            Action churn = () =>
            {
                for (var i = 0; i < count; i++) entities[i] = simulation.CreateEntity();
                for (var i = 0; i < count; i++) simulation.DestroyEntity(entities[i]);
                simulation.RunTick(options);
            };
            churn();
            var allocated = AllocationProbe.Count(() => { for (var i = 0; i < 8; i++) churn(); });
            Assert.AreEqual(0, allocated);
            for (var i = 0; i < count; i++)
            {
                Assert.Less(entities[i].Value, count);
                Assert.IsFalse(simulation.TryGetEntity(entities[i].Value, out _));
            }
            TestContext.WriteLine($"512-entity lifecycle churn: {allocated} allocation events over 8 create/destroy ticks.");
        }

        [Test]
        public void Fixed512EntityRoutedPipelineHasZeroSteadyStateAllocation()
        {
            const int count = 512;
            using var simulation = new FactSimulation(new RoutedFeature(300), new CascadeSettings(count, 4, 1));
            var entities = new EntityRef[count];
            for (var i = 0; i < count; i++) entities[i] = simulation.CreateEntity();
            var options = new ReduceOptions { MaxMilliseconds = 0 };
            RunLoad(simulation, entities, options);
            RunLoad(simulation, entities, options);
            var started = Stopwatch.GetTimestamp();
            var allocated = AllocationProbe.Count(() =>
            {
                for (var i = 0; i < 8; i++) RunLoad(simulation, entities, options);
            });
            var elapsed = (Stopwatch.GetTimestamp() - started) * 1000d / Stopwatch.Frequency;
            TestContext.WriteLine($"512 entities, 602 routed registrations: {allocated} allocation events over 8 ticks; {elapsed / 8:F3} ms/tick; candidates={simulation.CandidateReservedBytes} B");
            var metrics = simulation.Metrics;
            var milliseconds = 1000d / Stopwatch.Frequency;
            TestContext.WriteLine($"Last tick milliseconds: reducer max={metrics.MaximumReducerTicks * milliseconds:F4}; committer max={metrics.MaximumCommitterTicks * milliseconds:F4}; planning={metrics.PlanningTicks * milliseconds:F4}; validation={metrics.ValidationTicks * milliseconds:F4}; atomic apply={metrics.ApplyTicks * milliseconds:F4}; cleanup={metrics.CleanupTicks * milliseconds:F4}");
            Assert.AreEqual(0, allocated);
        }

        private static void RunLoad(FactSimulation simulation, EntityRef[] entities, ReduceOptions options)
        {
            foreach (var entity in entities)
            {
                simulation.Emit(entity, new RouteLeft());
                simulation.Emit(entity, new RouteRight());
            }
            simulation.RunTick(options);
        }

        private static SimulationResult Complete(FactSimulation simulation, ReduceOptions options)
        {
            for (var i = 0; i < 2048; i++)
                if (simulation.RunTickIncremental(options, out var result)) return result;
            throw new AssertionException("Continuation did not make forward progress.");
        }

        private sealed class SlowFeature : FactFeature
        {
            internal SlowFeature(bool negative = false)
            {
                if (negative) Reduce<SlowFact>().Without<Blocked>().With<NegativeReducer>();
                Output<SlowState>("Slow").AffectedBy<SlowFact>(0).CommitWith<SlowCommitter>();
            }
        }
        private sealed class RoutedFeature : FactFeature
        {
            internal RoutedFeature(int unrelated, bool extraBatch = false)
            {
                ReduceWhen<RouteLeft, RouteRight>().With<RoutedReducer>();
                ReduceBatchWhen<RouteLeft, RouteRight>().With<RoutedBatch>();
                if (extraBatch) ReduceBatchWhen<RouteLeft, RouteRight>().With<RoutedBatch>();
                for (var i = 0; i < unrelated; i++)
                {
                    ReduceWhen<UnrelatedLeft, UnrelatedRight>().With<RoutedReducer>();
                    ReduceBatchWhen<UnrelatedLeft, UnrelatedRight>().With<RoutedBatch>();
                }
                Output<SlowState>("Result").AffectedBy<SlowFact>(0).CommitWith<RoutedCommitter>();
            }
        }
        private sealed class RoutedReducer : ITransactionalReducer
        {
            internal static int Calls;
            public void Reduce(IReduceContext context, EntityRef entity)
            { Calls++; context.Emit(entity, new SlowFact(1)); }
        }
        private sealed class RoutedBatch : IBatchTransactionalReducer
        {
            internal static int Calls;
            internal static int Entities;
            public void ReduceBatch(IReduceContext context, ReadOnlySpan<EntityRef> entities)
            { Calls++; Entities += entities.Length; }
        }
        private sealed class NegativeReducer : IFactReducer<SlowFact>
        {
            public void Reduce(IReduceContext context, EntityRef entity, in SlowFact fact) { }
        }
        private sealed class SlowCommitter : IOutputCommitter<SlowState>
        {
            internal static int Calls;
            internal static int Delay;
            public CommitDecision<SlowState> Commit(ICommitContext context, EntityRef entity, in Optional<SlowState> previous)
            {
                Calls++;
                if (Delay > 0) Thread.Sleep(Delay);
                var sum = 0;
                foreach (var fact in context.Facts(entity).All<SlowFact>()) sum += fact.Value;
                return CommitDecision<SlowState>.Set(new SlowState(sum));
            }
        }
        private sealed class RoutedCommitter : IOutputCommitter<SlowState>
        {
            public CommitDecision<SlowState> Commit(ICommitContext context, EntityRef entity, in Optional<SlowState> previous)
                => CommitDecision<SlowState>.Set(new SlowState(previous.HasValue ? previous.Value.Value + 1 : 1));
        }
        private readonly struct SlowFact : IFact<SlowFact>
        {
            internal SlowFact(int value) => Value = value;
            internal int Value { get; }
            public bool Equals(SlowFact other) => Value == other.Value;
            public void Dispose() { }
        }
        private readonly struct SlowState : IOutputState<SlowState>
        {
            internal SlowState(int value) => Value = value;
            internal int Value { get; }
            public bool Equals(SlowState other) => Value == other.Value;
        }
        private readonly struct RouteLeft : IFact<RouteLeft>
        {
            public bool Equals(RouteLeft other) => true;
            public void Dispose() { }
        }
        private readonly struct RouteRight : IFact<RouteRight>
        {
            public bool Equals(RouteRight other) => true;
            public void Dispose() { }
        }
        private readonly struct UnrelatedLeft : IFact<UnrelatedLeft>
        {
            public bool Equals(UnrelatedLeft other) => true;
            public void Dispose() { }
        }
        private readonly struct UnrelatedRight : IFact<UnrelatedRight>
        {
            public bool Equals(UnrelatedRight other) => true;
            public void Dispose() { }
        }
        private readonly struct Blocked : IFact<Blocked>
        {
            public bool Equals(Blocked other) => true;
            public void Dispose() { }
        }
    }
}
