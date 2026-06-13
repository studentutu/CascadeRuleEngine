#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Test-only internal view of reusable runtime capacities.
    /// </summary>
    internal readonly struct CascadeCapacitySnapshot
    {
        internal CascadeCapacitySnapshot(
            int factQueueCapacity,
            int factTouchedEntityCapacity,
            int factCounterEntityCapacity,
            int factBucketCount,
            int minimumFactBucketEntityCapacity,
            int minimumFactBucketTouchedEntityCapacity,
            int minimumFactListCapacity,
            int queryBufferCapacity,
            int transactionBufferCapacity,
            int batchBufferCapacity,
            int commitActionCapacity,
            int minimumStateCapacityHint,
            int minimumMutationCapacity)
        {
            FactQueueCapacity = factQueueCapacity;
            FactTouchedEntityCapacity = factTouchedEntityCapacity;
            FactCounterEntityCapacity = factCounterEntityCapacity;
            FactBucketCount = factBucketCount;
            MinimumFactBucketEntityCapacity = minimumFactBucketEntityCapacity;
            MinimumFactBucketTouchedEntityCapacity = minimumFactBucketTouchedEntityCapacity;
            MinimumFactListCapacity = minimumFactListCapacity;
            QueryBufferCapacity = queryBufferCapacity;
            TransactionBufferCapacity = transactionBufferCapacity;
            BatchBufferCapacity = batchBufferCapacity;
            CommitActionCapacity = commitActionCapacity;
            MinimumStateCapacityHint = minimumStateCapacityHint;
            MinimumMutationCapacity = minimumMutationCapacity;
        }

        internal int FactQueueCapacity { get; }
        internal int FactTouchedEntityCapacity { get; }
        internal int FactCounterEntityCapacity { get; }
        internal int FactBucketCount { get; }
        internal int MinimumFactBucketEntityCapacity { get; }
        internal int MinimumFactBucketTouchedEntityCapacity { get; }
        internal int MinimumFactListCapacity { get; }
        internal int QueryBufferCapacity { get; }
        internal int TransactionBufferCapacity { get; }
        internal int BatchBufferCapacity { get; }
        internal int CommitActionCapacity { get; }
        internal int MinimumStateCapacityHint { get; }
        internal int MinimumMutationCapacity { get; }
    }
}
