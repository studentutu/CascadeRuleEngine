#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// [INTEGRATION] Hestia sample CascadeEngine owner.
    /// </summary>
    public sealed class HestiaGameContext
    {
        public HestiaGameContext(CascadeSettings? settings = null)
        {
            Feature = new HestiaGameSimulationFeature();
            Simulation = new FactSimulation(Feature, settings ?? CreateDefaultSettings());
        }

        public HestiaGameSimulationFeature Feature { get; }

        /// <summary>
        ///  [INTEGRATION] Actual runner. Make sure to call it each tick or update.
        /// </summary>
        public FactSimulation Simulation { get; }

        public EntityRef CreateEntity()
            => Simulation.CreateEntity();

        public void DestroyEntity(EntityRef entity)
            => Simulation.DestroyEntity(entity);

        public SimulationResult RunTick()
            => Simulation.RunTick();

        private static CascadeSettings CreateDefaultSettings()
        {
            return new CascadeSettings(
                maxEntities: 512,
                maxFactsPerEntity: 8,
                maxFactsPerTypePerEntity: 2)
            {
                MaxWorkItemsPerStep = 4096,
                MaxWorkItemsPerTick = 8192,
                MaxPasses = 32,
                MaxMillisecondsPerStep = 8,
                MaxCausalDepth = 16
            };
        }
    }
}
