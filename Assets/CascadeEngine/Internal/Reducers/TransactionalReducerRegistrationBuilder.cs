#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Fluent registration for entity-scoped transactional reducers.
    /// </summary>
    public sealed class TransactionalReducerRegistrationBuilder
    {
        private readonly FactFeatureRegistry _registry;
        private readonly FactType[] _requiredFacts;

        internal TransactionalReducerRegistrationBuilder(FactFeatureRegistry registry, FactType[] requiredFacts)
        {
            _registry = registry;
            _requiredFacts = requiredFacts;
        }

        public TransactionalReducerRegistrationBuilder With<TReducer>()
            where TReducer : ITransactionalReducer, new()
        {
            _registry.AddTransactionalReducer<TReducer>(_requiredFacts);
            return this;
        }
    }
}
