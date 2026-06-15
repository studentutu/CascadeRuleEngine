#nullable enable

using System;
using System.Collections.Generic;

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
        private readonly List<CommitAction<TState>> _commitActions = new List<CommitAction<TState>>();

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

        public CascadeTypeId StateId => Output.Id;
        public int Index => Output.Index;
        public string Name => Output.Name;
        public CascadeTypeId[] AffectedFactIds => _affectedFactIds;
        public int CommitActionCapacity => _commitActions.Capacity;

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

        public void QueueCommitAction(FactSimulation simulation, EntityRef entity)
        {
            var bucket = simulation.GetStateBucket<TState>();
            var previous = bucket.TryGet(entity, out var value)
                ? new Optional<TState>(value)
                : default;

            var decision = _committer.Commit(simulation, entity, in previous);
            if (decision.Kind == CommitDecisionKind.Unchanged)
            {
                return;
            }

            _commitActions.Add(new CommitAction<TState>(bucket, entity, decision));
        }

        public void ApplyQueuedCommitActions()
        {
            for (var i = 0; i < _commitActions.Count; i++)
            {
                _commitActions[i].Apply();
            }
        }

        public void ClearQueuedCommitActions()
        {
            _commitActions.Clear();
        }

        public IStateBucket CreateStateBucket()
            => new StateBucket<TState>(Output.Id, Output.Name);

        public void DeleteState(FactSimulation simulation, EntityRef entity)
            => simulation.GetStateBucket<TState>().Delete(entity);

        public void Warmup(
            FactSimulation simulation,
            int stateCapacity,
            int mutationCapacity,
            int commitActionCapacity)
        {
            simulation.GetStateBucket<TState>().EnsureCapacity(stateCapacity, mutationCapacity);
            if (_commitActions.Capacity < commitActionCapacity)
            {
                _commitActions.Capacity = commitActionCapacity;
            }
        }

        public void ClearMutations(FactSimulation simulation)
            => simulation.GetStateBucket<TState>().ClearMutations();

        public int MutationCount(FactSimulation simulation)
            => simulation.GetStateBucket<TState>().MutationCount;

        public void DisposeRegistration()
        {
            _commitActions.Clear();
            _commitActions.Capacity = 0;

            if (_committer is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
