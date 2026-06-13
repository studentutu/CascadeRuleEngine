#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// One accepted fact waiting for immediate reducer dispatch.
    /// </summary>
    internal readonly struct QueuedFact
    {
        internal QueuedFact(
            EntityRef entity,
            Type factType,
            IFactBucket bucket,
            int factIndex,
            FactPriority priority,
            int depth,
            long sequence)
        {
            Entity = entity;
            FactType = factType;
            Bucket = bucket;
            FactIndex = factIndex;
            Priority = priority;
            Depth = depth;
            Sequence = sequence;
        }

        internal EntityRef Entity { get; }
        internal Type FactType { get; }
        internal IFactBucket Bucket { get; }
        internal int FactIndex { get; }
        internal FactPriority Priority { get; }
        internal int Depth { get; }
        internal long Sequence { get; }
    }
}
