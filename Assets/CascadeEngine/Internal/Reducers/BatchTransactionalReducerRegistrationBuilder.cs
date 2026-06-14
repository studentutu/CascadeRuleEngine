#nullable enable

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
    }
}
