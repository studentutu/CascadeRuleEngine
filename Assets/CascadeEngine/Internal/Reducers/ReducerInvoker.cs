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
        private readonly string _debugName;

        internal ReducerInvoker(IFactReducer<TFact> reducer, string debugName)
        {
            _reducer = reducer ?? throw new ArgumentNullException(nameof(reducer));
            _debugName = debugName ?? string.Empty;
        }

        public string DebugName => _debugName;

        public void BindRoute(FactFeatureRegistry registry)
            => registry.RequireFactRoute<TFact>().AddReducer(this);

        public void Reduce(FactSimulation simulation, in QueuedFact fact)
        {
            var bucket = (FactBucket<TFact>)fact.Bucket;
            ref readonly var typedFact = ref bucket.Get(fact.Entity, fact.FactIndex);
            _reducer.Reduce(simulation, fact.Entity, in typedFact);
        }

        public void DisposeRegistration()
        {
            if (_reducer is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}
