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

        internal void Bind(CascadeEntityId entityId, CascadeEntityState entity, CascadePropertyKey property)
        {
            EntityId = entityId;
            Entity = entity;
            Property = property;
        }

        public T GetStaged<T>()
            => Entity.GetStaged<T>(Property);

        public bool PublishStagedIfChanged()
            => Entity.PublishStagedIfChanged(Property);

        public void MarkDirty(CascadeConsumerKey consumer)
        {
            _dirtyConsumers.Mark(consumer, EntityId);
        }
    }
}
