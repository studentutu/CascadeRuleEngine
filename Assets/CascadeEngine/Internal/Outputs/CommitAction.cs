#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed delayed state write applied after every committer has made its decision.
    /// </summary>
    internal readonly struct CommitAction<TState>
        where TState : struct, IOutputState
    {
        private readonly StateBucket<TState> _bucket;
        private readonly EntityRef _entity;
        private readonly StateMutation<TState> _mutation;

        internal CommitAction(StateBucket<TState> bucket, EntityRef entity, StateMutation<TState> mutation)
        {
            _bucket = bucket;
            _entity = entity;
            _mutation = mutation;
        }

        internal void Validate(EntityStore entities)
        {
            if (!entities.TryResolveForStorage(_entity, out var current) || current != _entity)
                throw new System.InvalidOperationException("Prepared output action targets a stale entity generation.");
        }

        public void Apply() => _bucket.ApplyPrepared(_entity, in _mutation);
    }
}
