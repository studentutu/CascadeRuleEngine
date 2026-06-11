#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Immutable fact produced by input or a reducer.
    /// </summary>
    public readonly struct CascadeFact
    {
        /// <summary>
        /// Range: one entity and one fact kind. Condition: payload matches reducer expectations. Output: immutable tick-local fact.
        /// </summary>
        public CascadeFact(
            CascadeEntityId entityId,
            CascadeFactKey key,
            CascadePropertyKey target,
            CascadeValue payload,
            int priority = 0)
        {
            EntityId = entityId;
            Key = key;
            Target = target;
            Payload = payload ?? throw new System.ArgumentNullException(nameof(payload));
            Priority = priority;
        }

        public CascadeEntityId EntityId { get; }
        public CascadeFactKey Key { get; }
        public CascadePropertyKey Target { get; }
        public CascadeValue Payload { get; }
        public int Priority { get; }
    }
}
