#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// One accepted fact waiting for immediate reducer dispatch.
    /// </summary>
    internal readonly struct QueuedFact
    {
        internal QueuedFact(EntityRef entity, Type factType, object payload, FactPriority priority, int depth, long sequence)
        {
            Entity = entity;
            FactType = factType;
            Payload = payload;
            Priority = priority;
            Depth = depth;
            Sequence = sequence;
        }

        internal EntityRef Entity { get; }
        internal Type FactType { get; }
        internal object Payload { get; }
        internal FactPriority Priority { get; }
        internal int Depth { get; }
        internal long Sequence { get; }
    }
}
