#nullable enable

using System;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    [Category("Internal")]
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
        public void ReservedStatePublicationAndJournalClearAllocateNothing()
        {
            var bucket = new StateBucket<AllocationState>(CascadeTypeId.FromName("AllocationState"), "AllocationState");
            bucket.EnsureCapacity(1, 1);
            bucket.FreezeCapacity();
            var entity = new EntityRef(0);
            var allocations = AllocationProbe.Count(() =>
            {
                bucket.Prepare(entity, CommitDecision<AllocationState>.Set(new AllocationState(1)), out var created);
                created.Apply();
                bucket.ClearPreparation();
                bucket.ClearMutations();
                bucket.Prepare(entity, CommitDecision<AllocationState>.Set(new AllocationState(2)), out var replaced);
                replaced.Apply();
                bucket.ClearPreparation();
                bucket.ClearMutations();
            });
            Assert.AreEqual(0, allocations);
            Assert.AreEqual(2, bucket.Get(entity).Value);
            Assert.AreEqual(0, bucket.MutationCount);
        }

        private readonly struct AllocationState : IOutputState<AllocationState>
        {
            internal AllocationState(int value) => Value = value;
            internal int Value { get; }
            public bool Equals(AllocationState other) => Value == other.Value;
        }

    }
}
