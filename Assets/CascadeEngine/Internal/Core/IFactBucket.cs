#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal untyped contract for one fact type bucket.
    /// </summary>
    internal interface IFactBucket
    {
        Type FactType { get; }
        int EntityCapacity { get; }
        int TouchedEntityCapacity { get; }

        bool Has(EntityRef entity);

        int CountFor(EntityRef entity);

        void EnsureEntityCapacity(int entityCapacity);

        void Warmup(int entityCapacity, int factCapacityPerEntity);

        int MinimumFactListCapacity(int entityCapacity);

        void Clear();
    }
}
