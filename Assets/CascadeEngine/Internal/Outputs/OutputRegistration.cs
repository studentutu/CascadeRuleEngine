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
        private readonly Type[] _affectedFacts;
        private readonly IOutputCommitter<TState> _committer;

        internal OutputRegistration(
            OutputState<TState> output,
            Type[] affectedFacts,
            IOutputCommitter<TState> committer)
        {
            Output = output;
            _affectedFacts = affectedFacts;
            _committer = committer;
        }

        internal OutputState<TState> Output { get; }

        public Type StateType => typeof(TState);
        public string Name => Output.Name;

        public void Reindex(int index)
            => Output.Index = index;

        public bool IsAffectedBy(IEntityFactView facts)
        {
            for (var i = 0; i < _affectedFacts.Length; i++)
            {
                if (facts.Has(new FactType(_affectedFacts[i])))
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

        public void DeleteState(FactSimulation simulation, EntityRef entity)
            => simulation.GetStateBucket<TState>().Delete(entity);

        public void ClearMutations(FactSimulation simulation)
            => simulation.GetStateBucket<TState>().ClearMutations();

        public int MutationCount(FactSimulation simulation)
            => simulation.GetStateBucket<TState>().MutationCount;
    }
}
