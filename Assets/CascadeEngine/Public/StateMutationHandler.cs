#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Consumer callback for typed output mutations.
    /// </summary>
    public delegate void StateMutationHandler<TState>(EntityRef entity, in StateMutation<TState> mutation)
        where TState : struct, IOutputState;
}
