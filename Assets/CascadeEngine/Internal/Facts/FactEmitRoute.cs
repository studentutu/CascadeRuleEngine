#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed emit route for one fact type in one feature registry.
    /// </summary>
    internal sealed class FactEmitRoute<TFact>
        where TFact : struct, IFact
    {
        private IFactPriorityResolver<TFact>? _priorityResolver;

        internal FactEmitRoute(CascadeTypeId factId)
        {
            FactId = factId;
        }

        internal CascadeTypeId FactId { get; }

        internal FactPriority ResolvePriority(in TFact fact)
            => _priorityResolver != null
                ? _priorityResolver.Resolve(in fact)
                : FactPriority.Normal;

        internal void SetPriorityResolver(IFactPriorityResolver<TFact> resolver)
            => _priorityResolver = resolver;

        internal void ClearPriorityResolver()
            => _priorityResolver = null;
    }
}
