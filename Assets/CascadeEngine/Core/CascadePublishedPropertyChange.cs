#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// One committed entity property published for consumer fanout after commit.
    /// </summary>
    public readonly struct CascadePublishedPropertyChange
    {
        /// <summary>
        /// Range: committed entity and property key. Condition: commit policy published a property. Output: immutable publish work item.
        /// </summary>
        public CascadePublishedPropertyChange(CascadeEntityId entityId, CascadePropertyKey property)
        {
            EntityId = entityId;
            Property = property;
        }

        public CascadeEntityId EntityId { get; }
        public CascadePropertyKey Property { get; }
    }
}
