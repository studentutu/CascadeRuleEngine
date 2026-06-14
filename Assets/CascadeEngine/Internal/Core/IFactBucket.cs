#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal untyped contract for one fact type bucket.
    /// </summary>
    internal interface IFactBucket
    {
        CascadeTypeId FactId { get; }
        int EntityCapacity { get; }
        int TouchedEntityCapacity { get; }

        bool Has(EntityRef entity);

        int CountFor(EntityRef entity);

        void EnsureEntityCapacity(int entityCapacity);

        void Warmup(
            int entityCapacity,
            int factCapacityPerEntity,
            FactListCapacityMode factListCapacityMode);

        int MinimumFactListCapacity(int entityCapacity);

        void Clear();
    }
}
