#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Fluent registration for explicit fact queue priority resolution.
    /// </summary>
    public sealed class FactPriorityRegistrationBuilder<TFact>
        where TFact : struct, IFact
    {
        private readonly FactFeatureRegistry _registry;

        internal FactPriorityRegistrationBuilder(FactFeatureRegistry registry)
        {
            _registry = registry;
        }

        public FactPriorityRegistrationBuilder<TFact> With<TResolver>()
            where TResolver : IFactPriorityResolver<TFact>, new()
        {
            _registry.AddPriorityResolver<TFact, TResolver>();
            return this;
        }
    }
}
