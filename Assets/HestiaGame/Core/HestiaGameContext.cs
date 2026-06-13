#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// [INTEGRATION] Hestia sample CascadeEngine owner.
    /// </summary>
    public sealed class HestiaGameContext
    {
        private readonly ReduceOptions _defaultOptions = ReduceOptions.Default();

        public HestiaGameContext()
        {
            Feature = new HestiaGameSimulationFeature();
            Simulation = new FactSimulation(Feature);
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
            => Simulation.RunTick(_defaultOptions);
    }
}
