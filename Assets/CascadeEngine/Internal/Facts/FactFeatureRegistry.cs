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
        private readonly List<IReducerInvoker> _reducers = new List<IReducerInvoker>();

        private readonly List<IOutputRegistration> _outputs = new List<IOutputRegistration>();
        private readonly List<IOutputRegistration> _absenceOutputs = new List<IOutputRegistration>();
        private readonly Dictionary<CascadeTypeId, IOutputRegistration> _outputsByState =
            new Dictionary<CascadeTypeId, IOutputRegistration>();

        private readonly List<ITransactionalRegistration> _transactionalReducers =
            new List<ITransactionalRegistration>();

        private readonly List<IBatchTransactionalRegistration> _batchTransactionalReducers =
            new List<IBatchTransactionalRegistration>();

        private readonly List<IStateReducerRegistration> _stateReducers =
            new List<IStateReducerRegistration>();

        private readonly FactTypeList _knownFactTypes = new FactTypeList();
        private readonly FactTypeList _negativeConditionFactTypes = new FactTypeList();
        private int _negativeReducerCount;

        internal FactFeatureRegistry()
        {
            AddKnownFact(FactType.Of<DeadFact>());
        }

        internal IReadOnlyList<IOutputRegistration> Outputs => _outputs;
        internal IReadOnlyList<IOutputRegistration> AbsenceOutputs => _absenceOutputs;
        internal IReadOnlyList<ITransactionalRegistration> TransactionalReducers => _transactionalReducers;
        internal IReadOnlyList<IBatchTransactionalRegistration> BatchTransactionalReducers => _batchTransactionalReducers;
        internal IReadOnlyList<IStateReducerRegistration> StateReducers => _stateReducers;
        internal FactType[] KnownFactTypes => _knownFactTypes.ToArray();
        internal bool HasNegativeReducers => _negativeReducerCount > 0;

        internal void AddReducer<TFact, TReducer>(FactType[] forbiddenFacts)
            where TFact : struct, IFact
            where TReducer : IFactReducer<TFact>, new()
        {
            var factType = FactType.Of<TFact>();
            ValidateReducerConditions(factType, forbiddenFacts);
            AddKnownFact(factType);
            AddKnownFacts(forbiddenFacts);
            if (forbiddenFacts.Length > 0)
            {
                AddNegativeConditionFact(factType);
                AddNegativeConditionFacts(forbiddenFacts);
                _negativeReducerCount++;
            }

            var reducer = Create<TReducer>();
            var invoker = new ReducerInvoker<TFact>(
                reducer,
                ToIds(forbiddenFacts),
                typeof(TReducer).Name);
            invoker.BindRoute(this);
            _reducers.Add(invoker);
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
                reducer,
                typeof(TReducer).Name));
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
                reducer,
                typeof(TReducer).Name));
        }

        internal void AddStateReducer<TState, TReducer>()
            where TState : struct, IOutputState
            where TReducer : ITransactionalReducer, new()
        {
            var stateId = _typeCatalog.Register<TState>();
            var reducer = Create<TReducer>();
            _stateReducers.Add(new StateReducerRegistration<TState>(
                stateId,
                reducer,
                typeof(TReducer).Name));
        }

        internal OutputState<TState> AddOutput<TState, TCommitter>(
            string name,
            FactType[] affectedFacts,
            int[] affectedFactPriorities,
            FactType[] absentFacts,
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

            if (affectedFactPriorities == null || affectedFactPriorities.Length != affectedFacts.Length)
            {
                throw new InvalidOperationException(
                    $"Output state '{stateName}' must declare one commit priority per affected fact.");
            }

            if (absentFacts == null)
            {
                throw new ArgumentNullException(nameof(absentFacts));
            }

            AddKnownFacts(affectedFacts);
            AddKnownFacts(absentFacts);
            var output = new OutputState<TState>(_outputs.Count, stateId, name, conflictPolicy);
            var committer = Create<TCommitter>();
            var registration = new OutputRegistration<TState>(
                output,
                affectedFacts,
                affectedFactPriorities,
                absentFacts,
                committer);
            _outputs.Add(registration);
            if (registration.HasAbsenceReconciliation)
            {
                _absenceOutputs.Add(registration);
            }

            _outputsByState.Add(stateId, registration);
            BindAffectedOutputRoutes(affectedFacts, registration);
            return output;
        }

        internal CascadeTypeId RequireFact<TFact>()
            where TFact : struct, IFact
            => RequireFactRoute<TFact>().FactId;

        internal FactEmitRoute<TFact> RequireFactRoute<TFact>()
            where TFact : struct, IFact
            => FactEmitRouteCache<TFact>.Require(this);

        internal string TypeName<T>()
            => _typeCatalog.NameOf<T>();

        internal string Describe(CascadeTypeId id)
            => _typeCatalog.Describe(id);

        /// <summary>
        /// [INTEGRATION] Binds composed registration indexes to typed accepted-fact routes during construction only.
        /// </summary>
        internal void BindTransactionalRoutes()
        {
            foreach (var fact in KnownFactTypes)
            {
                if (!fact.CanCreateBucket) continue;
                var transactional = new List<int>();
                var batch = new List<int>();
                for (var i = 0; i < _transactionalReducers.Count; i++)
                    if (Array.IndexOf(_transactionalReducers[i].RequiredFactIds, fact.Id) >= 0) transactional.Add(i);
                for (var i = 0; i < _batchTransactionalReducers.Count; i++)
                    if (Array.IndexOf(_batchTransactionalReducers[i].RequiredFactIds, fact.Id) >= 0) batch.Add(i);
                fact.ReduceRoute(this).BindWaiters(transactional.ToArray(), batch.ToArray());
            }
        }

        internal void ValidateStateReducerOutputs()
        {
            for (var i = 0; i < _stateReducers.Count; i++)
            {
                var registration = _stateReducers[i];
                if (!_outputsByState.ContainsKey(registration.StateId))
                {
                    throw new InvalidOperationException(
                        $"State reducer '{registration.DebugName}' requires registered output state '{Describe(registration.StateId)}'.");
                }
            }
        }

        public void Dispose()
        {
            var errors = new CleanupErrors();
            for (var i = 0; i < _reducers.Count; i++)
            {
                try
                {
                    _reducers[i].DisposeRegistration();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }

            for (var i = 0; i < _outputs.Count; i++)
            {
                try
                {
                    _outputs[i].DisposeRegistration();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }

            for (var i = 0; i < _transactionalReducers.Count; i++)
            {
                try
                {
                    _transactionalReducers[i].DisposeRegistration();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }

            for (var i = 0; i < _batchTransactionalReducers.Count; i++)
            {
                try
                {
                    _batchTransactionalReducers[i].DisposeRegistration();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }

            for (var i = 0; i < _stateReducers.Count; i++)
            {
                try
                {
                    _stateReducers[i].DisposeRegistration();
                }
                catch (Exception error)
                {
                    errors.Add(error);
                }
            }

            UnbindKnownFactRoutes();
            _reducers.Clear();
            _outputs.Clear();
            _absenceOutputs.Clear();
            _outputsByState.Clear();
            _transactionalReducers.Clear();
            _batchTransactionalReducers.Clear();
            _stateReducers.Clear();
            _knownFactTypes.Clear();
            _negativeConditionFactTypes.Clear();
            _negativeReducerCount = 0;
            _typeCatalog.Clear();
            errors.ThrowIfAny();
        }

        internal void AbsorbFrom(FactFeatureRegistry other)
        {
            _typeCatalog.AbsorbFrom(other._typeCatalog);
            AddKnownFacts(other._knownFactTypes.ToArray());
            AddNegativeConditionFacts(other._negativeConditionFactTypes.ToArray());

            for (var i = 0; i < other._reducers.Count; i++)
            {
                other._reducers[i].BindRoute(this);
                _reducers.Add(other._reducers[i]);
            }
            _negativeReducerCount += other._negativeReducerCount;

            for (var i = 0; i < other._outputs.Count; i++)
            {
                var output = other._outputs[i];
                if (_outputsByState.ContainsKey(output.StateId))
                {
                    throw new InvalidOperationException($"Output state '{output.Name}' is already registered.");
                }

                output.Reindex(_outputs.Count);
                _outputs.Add(output);
                if (output.HasAbsenceReconciliation)
                {
                    _absenceOutputs.Add(output);
                }

                _outputsByState.Add(output.StateId, output);
                BindAffectedOutputRoutes(output.AffectedFacts, output);
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

            for (var i = 0; i < other._stateReducers.Count; i++)
            {
                _stateReducers.Add(other._stateReducers[i]);
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
            if (_knownFactTypes.Add(factType))
            {
                factType.BindRoute(this);
            }
        }

        private void AddNegativeConditionFacts(FactType[] factTypes)
        {
            for (var i = 0; i < factTypes.Length; i++)
            {
                AddNegativeConditionFact(factTypes[i]);
            }
        }

        private void AddNegativeConditionFact(FactType factType)
        {
            AddKnownFact(factType);
            if (_negativeConditionFactTypes.Add(factType))
            {
                factType.MarkNegativeConditionInput(this);
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
            where T : new()
        {
            return new T();
        }

        private void BindAffectedOutputRoutes(FactType[] affectedFacts, IOutputRegistration output)
        {
            for (var i = 0; i < affectedFacts.Length; i++)
            {
                affectedFacts[i].BindAffectedOutput(this, output);
            }
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

        private static void ValidateReducerConditions(
            FactType triggerFact,
            FactType[] forbiddenFacts)
        {
            if (forbiddenFacts == null)
            {
                throw new ArgumentNullException(nameof(forbiddenFacts));
            }

            for (var forbiddenIndex = 0; forbiddenIndex < forbiddenFacts.Length; forbiddenIndex++)
            {
                var forbidden = forbiddenFacts[forbiddenIndex];
                if (forbidden.Id == triggerFact.Id)
                {
                    throw new InvalidOperationException(
                        $"Fact reducer cannot trigger on and forbid fact '{forbidden.DebugName}'.");
                }

                for (var previousIndex = 0; previousIndex < forbiddenIndex; previousIndex++)
                {
                    if (forbidden.Id == forbiddenFacts[previousIndex].Id)
                    {
                        throw new InvalidOperationException(
                            $"Fact reducer forbids fact '{forbidden.DebugName}' more than once.");
                    }
                }
            }
        }

        private void ClearWithoutDisposing()
        {
            UnbindKnownFactRoutes();
            _reducers.Clear();
            _outputs.Clear();
            _absenceOutputs.Clear();
            _outputsByState.Clear();
            _transactionalReducers.Clear();
            _batchTransactionalReducers.Clear();
            _stateReducers.Clear();
            _knownFactTypes.Clear();
            _negativeConditionFactTypes.Clear();
            _negativeReducerCount = 0;
            _typeCatalog.Clear();
        }

        private void UnbindKnownFactRoutes()
        {
            var knownFacts = _knownFactTypes.ToArray();
            for (var i = 0; i < knownFacts.Length; i++)
            {
                knownFacts[i].UnbindRoute(this);
            }
        }
    }
}
