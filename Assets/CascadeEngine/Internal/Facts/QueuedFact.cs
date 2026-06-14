#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// One accepted fact waiting for immediate reducer dispatch.
    /// </summary>
    internal readonly struct QueuedFact
    {
        internal QueuedFact(
            EntityRef entity,
            CascadeTypeId factId,
            IFactBucket bucket,
            int factIndex,
            FactPriority priority,
            int depth,
            long sequence)
        {
            Entity = entity;
            FactId = factId;
            Bucket = bucket;
            FactIndex = factIndex;
            Priority = priority;
            Depth = depth;
            Sequence = sequence;
        }

        internal EntityRef Entity { get; }
        internal CascadeTypeId FactId { get; }
        internal IFactBucket Bucket { get; }
        internal int FactIndex { get; }
        internal FactPriority Priority { get; }
        internal int Depth { get; }
        internal long Sequence { get; }
    }
}
