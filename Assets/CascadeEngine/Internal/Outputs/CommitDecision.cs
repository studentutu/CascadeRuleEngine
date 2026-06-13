#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Result of folding tick facts into one durable output state.
    /// </summary>
    public readonly struct CommitDecision<TState>
        where TState : struct, IOutputState
    {
        private CommitDecision(CommitDecisionKind kind, TState next)
        {
            Kind = kind;
            Next = next;
        }

        public CommitDecisionKind Kind { get; }
        public TState Next { get; }

        public static CommitDecision<TState> Unchanged()
            => new CommitDecision<TState>(CommitDecisionKind.Unchanged, default);

        public static CommitDecision<TState> Set(TState next)
            => new CommitDecision<TState>(CommitDecisionKind.Set, next);

        public static CommitDecision<TState> Delete()
            => new CommitDecision<TState>(CommitDecisionKind.Delete, default);
    }
}
