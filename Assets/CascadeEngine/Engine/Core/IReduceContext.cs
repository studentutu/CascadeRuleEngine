#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Reducer-facing API: read committed state and accumulated facts, emit more facts, and manage entity lifecycle.
    /// </summary>
    public interface IReduceContext
    {
        SimulationTick Tick { get; }

        IEntityFactView Facts(EntityRef entity);

        bool HasState<TState>(EntityRef entity)
            where TState : struct, IOutputState;

        TState GetState<TState>(EntityRef entity)
            where TState : struct, IOutputState;

        bool TryGetState<TState>(EntityRef entity, out TState state)
            where TState : struct, IOutputState;

        IEntityQuery Query { get; }

        EntityRef CreateEntity();

        bool IsDestroyed(EntityRef entity);

        void DestroyEntity(EntityRef entity);

        void Emit<TFact>(EntityRef entity, in TFact fact)
            where TFact : struct, IFact;

        void EmitGlobal<TFact>(in TFact fact)
            where TFact : struct, IFact;
    }
}
