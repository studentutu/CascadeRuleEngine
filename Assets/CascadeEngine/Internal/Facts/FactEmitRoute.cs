#nullable enable

using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed emit route for one fact type in one feature registry.
    /// </summary>
    internal sealed class FactEmitRoute<TFact> : IFactCommitRoute
        where TFact : struct, IFact
    {
        private readonly List<IOutputRegistration> _affectedOutputs = new List<IOutputRegistration>();
        private IFactPriorityResolver<TFact>? _priorityResolver;

        internal FactEmitRoute(CascadeTypeId factId)
        {
            FactId = factId;
        }

        internal CascadeTypeId FactId { get; }
        public int AffectedOutputCount => _affectedOutputs.Count;

        public IOutputRegistration AffectedOutputAt(int index)
            => _affectedOutputs[index];

        internal FactPriority ResolvePriority(in TFact fact)
            => _priorityResolver != null
                ? _priorityResolver.Resolve(in fact)
                : FactPriority.Normal;

        internal void AddAffectedOutput(IOutputRegistration output)
            => _affectedOutputs.Add(output);

        internal void SetPriorityResolver(IFactPriorityResolver<TFact> resolver)
            => _priorityResolver = resolver;

        internal void ClearPriorityResolver()
            => _priorityResolver = null;
    }
}
