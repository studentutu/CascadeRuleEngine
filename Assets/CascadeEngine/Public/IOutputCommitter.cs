#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Final integrity boundary for one output state. Committers are the only code that writes durable output state.
    /// </summary>
    public interface IOutputCommitter<TState>
        where TState : struct, IOutputState
    {
        CommitDecision<TState> Commit(
            ICommitContext ctx,
            EntityRef entity,
            in Optional<TState> previous);
    }
}
