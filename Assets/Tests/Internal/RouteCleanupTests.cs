#nullable enable

using System;
using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    [Category("Internal")]
    public sealed class RouteCleanupTests
    {
        [TestCase(false)]
        [TestCase(true)]
        public void TerminalDisposalUnbindsRoutesEvenAfterFeatureDisposal(bool disposeFeatureFirst)
        {
            var feature = new CleanupFeature();
            using var simulation = new FactSimulation(feature);
            Assert.DoesNotThrow(() => FactEmitRouteCache<CleanupFact>.Require(feature.Registry));
            Assert.DoesNotThrow(() => OutputStateRouteCache<CleanupState>.Require(simulation));
            if (disposeFeatureFirst) feature.Dispose();
            simulation.Dispose();
            Assert.Throws<InvalidOperationException>(() => FactEmitRouteCache<CleanupFact>.Require(feature.Registry));
            Assert.Throws<InvalidOperationException>(() => OutputStateRouteCache<CleanupState>.Require(simulation));
        }

        [Test]
        public void ThrowingFactCleanupStillUnbindsBothRoutes()
        {
            var feature = new CleanupFeature();
            using var simulation = new FactSimulation(feature);
            simulation.Emit(simulation.CreateEntity(), new CleanupFact());
            Assert.Throws<InvalidOperationException>(() => simulation.Dispose());
            Assert.Throws<InvalidOperationException>(() => FactEmitRouteCache<CleanupFact>.Require(feature.Registry));
            Assert.Throws<InvalidOperationException>(() => OutputStateRouteCache<CleanupState>.Require(simulation));
        }

        private sealed class CleanupFeature : FactFeature
        {
            internal CleanupFeature()
                => Output<CleanupState>("Cleanup").AffectedBy<CleanupFact>(0).CommitWith<CleanupCommitter>();
        }

        private sealed class CleanupCommitter : IOutputCommitter<CleanupState>
        {
            public CommitDecision<CleanupState> Commit(ICommitContext context, EntityRef entity, in Optional<CleanupState> previous)
                => CommitDecision<CleanupState>.Unchanged();
        }

        private readonly struct CleanupFact : IFact<CleanupFact>
        {
            public bool Equals(CleanupFact other) => true;
            public void Dispose() => throw new InvalidOperationException("cleanup failed");
        }

        private readonly struct CleanupState : IOutputState<CleanupState>
        {
            public bool Equals(CleanupState other) => true;
        }
    }
}
