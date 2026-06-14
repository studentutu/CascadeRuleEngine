#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Registration-time container for reducer and output mappings.
    /// </summary>
    internal sealed class FactFeatureRegistry : IDisposable
    {
        private readonly CascadeTypeCatalog _typeCatalog = new CascadeTypeCatalog();
        private readonly Dictionary<CascadeTypeId, List<IReducerInvoker>> _reducersByFact =
            new Dictionary<CascadeTypeId, List<IReducerInvoker>>();

        private readonly List<IOutputRegistration> _outputs = new List<IOutputRegistration>();
        private readonly Dictionary<CascadeTypeId, IOutputRegistration> _outputsByState =
            new Dictionary<CascadeTypeId, IOutputRegistration>();

        private readonly Dictionary<CascadeTypeId, object> _priorityResolvers =
            new Dictionary<CascadeTypeId, object>();

        private readonly List<ITransactionalRegistration> _transactionalReducers =
            new List<ITransactionalRegistration>();

        private readonly List<IBatchTransactionalRegistration> _batchTransactionalReducers =
            new List<IBatchTransactionalRegistration>();

        private readonly FactTypeList _knownFactTypes = new FactTypeList();

        internal IReadOnlyList<IOutputRegistration> Outputs => _outputs;
        internal IReadOnlyList<ITransactionalRegistration> TransactionalReducers => _transactionalReducers;
        internal IReadOnlyList<IBatchTransactionalRegistration> BatchTransactionalReducers => _batchTransactionalReducers;
        internal FactType[] KnownFactTypes => _knownFactTypes.ToArray();

        internal void AddReducer<TFact, TReducer>()
            where TFact : struct, IFact
            where TReducer : IFactReducer<TFact>, new()
        {
            var factType = FactType.Of<TFact>();
            AddKnownFact(factType);
            var reducer = Create<TReducer>();
            var invoker = new ReducerInvoker<TFact>(factType.Id, reducer);

            if (!_reducersByFact.TryGetValue(factType.Id, out var reducers))
            {
                reducers = new List<IReducerInvoker>();
                _reducersByFact.Add(factType.Id, reducers);
            }

            reducers.Add(invoker);
        }

        internal void AddTransactionalReducer<TReducer>(FactType[] requiredFacts)
            where TReducer : ITransactionalReducer, new()
        {
            ValidateRequiredFacts(requiredFacts);
            AddKnownFacts(requiredFacts);
            var reducer = Create<TReducer>();
            _transactionalReducers.Add(new TransactionalRegistration(
                _transactionalReducers.Count,
                ToIds(requiredFacts),
                reducer));
        }

        internal void AddBatchTransactionalReducer<TReducer>(FactType[] requiredFacts)
            where TReducer : IBatchTransactionalReducer, new()
        {
            ValidateRequiredFacts(requiredFacts);
            AddKnownFacts(requiredFacts);
            var reducer = Create<TReducer>();
            _batchTransactionalReducers.Add(new BatchTransactionalRegistration(
                _batchTransactionalReducers.Count,
                ToIds(requiredFacts),
                reducer));
        }

        internal OutputState<TState> AddOutput<TState, TCommitter>(
            string name,
            FactType[] affectedFacts,
            CommitConflictPolicy conflictPolicy)
            where TState : struct, IOutputState
            where TCommitter : IOutputCommitter<TState>, new()
        {
            var stateId = _typeCatalog.Register<TState>();
            var stateName = _typeCatalog.NameOf<TState>();
            if (_outputsByState.ContainsKey(stateId))
            {
                throw new InvalidOperationException($"Output state '{stateName}' is already registered.");
            }

            if (affectedFacts == null || affectedFacts.Length == 0)
            {
                throw new InvalidOperationException($"Output state '{stateName}' must declare at least one affected fact.");
            }

            AddKnownFacts(affectedFacts);
            var output = new OutputState<TState>(_outputs.Count, stateId, name, conflictPolicy);
            var committer = Create<TCommitter>();
            var registration = new OutputRegistration<TState>(output, ToIds(affectedFacts), committer);
            _outputs.Add(registration);
            _outputsByState.Add(stateId, registration);
            return output;
        }

        internal bool TryGetReducers(CascadeTypeId factId, out List<IReducerInvoker> reducers)
            => _reducersByFact.TryGetValue(factId, out reducers);

        internal bool TryGetOutput<TState>(out OutputRegistration<TState> output)
            where TState : struct, IOutputState
        {
            if (_outputsByState.TryGetValue(RequireOutput<TState>(), out var registration))
            {
                output = (OutputRegistration<TState>)registration;
                return true;
            }

            output = null!;
            return false;
        }

        internal bool ContainsOutput<TState>(OutputState<TState> output)
            where TState : struct, IOutputState
        {
            return _outputsByState.TryGetValue(RequireOutput<TState>(), out var registration)
                && ReferenceEquals(((OutputRegistration<TState>)registration).Output, output);
        }

        internal CascadeTypeId RequireFact<TFact>()
            where TFact : struct, IFact
            => _typeCatalog.Require<TFact>();

        internal CascadeTypeId RequireOutput<TState>()
            where TState : struct, IOutputState
            => _typeCatalog.Require<TState>();

        internal string TypeName<T>()
            => _typeCatalog.NameOf<T>();

        internal string Describe(CascadeTypeId id)
            => _typeCatalog.Describe(id);

        internal FactPriority ResolvePriority<TFact>(CascadeTypeId factId, in TFact fact)
            where TFact : struct, IFact
        {
            if (_priorityResolvers.TryGetValue(factId, out var resolver))
            {
                return ((IFactPriorityResolver<TFact>)resolver).Resolve(in fact);
            }

            return FactPriority.Normal;
        }

        public void Dispose()
        {
            foreach (var reducerList in _reducersByFact.Values)
            {
                for (var i = 0; i < reducerList.Count; i++)
                {
                    reducerList[i].DisposeRegistration();
                }
            }

            for (var i = 0; i < _outputs.Count; i++)
            {
                _outputs[i].DisposeRegistration();
            }

            for (var i = 0; i < _transactionalReducers.Count; i++)
            {
                _transactionalReducers[i].DisposeRegistration();
            }

            for (var i = 0; i < _batchTransactionalReducers.Count; i++)
            {
                _batchTransactionalReducers[i].DisposeRegistration();
            }

            foreach (var resolver in _priorityResolvers.Values)
            {
                if (resolver is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }

            _reducersByFact.Clear();
            _outputs.Clear();
            _outputsByState.Clear();
            _priorityResolvers.Clear();
            _transactionalReducers.Clear();
            _batchTransactionalReducers.Clear();
            _knownFactTypes.Clear();
            _typeCatalog.Clear();
        }

        internal void AbsorbFrom(FactFeatureRegistry other)
        {
            _typeCatalog.AbsorbFrom(other._typeCatalog);
            AddKnownFacts(other._knownFactTypes.ToArray());

            foreach (var pair in other._priorityResolvers)
            {
                if (_priorityResolvers.ContainsKey(pair.Key))
                {
                    throw new InvalidOperationException($"Fact '{_typeCatalog.Describe(pair.Key)}' already has a priority resolver.");
                }

                _priorityResolvers.Add(pair.Key, pair.Value);
            }

            foreach (var pair in other._reducersByFact)
            {
                if (!_reducersByFact.TryGetValue(pair.Key, out var reducers))
                {
                    reducers = new List<IReducerInvoker>();
                    _reducersByFact.Add(pair.Key, reducers);
                }

                reducers.AddRange(pair.Value);
            }

            for (var i = 0; i < other._outputs.Count; i++)
            {
                var output = other._outputs[i];
                if (_outputsByState.ContainsKey(output.StateId))
                {
                    throw new InvalidOperationException($"Output state '{output.Name}' is already registered.");
                }

                output.Reindex(_outputs.Count);
                _outputs.Add(output);
                _outputsByState.Add(output.StateId, output);
            }

            for (var i = 0; i < other._transactionalReducers.Count; i++)
            {
                AddKnownFacts(other._transactionalReducers[i].RequiredFactIds);
                other._transactionalReducers[i].Reindex(_transactionalReducers.Count);
                _transactionalReducers.Add(other._transactionalReducers[i]);
            }

            for (var i = 0; i < other._batchTransactionalReducers.Count; i++)
            {
                AddKnownFacts(other._batchTransactionalReducers[i].RequiredFactIds);
                other._batchTransactionalReducers[i].Reindex(_batchTransactionalReducers.Count);
                _batchTransactionalReducers.Add(other._batchTransactionalReducers[i]);
            }

            other.ClearWithoutDisposing();
        }

        private void AddKnownFacts(FactType[] factTypes)
        {
            for (var i = 0; i < factTypes.Length; i++)
            {
                AddKnownFact(factTypes[i]);
            }
        }

        private void AddKnownFact(FactType factType)
        {
            factType.Register(_typeCatalog);
            _knownFactTypes.Add(factType);

            if (!factType.CanCreatePriorityResolver || _priorityResolvers.ContainsKey(factType.Id))
            {
                return;
            }

            _priorityResolvers.Add(factType.Id, factType.CreatePriorityResolver());
        }

        private void AddKnownFacts(CascadeTypeId[] factIds)
        {
            for (var i = 0; i < factIds.Length; i++)
            {
                _knownFactTypes.Add(new FactType(factIds[i]));
            }
        }

        private static T Create<T>()
            where T : new()
        {
            return new T();
        }

        private static CascadeTypeId[] ToIds(FactType[] factTypes)
        {
            var result = new CascadeTypeId[factTypes.Length];
            for (var i = 0; i < factTypes.Length; i++)
            {
                result[i] = factTypes[i].Id;
            }

            return result;
        }

        private static void ValidateRequiredFacts(FactType[] requiredFacts)
        {
            if (requiredFacts == null || requiredFacts.Length == 0)
            {
                throw new InvalidOperationException("Transactional reducer must declare at least one required fact.");
            }
        }

        private void ClearWithoutDisposing()
        {
            _reducersByFact.Clear();
            _outputs.Clear();
            _outputsByState.Clear();
            _priorityResolvers.Clear();
            _transactionalReducers.Clear();
            _batchTransactionalReducers.Clear();
            _knownFactTypes.Clear();
            _typeCatalog.Clear();
        }
    }
}
