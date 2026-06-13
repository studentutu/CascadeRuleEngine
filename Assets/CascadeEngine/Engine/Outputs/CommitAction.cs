#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed delayed state write applied after every committer has made its decision.
    /// </summary>
    internal sealed class CommitAction<TState> : ICommitAction
        where TState : struct, IOutputState
    {
        private readonly StateBucket<TState> _bucket;
        private readonly EntityRef _entity;
        private readonly CommitDecision<TState> _decision;

        internal CommitAction(StateBucket<TState> bucket, EntityRef entity, CommitDecision<TState> decision)
        {
            _bucket = bucket;
            _entity = entity;
            _decision = decision;
        }

        public void Apply()
        {
            if (_decision.Kind == CommitDecisionKind.Set)
            {
                _bucket.Set(_entity, _decision.Next);
                return;
            }

            if (_decision.Kind == CommitDecisionKind.Delete)
            {
                _bucket.Delete(_entity);
            }
        }
    }
}
