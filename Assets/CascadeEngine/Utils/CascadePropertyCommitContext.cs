#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Context passed to property commit functions.
    /// </summary>
    public sealed class CascadePropertyCommitContext
    {
        private readonly CascadeDirtyConsumerSet _dirtyConsumers;

        internal CascadePropertyCommitContext(CascadeDirtyConsumerSet dirtyConsumers)
        {
            _dirtyConsumers = dirtyConsumers;
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
        /// Range: bound staged property. Condition: commit function accepts staged value. Output: publishes staged value only when it changed.
        /// </summary>
        public bool PublishStagedIfChanged()
            => Entity.PublishStagedIfChanged(Property);

        /// <summary>
        /// Range: bound staged property. Condition: commit function needs previous and next values. Output: returns whether publish changed committed state.
        /// </summary>
        public bool PublishStagedIfChanged<T>(out T previous, out T next)
        {
            previous = GetCommittedOrDefault<T>();
            next = GetStaged<T>();

            return PublishStagedIfChanged();
        }

        /// <summary>
        /// Range: bound entity. Condition: committed change is relevant to a consumer. Output: queues exact entity-consumer dirty work.
        /// </summary>
        public void MarkDirty(CascadeConsumerKey consumer)
        {
            _dirtyConsumers.Mark(consumer, EntityId);
        }
    }
}
