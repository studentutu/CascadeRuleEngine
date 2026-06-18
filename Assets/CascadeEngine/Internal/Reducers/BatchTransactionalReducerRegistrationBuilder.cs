#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Fluent registration for batch transactional reducers.
    /// </summary>
    public sealed class BatchTransactionalReducerRegistrationBuilder
    {
        private readonly FactFeatureRegistry _registry;
        private readonly FactType[] _requiredFacts;

        internal BatchTransactionalReducerRegistrationBuilder(FactFeatureRegistry registry, FactType[] requiredFacts)
        {
            _registry = registry;
            _requiredFacts = requiredFacts;
        }

        public BatchTransactionalReducerRegistrationBuilder With<TReducer>()
            where TReducer : IBatchTransactionalReducer, new()
        {
            _registry.AddBatchTransactionalReducer<TReducer>(_requiredFacts);
            return this;
        }

        internal BatchTransactionalReducerRegistrationBuilder Append<TFact>()
            where TFact : struct, IFact
        {
            var requiredFacts = new FactType[_requiredFacts.Length + 1];
            Array.Copy(_requiredFacts, requiredFacts, _requiredFacts.Length);
            requiredFacts[_requiredFacts.Length] = FactType.Of<TFact>();
            return new BatchTransactionalReducerRegistrationBuilder(_registry, requiredFacts);
        }
    }
}
