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
        private readonly Dictionary<CascadeTypeId, List<IReducerInvoker>> _reducersByFact =
            new Dictionary<CascadeTypeId, List<IReducerInvoker>>();

        private readonly List<IOutputRegistration> _outputs = new List<IOutputRegistration>();
        private readonly Dictionary<CascadeTypeId, IOutputRegistration> _outputsByState =
            new Dictionary<CascadeTypeId, IOutputRegistration>();

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
            where TReducer : IFactReducer<TFact>
        {
            var reducer = Create<TReducer>();
            var invoker = new ReducerInvoker<TFact>(reducer);
            var factType = FactType.Of<TFact>();
            _knownFactTypes.Add(factType);

            if (!_reducersByFact.TryGetValue(factType.Id, out var reducers))
            {
                reducers = new List<IReducerInvoker>();
                _reducersByFact.Add(factType.Id, reducers);
            }

            reducers.Add(invoker);
        }

        internal void AddTransactionalReducer<TReducer>(FactType[] requiredFacts)
            where TReducer : ITransactionalReducer
        {
            ValidateRequiredFacts(requiredFacts);
            var reducer = Create<TReducer>();
            AddKnownFacts(requiredFacts);
            _transactionalReducers.Add(new TransactionalRegistration(
                _transactionalReducers.Count,
                ToIds(requiredFacts),
                reducer));
        }

        internal void AddBatchTransactionalReducer<TReducer>(FactType[] requiredFacts)
            where TReducer : IBatchTransactionalReducer
        {
            ValidateRequiredFacts(requiredFacts);
            var reducer = Create<TReducer>();
            AddKnownFacts(requiredFacts);
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
            where TCommitter : IOutputCommitter<TState>
        {
            var stateId = CascadeTypeIdentity.RequireId<TState>();
            var stateName = CascadeTypeIdentity<TState>.DebugName;
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
            if (_outputsByState.TryGetValue(CascadeTypeIdentity.RequireId<TState>(), out var registration))
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
            return _outputsByState.TryGetValue(CascadeTypeIdentity.RequireId<TState>(), out var registration)
                && ReferenceEquals(((OutputRegistration<TState>)registration).Output, output);
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

            _reducersByFact.Clear();
            _outputs.Clear();
            _outputsByState.Clear();
            _transactionalReducers.Clear();
            _batchTransactionalReducers.Clear();
            _knownFactTypes.Clear();
        }

        internal void AbsorbFrom(FactFeatureRegistry other)
        {
            AddKnownFacts(other._knownFactTypes.ToArray());

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
                _knownFactTypes.Add(factTypes[i]);
            }
        }

        private void AddKnownFacts(CascadeTypeId[] factIds)
        {
            for (var i = 0; i < factIds.Length; i++)
            {
                _knownFactTypes.Add(new FactType(factIds[i]));
            }
        }

        private static T Create<T>()
        {
            var instance = Activator.CreateInstance(typeof(T));
            if (instance == null)
            {
                throw new InvalidOperationException($"Could not create '{typeof(T).Name}'. Reducers and committers need a public parameterless constructor in the MVP.");
            }

            return (T)instance;
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
            _transactionalReducers.Clear();
            _batchTransactionalReducers.Clear();
            _knownFactTypes.Clear();
        }
    }
}
