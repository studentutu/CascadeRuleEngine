#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Single registration point for reducers and output committers, similar to an Entitas Feature without global system scans.
    /// </summary>
    public abstract class FactFeature
    {
        private readonly FactFeatureRegistry _registry = new FactFeatureRegistry();

        internal FactFeatureRegistry Registry => _registry;

        protected ReducerRegistrationBuilder<TFact> Reduce<TFact>()
            where TFact : struct, IFact
            => new ReducerRegistrationBuilder<TFact>(_registry);

        protected TransactionalReducerRegistrationBuilder ReduceWhen(params FactType[] requiredFacts)
            => new TransactionalReducerRegistrationBuilder(_registry, requiredFacts);

        protected TransactionalReducerRegistrationBuilder ReduceWhen<TA, TB>()
            where TA : struct, IFact
            where TB : struct, IFact
            => ReduceWhen(FactType.Of<TA>(), FactType.Of<TB>());

        protected BatchTransactionalReducerRegistrationBuilder ReduceBatchWhen(params FactType[] requiredFacts)
            => new BatchTransactionalReducerRegistrationBuilder(_registry, requiredFacts);

        protected BatchTransactionalReducerRegistrationBuilder ReduceBatchWhen<TA, TB>()
            where TA : struct, IFact
            where TB : struct, IFact
            => ReduceBatchWhen(FactType.Of<TA>(), FactType.Of<TB>());

        protected OutputRegistrationBuilder<TState> Output<TState>()
            where TState : struct, IOutputState
            => Output<TState>(typeof(TState).Name);

        protected OutputRegistrationBuilder<TState> Output<TState>(string name)
            where TState : struct, IOutputState
            => new OutputRegistrationBuilder<TState>(_registry, name);

        protected void SubFeature(FactFeature feature)
        {
            if (feature == null)
            {
                throw new ArgumentNullException(nameof(feature));
            }

            _registry.MergeFrom(feature.Registry);
        }
    }
}
