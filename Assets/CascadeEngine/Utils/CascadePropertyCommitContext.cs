#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Context passed to property commit functions for committing staged state and recording mutations.
    /// </summary>
    public sealed class CascadePropertyCommitContext
    {
        private readonly CascadePropertyMutationSet _mutations;

        internal CascadePropertyCommitContext(CascadePropertyMutationSet mutations)
        {
            _mutations = mutations;
        }

        public CascadeEntityId EntityId { get; private set; }
        public CascadeEntityState Entity { get; private set; } = null!;
        public CascadePropertyKey Property { get; private set; }

        /// <summary>
        /// Range: core commit runner only. Condition: before one property commit function. Output: context points at staged entity property.
        /// </summary>
        internal void Bind(CascadeEntityId entityId, CascadeEntityState entity, CascadePropertyKey property)
        {
            EntityId = entityId;
            Entity = entity;
            Property = property;
        }

        /// <summary>
        /// Range: bound staged property. Condition: commit function needs next value. Output: typed staged value or throws when missing.
        /// </summary>
        public T GetStaged<T>()
            => Entity.GetStaged<T>(Property);

        /// <summary>
        /// Range: bound committed property. Condition: commit function needs previous value. Output: typed committed value or default.
        /// </summary>
        public T GetCommittedOrDefault<T>()
            => Entity.GetCommittedOrDefault<T>(Property);

        /// <summary>
        /// Range: bound staged property. Condition: commit function accepts staged value. Output: commits staged value and records mutation only when it changed.
        /// </summary>
        public bool CommitStagedIfChanged()
        {
            if (!Entity.CommitStagedIfChanged(Property))
            {
                return false;
            }

            _mutations.Mark(EntityId, Property);
            return true;
        }

        /// <summary>
        /// Range: bound staged property. Condition: commit function needs previous and next values. Output: returns whether commit changed committed state.
        /// </summary>
        public bool CommitStagedIfChanged<T>(out T previous, out T next)
        {
            previous = GetCommittedOrDefault<T>();
            next = GetStaged<T>();

            return CommitStagedIfChanged();
        }

        /// <summary>
        /// Range: bound entity property. Condition: commit policy needs to force output for a marker property. Output: records entity-property mutation.
        /// </summary>
        public void MarkMutated()
        {
            _mutations.Mark(EntityId, Property);
        }
    }
}
