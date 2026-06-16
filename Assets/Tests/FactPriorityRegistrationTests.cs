#nullable enable

using System;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    public sealed class FactPriorityRegistrationTests
    {
        [Test]
        public void ExplicitPriorityResolverControlsPriorityFirstQueueOrder()
        {
            PriorityOrderReducer.Reset();

            var simulation = new FactSimulation(new PriorityOrderFeature());
            try
            {
                var entity = simulation.CreateEntity();

                simulation.Emit(entity, new PriorityOrderFact(1, FactPriority.Low));
                simulation.Emit(entity, new PriorityOrderFact(2, FactPriority.PlayerVisible));

                var result = simulation.RunTick(new ReduceOptions
                {
                    MaxMilliseconds = 0
                });

                Assert.IsTrue(result.Complete);
                Assert.AreEqual(2, result.ReducerInvocations);
                Assert.AreEqual(2, PriorityOrderReducer.ValueAt(0));
                Assert.AreEqual(1, PriorityOrderReducer.ValueAt(1));
            }
            finally
            {
                simulation.Dispose();
            }
        }

        [Test]
        public void SameFactTypeCanUseDifferentPriorityResolversPerFeatureRegistry()
        {
            AssertPriorityOrder(new SecondValuePriorityFeature(), 2, 1);
            AssertPriorityOrder(new FirstValuePriorityFeature(), 1, 2);
        }

        [Test]
        public void SubFeaturePriorityResolverTransfersToParentRegistry()
        {
            var feature = new ParentPriorityFeature();

            Assert.Throws<InvalidOperationException>(() => new FactSimulation(feature.Child));
            AssertPriorityOrder(feature, 2, 1);
        }

        [Test]
        public void PriorityResolverIsDisposedWithFeatureRegistry()
        {
            DisposablePriorityResolver.DisposeCount = 0;

            var simulation = new FactSimulation(new DisposablePriorityFeature());
            simulation.Dispose();
            simulation.Dispose();

            Assert.AreEqual(1, DisposablePriorityResolver.DisposeCount);
        }

        private static void AssertPriorityOrder(FactFeature feature, int expectedFirst, int expectedSecond)
        {
            PriorityOrderReducer.Reset();

            var simulation = new FactSimulation(feature);
            try
            {
                var entity = simulation.CreateEntity();
                simulation.Emit(entity, new PriorityOrderFact(1, FactPriority.Normal));
                simulation.Emit(entity, new PriorityOrderFact(2, FactPriority.Normal));

                var result = simulation.RunTick(new ReduceOptions
                {
                    MaxMilliseconds = 0
                });

                Assert.IsTrue(result.Complete);
                Assert.AreEqual(2, result.ReducerInvocations);
                Assert.AreEqual(expectedFirst, PriorityOrderReducer.ValueAt(0));
                Assert.AreEqual(expectedSecond, PriorityOrderReducer.ValueAt(1));
            }
            finally
            {
                simulation.Dispose();
            }
        }

        private sealed class PriorityOrderFeature : FactFeature
        {
            public PriorityOrderFeature()
            {
                Priority<PriorityOrderFact>()
                    .With<PriorityOrderResolver>();

                Reduce<PriorityOrderFact>()
                    .With<PriorityOrderReducer>();
            }
        }

        private sealed class SecondValuePriorityFeature : FactFeature
        {
            public SecondValuePriorityFeature()
            {
                Priority<PriorityOrderFact>()
                    .With<SecondValuePriorityResolver>();

                Reduce<PriorityOrderFact>()
                    .With<PriorityOrderReducer>();
            }
        }

        private sealed class FirstValuePriorityFeature : FactFeature
        {
            public FirstValuePriorityFeature()
            {
                Priority<PriorityOrderFact>()
                    .With<FirstValuePriorityResolver>();

                Reduce<PriorityOrderFact>()
                    .With<PriorityOrderReducer>();
            }
        }

        private sealed class ParentPriorityFeature : FactFeature
        {
            public readonly ChildPriorityFeature Child = new ChildPriorityFeature();

            public ParentPriorityFeature()
            {
                SubFeature(Child);
            }
        }

        private sealed class ChildPriorityFeature : FactFeature
        {
            public ChildPriorityFeature()
            {
                Priority<PriorityOrderFact>()
                    .With<SecondValuePriorityResolver>();

                Reduce<PriorityOrderFact>()
                    .With<PriorityOrderReducer>();
            }
        }

        private sealed class DisposablePriorityFeature : FactFeature
        {
            public DisposablePriorityFeature()
            {
                Priority<PriorityOrderFact>()
                    .With<DisposablePriorityResolver>();
            }
        }

        private sealed class PriorityOrderResolver : IFactPriorityResolver<PriorityOrderFact>
        {
            public FactPriority Resolve(in PriorityOrderFact fact)
                => fact.Priority;
        }

        private sealed class SecondValuePriorityResolver : IFactPriorityResolver<PriorityOrderFact>
        {
            public FactPriority Resolve(in PriorityOrderFact fact)
                => fact.Value == 2 ? FactPriority.PlayerVisible : FactPriority.Low;
        }

        private sealed class FirstValuePriorityResolver : IFactPriorityResolver<PriorityOrderFact>
        {
            public FactPriority Resolve(in PriorityOrderFact fact)
                => fact.Value == 1 ? FactPriority.PlayerVisible : FactPriority.Low;
        }

        private sealed class DisposablePriorityResolver : IFactPriorityResolver<PriorityOrderFact>, IDisposable
        {
            public static int DisposeCount;

            public FactPriority Resolve(in PriorityOrderFact fact)
                => fact.Priority;

            public void Dispose()
            {
                DisposeCount++;
            }
        }

        private sealed class PriorityOrderReducer : IFactReducer<PriorityOrderFact>
        {
            private const int MaxObservedFacts = 4;
            private static readonly int[] Values = new int[MaxObservedFacts];
            private static int _count;

            public static void Reset()
            {
                Array.Clear(Values, 0, Values.Length);
                _count = 0;
            }

            public static int ValueAt(int index)
                => Values[index];

            public void Reduce(IReduceContext ctx, EntityRef entity, in PriorityOrderFact fact)
            {
                Values[_count] = fact.Value;
                _count++;
            }
        }

        private readonly struct PriorityOrderFact : IFact, IEquatable<PriorityOrderFact>
        {
            public PriorityOrderFact(int value, FactPriority priority)
            {
                Value = value;
                Priority = priority;
            }

            public int Value { get; }
            public FactPriority Priority { get; }

            public bool Equals(PriorityOrderFact other)
                => Value == other.Value && Priority == other.Priority;

            public override bool Equals(object? obj)
                => obj is PriorityOrderFact other && Equals(other);

            public override int GetHashCode()
                => unchecked((Value * 397) ^ (int)Priority);

            public void Dispose()
            {
            }
        }
    }
}
