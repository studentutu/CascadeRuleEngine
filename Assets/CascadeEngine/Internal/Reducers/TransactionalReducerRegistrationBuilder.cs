#nullable enable

using System;

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

        internal TransactionalReducerRegistrationBuilder Append<TFact>()
            where TFact : struct, IFact
        {
            var requiredFacts = new FactType[_requiredFacts.Length + 1];
            Array.Copy(_requiredFacts, requiredFacts, _requiredFacts.Length);
            requiredFacts[_requiredFacts.Length] = FactType.Of<TFact>();
            return new TransactionalReducerRegistrationBuilder(_registry, requiredFacts);
        }
    }
}
