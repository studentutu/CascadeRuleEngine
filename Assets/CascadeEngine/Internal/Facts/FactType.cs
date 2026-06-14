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

        public FactType(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            if (!typeof(IFact).IsAssignableFrom(type))
            {
                throw new ArgumentException("Fact type must implement IFact.", nameof(type));
            }

            Id = CascadeTypeIdentity.Resolve(type);
            _bucketFactory = null;
        }

        public FactType(CascadeTypeId id)
        {
            if (id.IsEmpty)
            {
                throw new ArgumentException("Fact type id cannot be empty.", nameof(id));
            }

            Id = id;
            _bucketFactory = null;
        }

        private FactType(IFactBucketFactory bucketFactory)
        {
            _bucketFactory = bucketFactory;
            Id = bucketFactory.Id;
        }

        internal CascadeTypeId Id { get; }
        internal bool CanCreateBucket => _bucketFactory != null;
        internal string DebugName => _bucketFactory != null
            ? _bucketFactory.DebugName
            : CascadeTypeDiagnostics.Describe(Id);

        internal IFactBucket CreateBucket(int entityCapacity, int factCapacityPerEntity)
        {
            if (_bucketFactory == null)
            {
                throw new InvalidOperationException($"Fact type '{DebugName}' was registered without a typed bucket factory.");
            }

            return _bucketFactory.Create(entityCapacity, factCapacityPerEntity);
        }

        public static FactType Of<TFact>()
            where TFact : struct, IFact
            => new FactType(FactBucketFactory<TFact>.Instance);
    }
}
