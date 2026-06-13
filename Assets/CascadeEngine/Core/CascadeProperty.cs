#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed property: dense committed/staged columns, change policy, and typed mutation output for one declared property.
    /// </summary>
    public sealed class CascadeProperty<T> : CascadePropertyKey
    {
        private static readonly CascadeValueEquality<T> SharedDefaultEquality = AreEqualByDefault;

        private readonly CascadeValueEquality<T> _areEqual;
        private readonly bool _alwaysPublish;

        private readonly T[] _committed;
        private readonly T[] _staged;
        private readonly int[] _stagedPriorities;
        private readonly bool[] _isStaged;
        private readonly int[] _stagedEntities;
        private int _stagedCount;

        private readonly int[] _mutatedEntities;
        private readonly T[] _mutatedPrevious;
        private readonly T[] _mutatedNext;
        private int _mutatedCount;

        internal CascadeProperty(
            CascadeSchema owner,
            int index,
            string name,
            int entityCapacity,
            CascadeValueEquality<T>? areEqual,
            bool alwaysPublish)
            : base(owner, index, name)
        {
            _areEqual = areEqual ?? SharedDefaultEquality;
            _alwaysPublish = alwaysPublish;
            _committed = new T[entityCapacity];
            _staged = new T[entityCapacity];
            _stagedPriorities = new int[entityCapacity];
            _isStaged = new bool[entityCapacity];
            _stagedEntities = new int[entityCapacity];
            _mutatedEntities = new int[entityCapacity];
            _mutatedPrevious = new T[entityCapacity];
            _mutatedNext = new T[entityCapacity];
        }

        internal override int MutatedCount
            => _mutatedCount;

        /// <summary>
        /// Range: live or destroyed entity slot. Condition: query, no side effects. Output: committed value or default.
        /// </summary>
        internal T ReadCommitted(CascadeEntityId entityId)
            => _committed[entityId.Value];

        /// <summary>
        /// Range: entity slot. Condition: reducer reads current working value, no side effects. Output: staged value wins, otherwise committed value.
        /// </summary>
        internal T ReadStagedOrCommitted(CascadeEntityId entityId)
            => _isStaged[entityId.Value] ? _staged[entityId.Value] : _committed[entityId.Value];

        /// <summary>
        /// Range: live entity slot. Condition: initialization or authoritative external load. Output: committed value set, staged value discarded.
        /// </summary>
        internal void SetCommitted(CascadeEntityId entityId, T value)
        {
            _committed[entityId.Value] = value;
            _isStaged[entityId.Value] = false;
        }

        /// <summary>
        /// Range: live entity slot. Condition: reducer stages a candidate value. Output: value staged once per entity for commit, priority reset.
        /// </summary>
        internal void Stage(CascadeEntityId entityId, T value, int priority)
        {
            RegisterStaged(entityId.Value);
            _staged[entityId.Value] = value;
            _stagedPriorities[entityId.Value] = priority;
        }

        /// <summary>
        /// Range: live entity slot. Condition: reducer resolves same-property conflicts by priority. Output: true when the value became the staged candidate.
        /// </summary>
        internal bool StageIfPriorityAtLeast(CascadeEntityId entityId, T value, int priority)
        {
            if (_isStaged[entityId.Value] && priority < _stagedPriorities[entityId.Value])
            {
                return false;
            }

            Stage(entityId, value, priority);
            return true;
        }

        /// <summary>
        /// Range: typed mutation output from the last tick. Condition: consumer routes changed values, no side effects. Output: handler invoked per mutated entity with previous and next values.
        /// </summary>
        internal void ForEachMutation(CascadeMutationHandler<T> handler)
        {
            for (var i = 0; i < _mutatedCount; i++)
            {
                handler(new CascadeEntityId(_mutatedEntities[i]), _mutatedPrevious[i], _mutatedNext[i]);
            }
        }

        internal override void CommitStaged(CascadeEntityStore entities, CascadeMutationLog mutations)
        {
            for (var i = 0; i < _stagedCount; i++)
            {
                var entityIndex = _stagedEntities[i];
                if (!_isStaged[entityIndex])
                {
                    continue;
                }

                _isStaged[entityIndex] = false;

                var entityId = new CascadeEntityId(entityIndex);
                if (entities.IsDestroyed(entityId))
                {
                    continue;
                }

                var previous = _committed[entityIndex];
                var next = _staged[entityIndex];
                var changed = !_areEqual(previous, next);
                if (changed)
                {
                    _committed[entityIndex] = next;
                }

                if (changed || _alwaysPublish)
                {
                    RecordMutation(entityIndex, previous, next, mutations);
                }
            }

            _stagedCount = 0;
        }

        internal override void AbortStaged()
        {
            for (var i = 0; i < _stagedCount; i++)
            {
                _isStaged[_stagedEntities[i]] = false;
            }

            _stagedCount = 0;
        }

        internal override void ClearMutationOutput()
        {
            _mutatedCount = 0;
        }

        internal override void ClearEntity(CascadeEntityId entityId)
        {
            _committed[entityId.Value] = default!;
            _isStaged[entityId.Value] = false;
        }

        internal override bool WasMutated(CascadeEntityId entityId)
        {
            for (var i = 0; i < _mutatedCount; i++)
            {
                if (_mutatedEntities[i] == entityId.Value)
                {
                    return true;
                }
            }

            return false;
        }

        private void RegisterStaged(int entityIndex)
        {
            if (_isStaged[entityIndex])
            {
                return;
            }

            if (_stagedCount >= _stagedEntities.Length)
            {
                throw new InvalidOperationException(
                    $"Staged entity list overflow for property '{Name}'. Caused by repeated SetCommitted/Stage cycles in one tick.");
            }

            _isStaged[entityIndex] = true;
            _stagedEntities[_stagedCount] = entityIndex;
            _stagedCount++;
        }

        private void RecordMutation(int entityIndex, T previous, T next, CascadeMutationLog mutations)
        {
            _mutatedEntities[_mutatedCount] = entityIndex;
            _mutatedPrevious[_mutatedCount] = previous;
            _mutatedNext[_mutatedCount] = next;
            _mutatedCount++;
            mutations.Record(new CascadePropertyMutation(new CascadeEntityId(entityIndex), this));
        }

        private static bool AreEqualByDefault(T previous, T next)
            => EqualityComparer<T>.Default.Equals(previous, next);
    }
}