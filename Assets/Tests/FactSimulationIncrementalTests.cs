#nullable enable

using System;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    public sealed class FactSimulationIncrementalTests
    {
        [Test]
        public void IncrementalTickDoesNotCommitUntilReductionCloses()
        {
            var feature = new IncrementalFeature();
            var simulation = new FactSimulation(feature);
            var entity = simulation.CreateEntity();
            var options = new ReduceOptions
            {
                MaxFacts = 1,
                MaxMilliseconds = 0
            };

            simulation.Emit(entity, new IncrementalStartFact(7));

            Assert.IsFalse(simulation.RunTickIncremental(options, out var first));
            Assert.IsFalse(first.Complete);
            Assert.AreEqual("maximum fact budget exceeded", first.BudgetReason);
            Assert.AreEqual(1, first.ProcessedFacts);
            Assert.IsFalse(simulation.TryGet<IncrementalResultState>(entity, out _));

            Assert.IsFalse(simulation.RunTickIncremental(options, out var second));
            Assert.IsFalse(second.Complete);
            Assert.AreEqual(2, second.ProcessedFacts);
            Assert.IsFalse(simulation.TryGet<IncrementalResultState>(entity, out _));

            Assert.IsTrue(simulation.RunTickIncremental(options, out var third));
            Assert.IsTrue(third.Complete);
            Assert.AreEqual(3, third.ProcessedFacts);
            Assert.AreEqual(1, third.MutationCount);
            Assert.IsTrue(simulation.TryGet<IncrementalResultState>(entity, out var state));
            Assert.AreEqual(9, state.Value);
        }

        [Test]
        public void FullTickBudgetFailureIncludesActionableContext()
        {
            var feature = new IncrementalFeature();
            var simulation = new FactSimulation(feature);
            var entity = simulation.CreateEntity();

            simulation.Emit(entity, new IncrementalStartFact(3));

            var exception = Assert.Throws<CascadeReductionException>(
                () => simulation.RunTick(new ReduceOptions
                {
                    MaxFacts = 1,
                    MaxMilliseconds = 0
                }));

            Assert.AreEqual("maximum fact budget exceeded", exception.BudgetReason);
            Assert.AreEqual(nameof(IncrementalStartFact), exception.FactName);
            Assert.AreEqual(entity, exception.Entity);
            Assert.AreEqual(nameof(IncrementalStartReducer), exception.ReducerName);
            Assert.IsFalse(simulation.TryGet<IncrementalResultState>(entity, out _));
            Assert.AreEqual(0, simulation.MutationCount);
        }

        [Test]
        public void CausalDepthFailureIncludesEmittedFactAndReducerContext()
        {
            var feature = new IncrementalFeature();
            var simulation = new FactSimulation(feature);
            var entity = simulation.CreateEntity();

            simulation.Emit(entity, new IncrementalStartFact(3));

            var exception = Assert.Throws<CascadeReductionException>(
                () => simulation.RunTick(new ReduceOptions
                {
                    MaxMilliseconds = 0,
                    Guardrails = new FactGuardrails
                    {
                        MaxCausalDepth = 0
                    }
                }));

            Assert.AreEqual("fact acceptance guardrail failed", exception.BudgetReason);
            Assert.AreEqual(nameof(IncrementalMiddleFact), exception.FactName);
            Assert.AreEqual(entity, exception.Entity);
            Assert.AreEqual(1, exception.CausalDepth);
            Assert.AreEqual(nameof(IncrementalStartReducer), exception.ReducerName);
            Assert.IsNotNull(exception.InnerException);
            Assert.IsFalse(simulation.TryGet<IncrementalResultState>(entity, out _));
        }

        private sealed class IncrementalFeature : FactFeature
        {
            public IncrementalFeature()
            {
                Reduce<IncrementalStartFact>()
                    .With<IncrementalStartReducer>();

                Reduce<IncrementalMiddleFact>()
                    .With<IncrementalMiddleReducer>();

                Result = Output<IncrementalResultState>("IncrementalResult")
                    .AffectedBy<IncrementalDoneFact>()
                    .CommitWith<IncrementalCommitter>();
            }

            public OutputState<IncrementalResultState> Result { get; }
        }

        private sealed class IncrementalStartReducer : IFactReducer<IncrementalStartFact>
        {
            public void Reduce(IReduceContext ctx, EntityRef entity, in IncrementalStartFact fact)
            {
                ctx.Emit(entity, new IncrementalMiddleFact(fact.Value + 1));
            }
        }

        private sealed class IncrementalMiddleReducer : IFactReducer<IncrementalMiddleFact>
        {
            public void Reduce(IReduceContext ctx, EntityRef entity, in IncrementalMiddleFact fact)
            {
                ctx.Emit(entity, new IncrementalDoneFact(fact.Value + 1));
            }
        }

        private sealed class IncrementalCommitter : IOutputCommitter<IncrementalResultState>
        {
            public CommitDecision<IncrementalResultState> Commit(
                ICommitContext ctx,
                EntityRef entity,
                in Optional<IncrementalResultState> previous)
            {
                return ctx.Facts(entity).TryGetLatest<IncrementalDoneFact>(out var fact)
                    ? CommitDecision<IncrementalResultState>.Set(new IncrementalResultState(fact.Value))
                    : CommitDecision<IncrementalResultState>.Unchanged();
            }
        }

        private readonly struct IncrementalStartFact : IFact, IEquatable<IncrementalStartFact>
        {
            public IncrementalStartFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(IncrementalStartFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is IncrementalStartFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        private readonly struct IncrementalMiddleFact : IFact, IEquatable<IncrementalMiddleFact>
        {
            public IncrementalMiddleFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(IncrementalMiddleFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is IncrementalMiddleFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        private readonly struct IncrementalDoneFact : IFact, IEquatable<IncrementalDoneFact>
        {
            public IncrementalDoneFact(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(IncrementalDoneFact other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is IncrementalDoneFact other && Equals(other);

            public override int GetHashCode()
                => Value;

            public void Dispose()
            {
            }
        }

        private readonly struct IncrementalResultState : IOutputState, IEquatable<IncrementalResultState>
        {
            public IncrementalResultState(int value)
            {
                Value = value;
            }

            public int Value { get; }

            public bool Equals(IncrementalResultState other)
                => Value == other.Value;

            public override bool Equals(object? obj)
                => obj is IncrementalResultState other && Equals(other);

            public override int GetHashCode()
                => Value;
        }
    }
}
