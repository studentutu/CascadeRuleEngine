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
        private readonly FactType[] _affectedFacts;
        private readonly int[] _affectedFactPriorities;
        private readonly CascadeTypeId[] _absentFactIds;
        private readonly IOutputCommitter<TState> _committer;
        private readonly List<CommitAction<TState>> _commitActions = new List<CommitAction<TState>>();
        private readonly DenseEntitySet _queuedEntities = new DenseEntitySet(64);
        private StateBucket<TState>? _bucket;

        internal OutputRegistration(
            OutputState<TState> output,
            FactType[] affectedFacts,
            int[] affectedFactPriorities,
            FactType[] absentFacts,
            IOutputCommitter<TState> committer)
        {
            Output = output;
            _affectedFacts = affectedFacts;
            _affectedFactPriorities = affectedFactPriorities;
            _absentFactIds = ToIds(absentFacts);
            _committer = committer;
        }

        internal OutputState<TState> Output { get; }

        public CascadeTypeId StateId => Output.Id;
        public int Index => Output.Index;
        public string Name => Output.Name;
        public FactType[] AffectedFacts => _affectedFacts;
        public bool UsesPrioritySelection =>
            Output.ConflictPolicy == CommitConflictPolicy.PriorityWinnerOrThrowOnTie;
        public bool HasAbsenceReconciliation => _absentFactIds.Length > 0;
        public int CommitActionCapacity => _commitActions.Capacity;

        public void Reindex(int index)
            => Output.Index = index;

        public void QueueCommitAction(FactSimulation simulation, EntityRef entity)
        {
            if (!_queuedEntities.Add(entity))
            {
                return;
            }

            var bucket = RequireBucket();
            var previous = bucket.TryGet(entity, out var value)
                ? new Optional<TState>(value)
                : default;

            var selectedFact = UsesPrioritySelection
                ? SelectPriorityWinner(simulation, entity)
                : default;

            simulation.BeginOutputCommit(this, entity, selectedFact);
            CommitDecision<TState> decision;
            try
            {
                decision = _committer.Commit(simulation, entity, in previous);
            }
            finally
            {
                simulation.EndOutputCommit();
            }

            if (decision.Kind == CommitDecisionKind.Unchanged)
            {
                return;
            }

            _commitActions.Add(new CommitAction<TState>(bucket, entity, decision));
        }

        public void QueueAbsentCommitActions(FactSimulation simulation)
        {
            var bucket = RequireBucket();
            for (var entityIndex = 0; entityIndex < bucket.EntityCount; entityIndex++)
            {
                var entity = bucket.EntityAt(entityIndex);
                if (simulation.IsDestroyed(entity) || HasAnyAbsentFact(simulation, entity))
                {
                    continue;
                }

                QueueCommitAction(simulation, entity);
            }
        }

        public CascadeTypeId SelectPriorityWinner(FactSimulation simulation, EntityRef entity)
        {
            var winner = default(CascadeTypeId);
            var winnerPriority = 0;
            var winnerCount = 0;

            for (var i = 0; i < _affectedFacts.Length; i++)
            {
                var count = simulation.FactCount(entity, _affectedFacts[i].Id);
                if (count == 0)
                {
                    continue;
                }

                var priority = _affectedFactPriorities[i];
                if (winner.IsEmpty || priority > winnerPriority)
                {
                    winner = _affectedFacts[i].Id;
                    winnerPriority = priority;
                    winnerCount = count;
                    continue;
                }

                if (priority == winnerPriority)
                {
                    winnerCount += count;
                }
            }

            if (winnerCount > 1)
            {
                throw new CommitConflictException(
                    entity,
                    typeof(TState),
                    $"{winnerCount} distinct facts share winning priority '{winnerPriority}'.");
            }

            return winner;
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
            _queuedEntities.Clear();
        }

        public IStateBucket CreateStateBucket()
            => new StateBucket<TState>(Output.Id, Output.Name);

        public void BindStateBucket(FactSimulation simulation, IStateBucket bucket)
        {
            var typedBucket = (StateBucket<TState>)bucket;
            _bucket = typedBucket;
            OutputStateRouteCache<TState>.Add(simulation, Output, typedBucket);
        }

        public void UnbindStateBucket(FactSimulation simulation)
        {
            OutputStateRouteCache<TState>.Remove(simulation);
            _bucket = null;
        }

        public void DeleteState(FactSimulation simulation, EntityRef entity)
            => RequireBucket().Delete(entity);

        public void Warmup(
            FactSimulation simulation,
            int stateCapacity,
            int mutationCapacity,
            int commitActionCapacity)
        {
            RequireBucket().EnsureCapacity(stateCapacity, mutationCapacity);
            if (_commitActions.Capacity < commitActionCapacity)
            {
                _commitActions.Capacity = commitActionCapacity;
            }

            _queuedEntities.EnsureCapacity(stateCapacity);
        }

        public void ClearMutations(FactSimulation simulation)
            => RequireBucket().ClearMutations();

        public int MutationCount(FactSimulation simulation)
            => RequireBucket().MutationCount;

        public void DisposeRegistration()
        {
            _commitActions.Clear();
            _commitActions.Capacity = 0;
            _queuedEntities.Clear();

            if (_committer is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }

        private StateBucket<TState> RequireBucket()
        {
            if (_bucket == null)
            {
                throw new InvalidOperationException($"Output state '{Output.Name}' is not bound to a simulation state bucket.");
            }

            return _bucket;
        }

        private bool HasAnyAbsentFact(FactSimulation simulation, EntityRef entity)
        {
            for (var i = 0; i < _absentFactIds.Length; i++)
            {
                if (simulation.FactCount(entity, _absentFactIds[i]) > 0)
                {
                    return true;
                }
            }

            return false;
        }

        private static CascadeTypeId[] ToIds(FactType[] facts)
        {
            var ids = new CascadeTypeId[facts.Length];
            for (var i = 0; i < facts.Length; i++)
            {
                ids[i] = facts[i].Id;
            }

            return ids;
        }
    }
}
