#nullable enable

using System;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    public sealed class DenseEntityStorageTests
    {
        [Test]
        public void DenseEntitySetTracksEachEntityOnceAndClearsMembership()
        {
            var set = new DenseEntitySet(initialEntityCapacity: 1);
            var entity = new EntityRef(3);

            Assert.IsTrue(set.Add(entity));
            Assert.IsFalse(set.Add(entity));
            Assert.AreEqual(1, set.Count);
            Assert.IsTrue(set.Contains(entity));
            Assert.GreaterOrEqual(set.Capacity, 4);
            Assert.AreEqual(entity, set[0]);

            var copy = new EntityRefBuffer(initialCapacity: 1);
            set.CopyTo(copy, out var copiedCount);
            Assert.AreEqual(1, copiedCount);
            Assert.AreEqual(entity, copy[0]);

            set.Clear();

            Assert.AreEqual(0, set.Count);
            Assert.IsFalse(set.Contains(entity));
            Assert.IsTrue(set.Add(entity));
        }

        [Test]
        public void DenseEntityCounterClearsOnlyTouchedEntities()
        {
            var counter = new DenseEntityCounter(initialEntityCapacity: 1);
            var touched = new DenseEntitySet(initialEntityCapacity: 1);
            var first = new EntityRef(1);
            var second = new EntityRef(4);

            touched.Add(first);
            touched.Add(second);

            Assert.AreEqual(1, counter.Increment(first));
            Assert.AreEqual(2, counter.Increment(first));
            Assert.AreEqual(1, counter.Increment(second));
            Assert.AreEqual(2, counter.Get(first));
            Assert.AreEqual(1, counter.Get(second));

            counter.Clear(touched);

            Assert.AreEqual(0, counter.Get(first));
            Assert.AreEqual(0, counter.Get(second));
        }

        [Test]
        public void DenseEntityObjectStoreCreatesOnceAndRespectsPreCapacity()
        {
            var created = 0;
            var store = new DenseEntityObjectStore<object>(
                () =>
                {
                    created++;
                    return new object();
                },
                initialEntityCapacity: 1);
            var entity = new EntityRef(5);

            store.EnsureCapacity(6);
            var capacity = store.Capacity;

            var first = store.GetOrCreate(entity);
            var second = store.GetOrCreate(entity);

            Assert.AreSame(first, second);
            Assert.AreEqual(1, created);
            Assert.AreEqual(capacity, store.Capacity);
            Assert.IsTrue(store.TryGet(entity, out var stored));
            Assert.AreSame(first, stored);
        }

        [Test]
        public void EntityRefBufferRespectsPreCapacityAndCreatesQueryResultView()
        {
            var buffer = new EntityRefBuffer(initialCapacity: 1);
            buffer.EnsureCapacity(4);
            var capacity = buffer.Capacity;

            buffer[0] = new EntityRef(2);
            buffer[1] = new EntityRef(3);

            var result = buffer.ToQueryResult(count: 2);

            Assert.AreEqual(capacity, buffer.Capacity);
            Assert.AreEqual(2, result.Count);
            Assert.AreEqual(new EntityRef(2), result[0]);
            Assert.AreEqual(new EntityRef(3), result[1]);
            Assert.AreEqual(2, buffer.AsSpan(2).Length);
        }

        [Test]
        public void FactBucketTypedSlabsPreserveSpansAcrossGrowthAndReuse()
        {
            DenseStorageDisposableFact.DisposeCount = 0;
            var facts = new FactBucket<DenseStorageDisposableFact>(
                CascadeTypeId.FromName(nameof(DenseStorageDisposableFact)),
                entityCapacity: 2,
                factCapacityPerEntity: 1,
                factListCapacityMode: FactListCapacityMode.GrowOnDemand);
            var first = new EntityRef(0);
            var second = new EntityRef(1);

            Assert.AreEqual(0, facts.Add(first, new DenseStorageDisposableFact(10)));
            Assert.AreEqual(1, facts.Add(first, new DenseStorageDisposableFact(20)));
            Assert.AreEqual(0, facts.Add(second, new DenseStorageDisposableFact(30)));

            var firstSpan = facts.All(first);
            var secondSpan = facts.All(second);
            Assert.AreEqual(2, firstSpan.Length);
            Assert.AreEqual(10, firstSpan[0].Value);
            Assert.AreEqual(20, firstSpan[1].Value);
            Assert.AreEqual(1, secondSpan.Length);
            Assert.AreEqual(30, secondSpan[0].Value);
            Assert.AreEqual(2, facts.ActiveSlabCount);
            Assert.AreEqual(2, facts.MinimumFactListCapacity(entityCapacity: 2));

            facts.Clear();

            Assert.AreEqual(0, facts.CountFor(first));
            Assert.AreEqual(0, facts.CountFor(second));
            Assert.AreEqual(0, facts.ActiveSlabCount);
            Assert.AreEqual(3, DenseStorageDisposableFact.DisposeCount);

            Assert.AreEqual(0, facts.Add(first, new DenseStorageDisposableFact(40)));
            Assert.AreEqual(1, facts.ActiveSlabCount);
            Assert.IsTrue(facts.TryGetLatest(first, out var latest));
            Assert.AreEqual(40, latest.Value);
        }

        [Test]
        public void FactBucketFixedSlabRejectsUnexpectedGrowthBeforeWrite()
        {
            var facts = new FactBucket<DenseStorageDisposableFact>(
                CascadeTypeId.FromName(nameof(DenseStorageDisposableFact)),
                entityCapacity: 1,
                factCapacityPerEntity: 1,
                factListCapacityMode: FactListCapacityMode.Fixed);
            var entity = new EntityRef(0);

            facts.Add(entity, new DenseStorageDisposableFact(10));

            Assert.Throws<InvalidOperationException>(
                () => facts.Add(entity, new DenseStorageDisposableFact(20)));
            Assert.AreEqual(1, facts.CountFor(entity));
            Assert.AreEqual(10, facts.All(entity)[0].Value);
            Assert.AreEqual(1, facts.MinimumFactListCapacity(entityCapacity: 1));
        }

        private readonly struct DenseStorageDisposableFact : IFact, IEquatable<DenseStorageDisposableFact>
        {
            internal static int DisposeCount;

            internal DenseStorageDisposableFact(int value)
            {
                Value = value;
            }

            internal int Value { get; }

            public bool Equals(DenseStorageDisposableFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is DenseStorageDisposableFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
                => DisposeCount++;
        }
    }
}
