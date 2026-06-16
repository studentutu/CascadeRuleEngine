#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// [INTEGRATION] Host-facing simulation API: create entities, emit facts, run closure, then route typed output mutations.
    /// </summary>
    public interface IFactSimulation
    {
        ICommittedStateStore State { get; }

        EntityRef CreateEntity();

        void DestroyEntity(EntityRef entity);

        void Emit<TFact>(EntityRef entity, in TFact fact)
            where TFact : struct, IFact;

        SimulationResult RunTick(ReduceOptions options);

        void ForEachMutation<TState>(
            OutputState<TState> output,
            StateMutationHandler<TState> handler)
            where TState : struct, IOutputState;
    }
}
