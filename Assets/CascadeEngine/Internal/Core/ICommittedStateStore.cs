#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Query-only view of durable output state. Missing state is explicit.
    /// </summary>
    public interface ICommittedStateStore
    {
        bool Has<TState>(EntityRef entity)
            where TState : struct, IOutputState;

        TState Get<TState>(EntityRef entity)
            where TState : struct, IOutputState;

        bool TryGet<TState>(EntityRef entity, out TState state)
            where TState : struct, IOutputState;
    }
}
