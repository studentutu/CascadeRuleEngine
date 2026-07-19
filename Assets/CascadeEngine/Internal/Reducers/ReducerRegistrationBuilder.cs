#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Fluent registration for fact-triggered reducers.
    /// </summary>
    public sealed class ReducerRegistrationBuilder<TFact>
        where TFact : struct, IFact
    {
        private readonly FactFeatureRegistry _registry;
        private readonly FactType[] _forbiddenFacts;

        internal ReducerRegistrationBuilder(FactFeatureRegistry registry)
            : this(registry, Array.Empty<FactType>())
        {
        }

        private ReducerRegistrationBuilder(
            FactFeatureRegistry registry,
            FactType[] forbiddenFacts)
        {
            _registry = registry;
            _forbiddenFacts = forbiddenFacts;
        }

        /// <summary>
        /// [INTEGRATION] Defers this fact reducer until positive closure and requires the declared fact to be absent for the entity.
        /// </summary>
        public ReducerRegistrationBuilder<TFact> Without<TForbiddenFact>()
            where TForbiddenFact : struct, IFact
        {
            var forbiddenFacts = new FactType[_forbiddenFacts.Length + 1];
            Array.Copy(_forbiddenFacts, forbiddenFacts, _forbiddenFacts.Length);
            forbiddenFacts[_forbiddenFacts.Length] = FactType.Of<TForbiddenFact>();
            return new ReducerRegistrationBuilder<TFact>(_registry, forbiddenFacts);
        }

        public ReducerRegistrationBuilder<TFact> With<TReducer>()
            where TReducer : IFactReducer<TFact>, new()
        {
            _registry.AddReducer<TFact, TReducer>(_forbiddenFacts);
            return this;
        }
    }
}
