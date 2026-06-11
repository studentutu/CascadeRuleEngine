#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// One entity property changed during commit.
    /// </summary>
    public readonly struct CascadePropertyMutation
    {
        /// <summary>
        /// Range: committed entity and property key. Condition: commit changed the property. Output: immutable mutation item.
        /// </summary>
        public CascadePropertyMutation(CascadeEntityId entityId, CascadePropertyKey property)
        {
            EntityId = entityId;
            Property = property;
        }

        /// <summary>
        /// Entity whose committed property changed.
        /// </summary>
        public CascadeEntityId EntityId { get; }

        /// <summary>
        /// Property that changed or emitted a marker mutation.
        /// </summary>
        public CascadePropertyKey Property { get; }
    }
}
