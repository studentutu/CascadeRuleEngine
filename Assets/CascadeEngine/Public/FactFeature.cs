#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Single registration point for reducers and output committers, similar to an Entitas Feature without global system scans.
    /// </summary>
    public abstract class FactFeature : IDisposable
    {
        private readonly FactFeatureRegistry _registry = new FactFeatureRegistry();
        private readonly List<FactFeature> _subFeatures = new List<FactFeature>();
        private bool _attachedToParent;
        private bool _disposed;

        internal FactFeatureRegistry Registry => _registry;
        internal bool IsDisposed => _disposed;
        internal bool IsAttachedToParent => _attachedToParent;

        protected ReducerRegistrationBuilder<TFact> Reduce<TFact>()
            where TFact : struct, IFact
        {
            ThrowIfNotMutable();
            return new ReducerRegistrationBuilder<TFact>(_registry);
        }

        protected TransactionalReducerRegistrationBuilder ReduceWhen(params FactType[] requiredFacts)
        {
            ThrowIfNotMutable();
            return new TransactionalReducerRegistrationBuilder(_registry, requiredFacts);
        }

        protected TransactionalReducerRegistrationBuilder ReduceWhen<TA, TB>()
            where TA : struct, IFact
            where TB : struct, IFact
            => ReduceWhen(FactType.Of<TA>(), FactType.Of<TB>());

        protected BatchTransactionalReducerRegistrationBuilder ReduceBatchWhen(params FactType[] requiredFacts)
        {
            ThrowIfNotMutable();
            return new BatchTransactionalReducerRegistrationBuilder(_registry, requiredFacts);
        }

        protected BatchTransactionalReducerRegistrationBuilder ReduceBatchWhen<TA, TB>()
            where TA : struct, IFact
            where TB : struct, IFact
            => ReduceBatchWhen(FactType.Of<TA>(), FactType.Of<TB>());

        protected OutputRegistrationBuilder<TState> Output<TState>()
            where TState : struct, IOutputState
            => Output<TState>(CascadeTypeIdentity<TState>.DebugName);

        protected OutputRegistrationBuilder<TState> Output<TState>(string name)
            where TState : struct, IOutputState
        {
            ThrowIfNotMutable();
            return new OutputRegistrationBuilder<TState>(_registry, name);
        }

        protected void SubFeature(FactFeature feature)
        {
            ThrowIfNotMutable();

            if (feature == null)
            {
                throw new ArgumentNullException(nameof(feature));
            }

            if (feature._disposed)
            {
                throw new ObjectDisposedException(nameof(FactFeature));
            }

            if (feature._attachedToParent)
            {
                throw new InvalidOperationException("Sub-feature already belongs to another feature.");
            }

            _registry.AbsorbFrom(feature.Registry);
            feature._attachedToParent = true;
            _subFeatures.Add(feature);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _registry.Dispose();

            for (var i = 0; i < _subFeatures.Count; i++)
            {
                _subFeatures[i].Dispose();
            }

            _subFeatures.Clear();
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        private void ThrowIfNotMutable()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(FactFeature));
            }

            if (_attachedToParent)
            {
                throw new InvalidOperationException("Sub-feature registrations are owned by its parent feature.");
            }
        }
    }
}
