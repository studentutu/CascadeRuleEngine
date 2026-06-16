#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Registration-owned typed priority resolver handle.
    /// </summary>
    internal sealed class FactPriorityRegistration<TFact> : IFactPriorityRegistration
        where TFact : struct, IFact
    {
        private readonly IFactPriorityResolver<TFact> _resolver;

        internal FactPriorityRegistration(IFactPriorityResolver<TFact> resolver)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        }

        public void Bind(FactFeatureRegistry registry)
            => FactPriorityResolverCache<TFact>.Add(registry, _resolver);

        public void Unbind(FactFeatureRegistry registry)
            => FactPriorityResolverCache<TFact>.Remove(registry);

        public void DisposeRegistration()
        {
            if (_resolver is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
