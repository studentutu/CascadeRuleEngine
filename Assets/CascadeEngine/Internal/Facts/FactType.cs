#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Runtime fact type descriptor for non-generic transactional registration helpers.
    /// </summary>
    public readonly struct FactType
    {
        private readonly IFactBucketFactory? _bucketFactory;
        private readonly string _debugName;

        public FactType(CascadeTypeId id)
        {
            if (id.IsEmpty)
            {
                throw new ArgumentException("Fact type id cannot be empty.", nameof(id));
            }

            Id = id;
            _bucketFactory = null;
            _debugName = id.ToString();
        }

        private FactType(IFactBucketFactory bucketFactory)
        {
            _bucketFactory = bucketFactory;
            _debugName = bucketFactory.DebugName;
            Id = bucketFactory.Id;
        }

        internal CascadeTypeId Id { get; }
        internal bool CanCreateBucket => _bucketFactory != null;

        internal string DebugName => _bucketFactory != null
            ? _bucketFactory.DebugName
            : _debugName;

        internal void Register(CascadeTypeCatalog catalog)
        {
            _bucketFactory?.Register(catalog);
        }

        internal IFactBucket CreateBucket(
            int entityCapacity,
            int factCapacityPerEntity,
            FactListCapacityMode factListCapacityMode)
        {
            if (_bucketFactory == null)
            {
                throw new InvalidOperationException($"Fact type '{DebugName}' was registered without a typed bucket factory.");
            }

            return _bucketFactory.Create(Id, entityCapacity, factCapacityPerEntity, factListCapacityMode);
        }

        public static FactType Of<TFact>()
            where TFact : struct, IFact
            => new FactType(FactBucketFactory<TFact>.Instance);
    }
}
