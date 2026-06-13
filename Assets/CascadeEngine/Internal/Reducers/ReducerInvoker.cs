#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed reducer bridge that keeps unboxing in one place.
    /// </summary>
    internal sealed class ReducerInvoker<TFact> : IReducerInvoker
        where TFact : struct, IFact
    {
        private readonly IFactReducer<TFact> _reducer;

        internal ReducerInvoker(IFactReducer<TFact> reducer)
        {
            _reducer = reducer ?? throw new ArgumentNullException(nameof(reducer));
        }

        public Type FactType => typeof(TFact);

        public void Reduce(FactSimulation simulation, EntityRef entity, object fact)
        {
            var typedFact = (TFact)fact;
            _reducer.Reduce(simulation, entity, in typedFact);
        }
    }
}
