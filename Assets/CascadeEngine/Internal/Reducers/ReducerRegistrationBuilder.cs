#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Fluent registration for fact-triggered reducers.
    /// </summary>
    public sealed class ReducerRegistrationBuilder<TFact>
        where TFact : struct, IFact
    {
        private readonly FactFeatureRegistry _registry;

        internal ReducerRegistrationBuilder(FactFeatureRegistry registry)
        {
            _registry = registry;
        }

        public ReducerRegistrationBuilder<TFact> With<TReducer>()
            where TReducer : IFactReducer<TFact>, new()
        {
            _registry.AddReducer<TFact, TReducer>();
            return this;
        }
    }
}
