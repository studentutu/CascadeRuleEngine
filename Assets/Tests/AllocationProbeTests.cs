#nullable enable

using System;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    public sealed class AllocationProbeTests
    {
        [Test]
        public void AllocationRecorderDetectsKnownArrayAndIgnoresEmptyWork()
        {
            Action empty = () => { };
            Action allocating = () => GC.KeepAlive(new byte[4096]);
            AllocationProbe.Count(empty);
            Assert.Greater(AllocationProbe.Count(allocating), 0, "The allocation instrument must detect known allocations.");
            Assert.AreEqual(0, AllocationProbe.Count(empty));
        }

        [Test]
        public void DisposalProbeDistinguishesInheritedAndExplicitNoOpImplementations()
        {
            var inherited = AllocationProbe.Count(DisposeMany<InheritedNoOpFact>);
            var explicitImplementation = AllocationProbe.Count(DisposeMany<ExplicitNoOpFact>);
            TestContext.WriteLine($"100 constrained Dispose calls: inherited default={inherited} allocation events; explicit struct implementation={explicitImplementation} allocation events.");
            Assert.AreEqual(0, explicitImplementation);
        }

        private static void DisposeMany<TFact>() where TFact : struct, IFact
        {
            for (var i = 0; i < 100; i++)
            {
                var fact = default(TFact);
                fact.Dispose();
            }
        }

        [Test]
        public void PreparedStorageAllocationDiagnosticsSeparateCreateReplaceAndClear()
        {
            var bucket = new StateBucket<AllocationState>(CascadeTypeId.FromName("AllocationState"), "AllocationState");
            bucket.EnsureCapacity(1, 1);
            bucket.FreezeCapacity();
            var entity = new EntityRef(0);
            CommitAction<AllocationState> action = default;
            CommitDecision<AllocationState> decision = default;
            var decisionCreation = AllocationProbe.Count(() => decision = CommitDecision<AllocationState>.Set(new AllocationState(1)));
            StateMutation<AllocationState> mutation = default;
            var mutationCreation = AllocationProbe.Count(() => mutation = new StateMutation<AllocationState>(false, default, true, new AllocationState(1)));
            var actionCreation = AllocationProbe.Count(() => action = new CommitAction<AllocationState>(bucket, entity, mutation));
            var prepare = AllocationProbe.Count(() => bucket.Prepare(entity, decision, out action));
            var apply = AllocationProbe.Count(() => action.Apply());
            bucket.ClearPreparation();
            var clear = AllocationProbe.Count(bucket.ClearMutations);
            var replace = AllocationProbe.Count(() => bucket.Prepare(entity, CommitDecision<AllocationState>.Set(new AllocationState(2)), out action));
            TestContext.WriteLine($"Cold typed state storage allocation events: decision={decisionCreation}; mutation={mutationCreation}; action={actionCreation}; prepare={prepare}; apply={apply}; journal clear={clear}; first equality={replace}.");
            Assert.AreEqual(0, decisionCreation + mutationCreation + actionCreation + prepare + replace);
            Assert.AreEqual(0, apply);
            Assert.AreEqual(0, clear);
        }

        private readonly struct AllocationState : IOutputState<AllocationState>
        {
            internal AllocationState(int value) => Value = value;
            internal int Value { get; }
            public bool Equals(AllocationState other) => Value == other.Value;
        }

        private readonly struct InheritedNoOpFact : IFact<InheritedNoOpFact>
        { public bool Equals(InheritedNoOpFact other) => true; }

        private readonly struct ExplicitNoOpFact : IFact<ExplicitNoOpFact>
        {
            public bool Equals(ExplicitNoOpFact other) => true;
            public void Dispose() { }
        }
    }
}
