#nullable enable

using System;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    [Category("Internal")]
    public sealed class EntitySparseSetTests
    {
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void SwapBackRemovalRepairsMovedMembershipAndRejectsOldGeneration(int removed)
        {
            var storage = new EntitySparseSet<int>();
            storage.EnsureCapacity(3, 3);
            storage.FreezeCapacity();
            for (var i = 0; i < 3; i++)
            {
                storage.Prepare(new EntityRef(i), i + 1);
                storage.SetPrepared(new EntityRef(i), i * 10);
            }
            storage.RemovePrepared(new EntityRef(removed));
            Assert.AreEqual(2, storage.Count);
            for (var i = 0; i < 3; i++)
            {
                if (i == removed) continue;
                Assert.IsTrue(storage.TryGetIndex(new EntityRef(i), out var row));
                Assert.AreEqual(i * 10, storage.ValueAt(row));
            }
            var next = new EntityRef(removed, 1);
            storage.Prepare(next, 3);
            storage.SetPrepared(next, 99);
            Assert.IsFalse(storage.TryGetIndex(new EntityRef(removed), out _));
            storage.RemovePrepared(new EntityRef(removed));
            Assert.IsTrue(storage.TryGetIndex(next, out _));
            Assert.Throws<InvalidOperationException>(() => storage.Prepare(new EntityRef(removed), 3));
            storage.Clear();
            Assert.AreEqual(0, storage.Count);
            Assert.IsFalse(storage.TryGetIndex(next, out _));
            storage.Prepare(next, 1);
            storage.SetPrepared(next, 1);
            Assert.AreEqual(1, storage.Count);
        }

        [Test]
        public void FailedPreparationDoesNotAcquireMembershipOrReplaceValues()
        {
            var storage = new EntitySparseSet<int>();
            storage.EnsureCapacity(2, 1);
            storage.FreezeCapacity();
            var first = new EntityRef(0);
            storage.Prepare(first, 1);
            storage.SetPrepared(first, 10);
            Assert.Throws<InvalidOperationException>(() => storage.Prepare(new EntityRef(1), 2));
            Assert.AreEqual(1, storage.Count);
            Assert.AreEqual(10, storage.ValueAt(0));
            Assert.IsFalse(storage.TryGetIndex(new EntityRef(1), out _));
        }

        [Test]
        public void MutationCapacityFailureAndStaleActionValidationPrecedeAnyWrite()
        {
            var entities = new EntityStore(2);
            var first = entities.Create(false);
            var second = entities.Create(false);
            var bucket = new StateBucket<ProbeState>(CascadeTypeId.FromName("Probe"), "Probe");
            bucket.EnsureCapacity(2, 1);
            bucket.FreezeCapacity();
            Assert.IsTrue(bucket.Prepare(first, CommitDecision<ProbeState>.Set(new ProbeState(1)), out var action));
            Assert.Throws<InvalidOperationException>(() => bucket.Prepare(second, CommitDecision<ProbeState>.Set(new ProbeState(2)), out _));
            Assert.AreEqual(0, bucket.EntityCount);
            Assert.AreEqual(0, bucket.MutationCount);
            entities.StageDestroy(first);
            entities.CommitTick();
            var replacement = entities.Create(false);
            Assert.AreEqual(first.Value, replacement.Value);
            Assert.Throws<InvalidOperationException>(() => action.Validate(entities));
            Assert.IsFalse(bucket.Has(replacement));
        }

        [Test]
        public void StateCapacityFailureAfterEarlierPreparationLeavesBothBucketsUnchanged()
        {
            var a = new StateBucket<ProbeState>(CascadeTypeId.FromName("A"), "A");
            var b = new StateBucket<ProbeState>(CascadeTypeId.FromName("B"), "B");
            a.EnsureCapacity(2, 2);
            b.EnsureCapacity(1, 1);
            a.FreezeCapacity();
            b.FreezeCapacity();
            var first = new EntityRef(0);
            var second = new EntityRef(1);
            a.SetSilently(first, new ProbeState(10));
            b.SetSilently(first, new ProbeState(20));
            Assert.IsTrue(a.Prepare(first, CommitDecision<ProbeState>.Set(new ProbeState(11)), out _));
            Assert.Throws<InvalidOperationException>(() => b.Prepare(second, CommitDecision<ProbeState>.Set(new ProbeState(21)), out _));
            Assert.AreEqual(10, a.Get(first).Value);
            Assert.AreEqual(20, b.Get(first).Value);
            Assert.AreEqual(0, a.MutationCount);
            Assert.AreEqual(0, b.MutationCount);
        }

        private readonly struct ProbeState : IOutputState<ProbeState>
        {
            internal ProbeState(int value) => Value = value;
            internal int Value { get; }
            public bool Equals(ProbeState other) => Value == other.Value;
        }
    }
}
