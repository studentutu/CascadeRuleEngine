#nullable enable

using System;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    [Category("Internal")]
    public sealed class FactStorageTests
    {
        [Test]
        public void BorrowedFactViewSurvivesOtherEntityGrowthUntilClosure()
        {
            var bucket = new FactBucket<BorrowedFact>(CascadeTypeId.FromName(nameof(BorrowedFact)),
                1, 1, FactListCapacityMode.GrowOnDemand);
            var first = new EntityRef(0);
            var second = new EntityRef(8);
            bucket.Add(first, new BorrowedFact(10));
            var borrowed = bucket.All(first);
            bucket.Add(second, new BorrowedFact(20));
            Assert.AreEqual(1, borrowed.Length);
            Assert.AreEqual(10, borrowed[0].Value);
            Assert.AreEqual(20, bucket.All(second)[0].Value);
            bucket.Clear();
            Assert.IsFalse(bucket.Has(first));
            Assert.IsFalse(bucket.Has(second));
            bucket.Add(second, new BorrowedFact(30));
            Assert.AreEqual(30, bucket.All(second)[0].Value);
            bucket.Clear();
        }

        [Test]
        public void FrozenEntityCapacityRejectsBeforeAcquiringFactMembership()
        {
            var bucket = new FactBucket<BorrowedFact>(CascadeTypeId.FromName(nameof(BorrowedFact)),
                1, 1, FactListCapacityMode.Fixed);
            bucket.FreezeCapacity();
            var first = new EntityRef(0);
            var rejected = new EntityRef(1);
            bucket.Add(first, new BorrowedFact(10));
            Assert.Throws<InvalidOperationException>(() => bucket.Add(rejected, new BorrowedFact(20)));
            Assert.IsFalse(bucket.Has(rejected));
            Assert.AreEqual(10, bucket.All(first)[0].Value);
            bucket.Clear();
        }

        private readonly struct BorrowedFact : IFact<BorrowedFact>
        {
            internal BorrowedFact(int value) => Value = value;
            internal int Value { get; }
            public bool Equals(BorrowedFact other) => Value == other.Value;
            public void Dispose() { }
        }
    }
}
