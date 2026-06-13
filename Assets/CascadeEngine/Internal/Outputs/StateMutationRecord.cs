#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal typed mutation row.
    /// </summary>
    internal readonly struct StateMutationRecord<TState>
        where TState : struct, IOutputState
    {
        internal StateMutationRecord(EntityRef entity, StateMutation<TState> mutation)
        {
            Entity = entity;
            Mutation = mutation;
        }

        internal EntityRef Entity { get; }
        internal StateMutation<TState> Mutation { get; }
    }
}
