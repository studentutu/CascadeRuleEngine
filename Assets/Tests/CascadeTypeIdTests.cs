#nullable enable

using System;
using CascadeEngineApi;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    public sealed class CascadeTypeIdTests
    {
        [Test]
        public void ValidTypeIdsRouteReducersAndCommitters()
        {
            var feature = new TypeIdRoutingFeature();
            var simulation = new FactSimulation(feature);
            var entity = simulation.CreateEntity();

            simulation.Emit(entity, new TypeIdStartFact(12));
            var result = simulation.RunTick(ReduceOptions.Default());

            Assert.IsTrue(result.Complete);
            Assert.AreEqual(2, result.AcceptedFacts);
            Assert.AreEqual(1, result.ReducerInvocations);
            Assert.AreEqual(1, result.MutationCount);
            Assert.IsTrue(simulation.TryGet<TypeIdResultState>(entity, out var state));
            Assert.AreEqual(24, state.Value);

            var mutations = 0;
            simulation.ForEachMutation(
                feature.Result,
                (EntityRef mutatedEntity, in StateMutation<TypeIdResultState> mutation) =>
                {
                    mutations++;
                    Assert.AreEqual(entity, mutatedEntity);
                    Assert.IsTrue(mutation.HasNext);
                    Assert.AreEqual(24, mutation.Next.Value);
                });

            Assert.AreEqual(1, mutations);
        }

        [Test]
        public void DuplicateFactIdsFailDuringFeatureValidation()
        {
            var exception = Assert.Throws<InvalidOperationException>(() => new DuplicateFactIdFeature());

            StringAssert.Contains("Cascade type name", exception.Message);
        }

        [Test]
        public void DuplicateOutputIdsFailDuringFeatureValidation()
        {
            var exception = Assert.Throws<InvalidOperationException>(() => new DuplicateOutputIdFeature());

            StringAssert.Contains("Cascade type name", exception.Message);
        }

        [Test]
        public void NameTokensCreateDeterministicNonEmptyIntIds()
        {
            var first = CascadeTypeId.FromName(nameof(TypeIdStartFact));
            var second = CascadeTypeId.FromName(nameof(TypeIdStartFact));
            var different = CascadeTypeId.FromName(nameof(TypeIdDerivedFact));

            Assert.AreEqual(first, second);
            Assert.AreNotEqual(first, different);
            Assert.IsFalse(first.IsEmpty);
            Assert.AreNotEqual(0, first.ToInt());
        }

        private sealed class TypeIdRoutingFeature : FactFeature
        {
            public TypeIdRoutingFeature()
            {
                Reduce<TypeIdStartFact>()
                    .With<TypeIdStartReducer>();

                Result = Output<TypeIdResultState>("TypeIdResult")
                    .AffectedBy<TypeIdDerivedFact>(0)
                    .CommitWith<TypeIdResultCommitter>();
            }

            public OutputState<TypeIdResultState> Result { get; }
        }

        private sealed class TypeIdStartReducer : IFactReducer<TypeIdStartFact>
        {
            public void Reduce(IReduceContext ctx, EntityRef entity, in TypeIdStartFact fact)
            {
                ctx.Emit(entity, new TypeIdDerivedFact(fact.Value * 2));
            }
        }

        private sealed class TypeIdResultCommitter : IOutputCommitter<TypeIdResultState>
        {
            public CommitDecision<TypeIdResultState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<TypeIdResultState> previous)
            {
                return ctx.Facts(entity).TryGetLatest<TypeIdDerivedFact>(out var fact)
                    ? CommitDecision<TypeIdResultState>.Set(new TypeIdResultState(fact.Value))
                    : CommitDecision<TypeIdResultState>.Unchanged();
            }
        }

        private sealed class DuplicateFactIdFeature : FactFeature
        {
            public DuplicateFactIdFeature()
            {
                Reduce<DuplicateFactSourceA.SameNameFact>()
                    .With<DuplicateFactAReducer>();

                Reduce<DuplicateFactSourceB.SameNameFact>()
                    .With<DuplicateFactBReducer>();
            }
        }

        private sealed class DuplicateOutputIdFeature : FactFeature
        {
            public DuplicateOutputIdFeature()
            {
                Output<DuplicateOutputStateSourceA.SameNameState>("DuplicateA")
                    .AffectedBy<DuplicateOutputTriggerFact>(0)
                    .CommitWith<DuplicateOutputCommitterA>();

                Output<DuplicateOutputStateSourceB.SameNameState>("DuplicateB")
                    .AffectedBy<DuplicateOutputTriggerFact>(0)
                    .CommitWith<DuplicateOutputCommitterB>();
            }
        }

        private sealed class DuplicateFactAReducer : IFactReducer<DuplicateFactSourceA.SameNameFact>
        {
            public void Reduce(IReduceContext ctx, EntityRef entity, in DuplicateFactSourceA.SameNameFact fact)
            {
            }
        }

        private sealed class DuplicateFactBReducer : IFactReducer<DuplicateFactSourceB.SameNameFact>
        {
            public void Reduce(IReduceContext ctx, EntityRef entity, in DuplicateFactSourceB.SameNameFact fact)
            {
            }
        }

        private sealed class DuplicateOutputCommitterA : IOutputCommitter<DuplicateOutputStateSourceA.SameNameState>
        {
            public CommitDecision<DuplicateOutputStateSourceA.SameNameState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<DuplicateOutputStateSourceA.SameNameState> previous)
            {
                return CommitDecision<DuplicateOutputStateSourceA.SameNameState>.Unchanged();
            }
        }

        private sealed class DuplicateOutputCommitterB : IOutputCommitter<DuplicateOutputStateSourceB.SameNameState>
        {
            public CommitDecision<DuplicateOutputStateSourceB.SameNameState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<DuplicateOutputStateSourceB.SameNameState> previous)
            {
                return CommitDecision<DuplicateOutputStateSourceB.SameNameState>.Unchanged();
            }
        }

        private readonly struct TypeIdStartFact : IFact, IEquatable<TypeIdStartFact>
        {
            public TypeIdStartFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(TypeIdStartFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is TypeIdStartFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        private readonly struct TypeIdDerivedFact : IFact, IEquatable<TypeIdDerivedFact>
        {
            public TypeIdDerivedFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(TypeIdDerivedFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is TypeIdDerivedFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        private readonly struct TypeIdResultState : IOutputState, IEquatable<TypeIdResultState>
        {
            public TypeIdResultState(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(TypeIdResultState other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is TypeIdResultState other && Equals(other);

            public override int GetHashCode()
                => Value;
        }

        private static class DuplicateFactSourceA
        {
            internal readonly struct SameNameFact : IFact, IEquatable<SameNameFact>
            {
                public bool Equals(SameNameFact other)
                    => true;

                public override bool Equals(object? obj)
                    => obj is SameNameFact;

                public override int GetHashCode()
                    => 0;

                public void Dispose()
                {
                }
            }
        }

        private static class DuplicateFactSourceB
        {
            internal readonly struct SameNameFact : IFact, IEquatable<SameNameFact>
            {
                public bool Equals(SameNameFact other)
                    => true;

                public override bool Equals(object? obj)
                    => obj is SameNameFact;

                public override int GetHashCode()
                    => 0;

                public void Dispose()
                {
                }
            }
        }

        private readonly struct DuplicateOutputTriggerFact : IFact, IEquatable<DuplicateOutputTriggerFact>
        {
            public bool Equals(DuplicateOutputTriggerFact other)
                => true;

            public override bool Equals(object? obj)
                => obj is DuplicateOutputTriggerFact;

            public override int GetHashCode()
                => 0;

            public void Dispose()
            {
            }
        }

        private static class DuplicateOutputStateSourceA
        {
            internal readonly struct SameNameState : IOutputState, IEquatable<SameNameState>
            {
                public bool Equals(SameNameState other)
                    => true;

                public override bool Equals(object? obj)
                    => obj is SameNameState;

                public override int GetHashCode()
                    => 0;
            }
        }

        private static class DuplicateOutputStateSourceB
        {
            internal readonly struct SameNameState : IOutputState, IEquatable<SameNameState>
            {
                public bool Equals(SameNameState other)
                    => true;

                public override bool Equals(object? obj)
                    => obj is SameNameState;

                public override int GetHashCode()
                    => 0;
            }
        }
    }
}
