#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// One published mutation: which property changed on which entity in the last successful tick.
    /// </summary>
    public readonly struct CascadePropertyMutation
    {
        internal CascadePropertyMutation(CascadeEntityId entityId, CascadePropertyKey property)
        {
            EntityId = entityId;
            Property = property;
        }

        public CascadeEntityId EntityId { get; }
        public CascadePropertyKey Property { get; }

        public override string ToString()
            => $"{Property.Name}@{EntityId.Value}";
    }
}