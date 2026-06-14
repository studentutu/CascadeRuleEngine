#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed output registration: affected facts plus the committer that owns durable writes.
    /// </summary>
    internal sealed class OutputRegistration<TState> : IOutputRegistration
        where TState : struct, IOutputState
    {
        private readonly CascadeTypeId[] _affectedFactIds;
        private readonly IOutputCommitter<TState> _committer;

        internal OutputRegistration(
            OutputState<TState> output,
            CascadeTypeId[] affectedFactIds,
            IOutputCommitter<TState> committer)
        {
            Output = output;
            _affectedFactIds = affectedFactIds;
            _committer = committer;
        }

        internal OutputState<TState> Output { get; }

        public CascadeTypeId StateId => CascadeTypeIdentity.RequireId<TState>();
        public string Name => Output.Name;

        public void Reindex(int index)
            => Output.Index = index;

        public bool IsAffectedBy(FactStore facts, EntityRef entity)
        {
            for (var i = 0; i < _affectedFactIds.Length; i++)
            {
                if (facts.Has(entity, _affectedFactIds[i]))
                {
                    return true;
                }
            }

            return false;
        }

        public ICommitAction? CreateCommitAction(FactSimulation simulation, EntityRef entity)
        {
            var bucket = simulation.GetStateBucket<TState>();
            var previous = bucket.TryGet(entity, out var value)
                ? new Optional<TState>(value)
                : default;

            var decision = _committer.Commit(simulation, entity, in previous);
            if (decision.Kind == CommitDecisionKind.Unchanged)
            {
                return null;
            }

            return new CommitAction<TState>(bucket, entity, decision);
        }

        public IStateBucket CreateStateBucket()
            => new StateBucket<TState>();

        public void DeleteState(FactSimulation simulation, EntityRef entity)
            => simulation.GetStateBucket<TState>().Delete(entity);

        public void Warmup(FactSimulation simulation, int stateCapacity, int mutationCapacity)
            => simulation.GetStateBucket<TState>().EnsureCapacity(stateCapacity, mutationCapacity);

        public void ClearMutations(FactSimulation simulation)
            => simulation.GetStateBucket<TState>().ClearMutations();

        public int MutationCount(FactSimulation simulation)
            => simulation.GetStateBucket<TState>().MutationCount;

        public void DisposeRegistration()
        {
            if (_committer is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
