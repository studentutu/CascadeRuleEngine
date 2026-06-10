#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Immutable fact produced by input or a reducer.
    /// </summary>
    public readonly struct CascadeFact
    {
        public CascadeFact(
            CascadeEntityId entityId,
            CascadeFactKey key,
            CascadePropertyKey target,
            ReducerPayload payload,
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
        public ReducerPayload Payload { get; }
        public int Priority { get; }
    }
}
