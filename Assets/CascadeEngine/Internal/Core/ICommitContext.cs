#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Committer-facing API: fold closed facts against previous durable state without emitting same-tick work.
    /// </summary>
    public interface ICommitContext
    {
        SimulationTick Tick { get; }

        IEntityFactView Facts(EntityRef entity);

        bool HasState<TState>(EntityRef entity)
            where TState : struct, IOutputState;

        TState GetState<TState>(EntityRef entity)
            where TState : struct, IOutputState;

        bool TryGetState<TState>(EntityRef entity, out TState state)
            where TState : struct, IOutputState;
    }
}
