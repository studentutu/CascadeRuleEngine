#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed reducer bridge from bucket/index queued work to a reducer call.
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

        public void Reduce(FactSimulation simulation, in QueuedFact fact)
        {
            var bucket = (FactBucket<TFact>)fact.Bucket;
            ref readonly var typedFact = ref bucket.Get(fact.Entity, fact.FactIndex);
            _reducer.Reduce(simulation, fact.Entity, in typedFact);
        }
    }
}
