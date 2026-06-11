#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// One committed entity-consumer pair that must be refreshed after a tick.
    /// </summary>
    public readonly struct CascadeConsumerWorkItem
    {
        /// <summary>
        /// Range: committed entity and dirty consumer key. Condition: commit marked exact consumer work. Output: immutable consumer refresh item.
        /// </summary>
        public CascadeConsumerWorkItem(CascadeEntityId entityId, CascadeConsumerKey consumer)
        {
            EntityId = entityId;
            Consumer = consumer;
        }

        public CascadeEntityId EntityId { get; }
        public CascadeConsumerKey Consumer { get; }
    }
}
