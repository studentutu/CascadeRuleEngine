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

        bool Has(EntityRef entity);

        int CountFor(EntityRef entity);

        void EnsureEntityCapacity(int entityCapacity);

        void Clear();
    }
}
