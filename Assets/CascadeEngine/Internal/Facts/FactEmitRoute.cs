#nullable enable

using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed emit route for one fact type in one feature registry.
    /// </summary>
    internal sealed class FactEmitRoute<TFact> : IFactCommitRoute, IFactReduceRoute
        where TFact : struct, IFact
    {
        private readonly List<IOutputRegistration> _affectedOutputs = new List<IOutputRegistration>();
        private readonly List<IReducerInvoker> _reducers = new List<IReducerInvoker>();

        internal FactEmitRoute(CascadeTypeId factId)
        {
            FactId = factId;
        }

        internal CascadeTypeId FactId { get; }
        public int AffectedOutputCount => _affectedOutputs.Count;
        public int ReducerCount => _reducers.Count;

        public IOutputRegistration AffectedOutputAt(int index)
            => _affectedOutputs[index];

        public IReducerInvoker ReducerAt(int index)
            => _reducers[index];

        internal void AddAffectedOutput(IOutputRegistration output)
            => _affectedOutputs.Add(output);

        internal void AddReducer(IReducerInvoker reducer)
            => _reducers.Add(reducer);
    }
}
