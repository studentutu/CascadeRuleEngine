#nullable enable

using NUnit.Framework;

namespace CascadeEngineApi.Tests
{
    [Category("Internal")]
    public sealed class FactRoutingTests
    {
        [Test]
        public void UnrelatedRegistrationsDoNotIncreaseEligibilityWork()
        {
            var baseline = Run(0);
            Assert.Greater(baseline, 0, "The control must exercise candidate eligibility.");
            Assert.AreEqual(baseline, Run(300));
        }

        private static long Run(int unrelated)
        {
            using var simulation = new FactSimulation(new RoutingFeature(unrelated), new CascadeSettings(32, 2, 1));
            for (var i = 0; i < 32; i++)
            {
                var entity = simulation.CreateEntity();
                simulation.Emit(entity, new RoutedInput());
                simulation.Emit(entity, new RequiredInput());
            }
            var options = new ReduceOptions { MaxWorkItems = 1, MaxMilliseconds = 0 };
            for (var i = 0; i < 128; i++)
                if (simulation.RunTickIncremental(options, out _)) return simulation.EligibilityChecks;
            throw new AssertionException("Routing did not reach closure.");
        }

        private sealed class RoutingFeature : FactFeature
        {
            internal RoutingFeature(int unrelated)
            {
                ReduceWhen<RoutedInput, RequiredInput>().With<RoutingReducer>();
                for (var i = 0; i < unrelated; i++)
                    ReduceWhen<UnrelatedInput, OtherRequiredInput>().With<RoutingReducer>();
            }
        }

        private sealed class RoutingReducer : ITransactionalReducer
        {
            public void Reduce(IReduceContext context, EntityRef entity) { }
        }

        private readonly struct RoutedInput : IFact<RoutedInput>
        {
            public bool Equals(RoutedInput other) => true;
            public void Dispose() { }
        }

        private readonly struct RequiredInput : IFact<RequiredInput>
        {
            public bool Equals(RequiredInput other) => true;
            public void Dispose() { }
        }

        private readonly struct UnrelatedInput : IFact<UnrelatedInput>
        {
            public bool Equals(UnrelatedInput other) => true;
            public void Dispose() { }
        }

        private readonly struct OtherRequiredInput : IFact<OtherRequiredInput>
        {
            public bool Equals(OtherRequiredInput other) => true;
            public void Dispose() { }
        }
    }
}
