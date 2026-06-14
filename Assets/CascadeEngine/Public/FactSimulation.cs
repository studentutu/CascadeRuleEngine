#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace CascadeEngineApi
{
    /// <summary>
    /// Concrete fact-reduction-mutation engine: facts reduce to closure, committers write durable output state once.
    /// </summary>
    public sealed class FactSimulation :
        IFactSimulation,
        IDisposable,
        ICommittedStateStore,
        IReduceContext,
        ICommitContext,
        IEntityQuery
    {
        private readonly FactFeature _feature;
        private readonly FactFeatureRegistry _registry;
        private readonly EntityStore _entities = new EntityStore();
        private readonly FactStore _facts = new FactStore();
        private readonly Dictionary<CascadeTypeId, IStateBucket> _stateBuckets = new Dictionary<CascadeTypeId, IStateBucket>();
        private readonly EntityFactView _factView;
        private readonly List<ICommitAction> _commitActions = new List<ICommitAction>();
        private readonly EntityRefBuffer _queryBuffer = new EntityRefBuffer(64);
        private readonly EntityRefBuffer _transactionBuffer = new EntityRefBuffer(64);
        private readonly EntityRefBuffer _batchBuffer = new EntityRefBuffer(64);
        private readonly HashSet<FiredReducerKey> _firedTransactional = new HashSet<FiredReducerKey>();
        private readonly HashSet<FiredReducerKey> _firedBatchEntities = new HashSet<FiredReducerKey>();
        private SimulationTick _tick;
        private int _currentCausalDepth;
        private int _mutationCount;
        private bool _disposed;

        /// <summary>
        /// [INTEGRATION] Range: fully constructed feature. Condition: bootstrap. Output: simulation bound to the feature registrations.
        /// </summary>
        public FactSimulation(FactFeature feature)
        {
            if (feature == null)
            {
                throw new ArgumentNullException(nameof(feature));
            }

            if (feature.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(FactFeature));
            }

            if (feature.IsAttachedToParent)
            {
                throw new InvalidOperationException("Sub-feature registrations are owned by its parent feature.");
            }

            _feature = feature;
            _registry = feature.Registry;
            _factView = new EntityFactView(_facts, _registry);
            CreateRegisteredStateBuckets();
        }

        public ICommittedStateStore State
        {
            get
            {
                ThrowIfDisposed();
                return this;
            }
        }

        public IEntityQuery Query
        {
            get
            {
                ThrowIfDisposed();
                return this;
            }
        }

        public SimulationTick Tick => _tick;
        public int MutationCount
        {
            get
            {
                ThrowIfDisposed();
                return _mutationCount;
            }
        }

        public SimulationResult LastResult { get; private set; }

        /// <summary>
        /// [INTEGRATION] Range: host capacity hints. Condition: call before gameplay load/tick. Output: reusable stores and buffers are pre-sized only.
        /// </summary>
        public void Warmup(WarmupCapacityHints hints)
        {
            ThrowIfDisposed();

            if (hints == null)
            {
                throw new ArgumentNullException(nameof(hints));
            }

            var entityCapacity = NormalizeCapacity(hints.EntityCapacity);
            _facts.Warmup(
                entityCapacity,
                NormalizeCapacity(hints.FactQueueCapacity),
                NormalizeCapacity(hints.FactsPerEntityPerTypeCapacity),
                _registry.KnownFactTypes);

            _queryBuffer.EnsureCapacity(NormalizeCapacity(hints.QueryEntityCapacity));
            _transactionBuffer.EnsureCapacity(NormalizeCapacity(hints.TransactionEntityCapacity));
            _batchBuffer.EnsureCapacity(NormalizeCapacity(hints.BatchEntityCapacity));
            EnsureListCapacity(_commitActions, NormalizeCapacity(hints.CommitActionCapacity));

            var outputStateCapacity = NormalizeCapacity(hints.OutputStateCapacityPerOutput);
            var mutationCapacity = NormalizeCapacity(hints.MutationCapacityPerOutput);
            for (var i = 0; i < _registry.Outputs.Count; i++)
            {
                _registry.Outputs[i].Warmup(this, outputStateCapacity, mutationCapacity);
            }
        }

        public EntityRef CreateEntity()
        {
            ThrowIfDisposed();

            var entity = _entities.Create();
            _facts.EnsureEntityCapacity(_entities.Count);
            return entity;
        }

        public void DestroyEntity(EntityRef entity)
        {
            ThrowIfDisposed();

            if (!_entities.Destroy(entity))
            {
                return;
            }

            for (var i = 0; i < _registry.Outputs.Count; i++)
            {
                _registry.Outputs[i].DeleteState(this, entity);
            }

            RefreshMutationCount();
        }

        public bool IsDestroyed(EntityRef entity)
        {
            ThrowIfDisposed();
            return _entities.IsDestroyed(entity);
        }

        public void Emit<TFact>(EntityRef entity, in TFact fact)
            where TFact : struct, IFact
        {
            ThrowIfDisposed();
            EmitCore(entity, in fact, _currentCausalDepth);
        }

        public void EmitGlobal<TFact>(in TFact fact)
            where TFact : struct, IFact
        {
            ThrowIfDisposed();
            EmitCore(EntityRef.Global, in fact, _currentCausalDepth);
        }

        public SimulationResult RunTick(ReduceOptions options)
        {
            ThrowIfDisposed();

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            ClearMutations();
            _tick = new SimulationTick(_tick.Value + 1);
            CurrentGuardrails = options.Guardrails;
            _firedTransactional.Clear();
            _firedBatchEntities.Clear();

            var stopwatch = Stopwatch.StartNew();
            var processedFacts = 0;
            var reducerInvocations = 0;
            var transactionalInvocations = 0;
            var passes = 0;

            try
            {
                while (true)
                {
                    passes++;
                    if (passes > options.MaxPasses)
                    {
                        return HandleIncomplete(options, processedFacts, reducerInvocations, transactionalInvocations, "maximum pass count exceeded");
                    }

                    while (_facts.HasQueuedFacts)
                    {
                        if (BudgetExceeded(options, stopwatch, processedFacts))
                        {
                            return HandleIncomplete(options, processedFacts, reducerInvocations, transactionalInvocations, "fact budget exceeded");
                        }

                        _facts.TryPop(options.BudgetMode, out var queued);
                        processedFacts++;

                        if (!queued.Entity.IsGlobal && _entities.IsDestroyed(queued.Entity))
                        {
                            continue;
                        }

                        if (!_registry.TryGetReducers(queued.FactId, out var reducers))
                        {
                            continue;
                        }

                        for (var i = 0; i < reducers.Count; i++)
                        {
                            reducerInvocations++;
                            if (reducerInvocations > options.Guardrails.MaxReducerInvocationsPerTick)
                            {
                                throw new InvalidOperationException($"Reducer invocation limit '{options.Guardrails.MaxReducerInvocationsPerTick}' exceeded.");
                            }

                            _currentCausalDepth = queued.Depth + 1;
                            reducers[i].Reduce(this, in queued);
                            _currentCausalDepth = 0;
                        }
                    }

                    var ranTransactional = RunReadyTransactionalReducers(options, ref transactionalInvocations);
                    var ranBatch = RunReadyBatchReducers(options, ref transactionalInvocations);
                    if (!ranTransactional && !ranBatch)
                    {
                        break;
                    }
                }

                CommitTouchedOutputs();
                RefreshMutationCount();
                LastResult = CreateResult(true, processedFacts, reducerInvocations, transactionalInvocations);
                return LastResult;
            }
            catch
            {
                _commitActions.Clear();
                ClearMutations();
                _facts.Clear();
                _currentCausalDepth = 0;
                throw;
            }
            finally
            {
                _facts.Clear();
                _currentCausalDepth = 0;
            }
        }

        /// <summary>
        /// [INTEGRATION] Range: terminal simulation lifecycle. Condition: scene/domain unload or host replacement. Output: runtime-owned stores and the bound feature registry are disposed once.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _commitActions.Clear();
            _firedTransactional.Clear();
            _firedBatchEntities.Clear();
            _facts.DisposeStore();

            foreach (var bucket in _stateBuckets.Values)
            {
                bucket.DisposeBucket();
            }

            _stateBuckets.Clear();
            _entities.DisposeStore();
            _feature.Dispose();
            _currentCausalDepth = 0;
            _mutationCount = 0;
            _disposed = true;
            GC.SuppressFinalize(this);
        }

        public void ForEachMutation<TState>(
            OutputState<TState> output,
            StateMutationHandler<TState> handler)
            where TState : struct, IOutputState
        {
            ThrowIfDisposed();

            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            if (!_registry.ContainsOutput(output))
            {
                throw new InvalidOperationException($"Output '{output.Name}' belongs to a different feature registration.");
            }

            GetStateBucket<TState>().ForEachMutation(handler);
        }

        /// <summary>
        /// [INTEGRATION] Range: live entity and registered output state. Condition: authoritative bootstrap/load. Output: committed state set without mutation output.
        /// </summary>
        public void SetStateSilently<TState>(EntityRef entity, in TState state)
            where TState : struct, IOutputState
        {
            ThrowIfDisposed();
            ThrowIfNotLive(entity);
            GetStateBucket<TState>().SetSilently(entity, state);
        }

        public bool Has<TState>(EntityRef entity)
            where TState : struct, IOutputState
        {
            ThrowIfDisposed();
            return GetStateBucket<TState>().Has(entity);
        }

        public TState Get<TState>(EntityRef entity)
            where TState : struct, IOutputState
        {
            ThrowIfDisposed();
            return GetStateBucket<TState>().Get(entity);
        }

        public bool TryGet<TState>(EntityRef entity, out TState state)
            where TState : struct, IOutputState
        {
            ThrowIfDisposed();
            return GetStateBucket<TState>().TryGet(entity, out state);
        }

        public IEntityFactView Facts(EntityRef entity)
        {
            ThrowIfDisposed();
            return _factView.Bind(entity);
        }

        public bool HasState<TState>(EntityRef entity)
            where TState : struct, IOutputState
            => Has<TState>(entity);

        public TState GetState<TState>(EntityRef entity)
            where TState : struct, IOutputState
            => Get<TState>(entity);

        public bool TryGetState<TState>(EntityRef entity, out TState state)
            where TState : struct, IOutputState
            => TryGet(entity, out state);

        public EntityQueryResult With<TState>()
            where TState : struct, IOutputState
        {
            ThrowIfDisposed();

            var count = 0;
            EnsureQueryCapacity(_entities.Count);
            for (var i = 0; i < _entities.Count; i++)
            {
                var entity = new EntityRef(i);
                if (_entities.IsLive(entity) && Has<TState>(entity))
                {
                    _queryBuffer[count] = entity;
                    count++;
                }
            }

            return _queryBuffer.ToQueryResult(count);
        }

        public EntityQueryResult With<TStateA, TStateB>()
            where TStateA : struct, IOutputState
            where TStateB : struct, IOutputState
        {
            ThrowIfDisposed();

            var count = 0;
            EnsureQueryCapacity(_entities.Count);
            for (var i = 0; i < _entities.Count; i++)
            {
                var entity = new EntityRef(i);
                if (_entities.IsLive(entity) && Has<TStateA>(entity) && Has<TStateB>(entity))
                {
                    _queryBuffer[count] = entity;
                    count++;
                }
            }

            return _queryBuffer.ToQueryResult(count);
        }

        public EntityQueryResult WithFact<TFact>()
            where TFact : struct, IFact
        {
            ThrowIfDisposed();

            EnsureTransactionCapacity();
            _facts.CopyTouchedEntities(_transactionBuffer, out var touchedCount);

            var count = 0;
            EnsureQueryCapacity(touchedCount);
            for (var i = 0; i < touchedCount; i++)
            {
                var entity = _transactionBuffer[i];
                if (_facts.Has(entity, _registry.RequireFact<TFact>()))
                {
                    _queryBuffer[count] = entity;
                    count++;
                }
            }

            return _queryBuffer.ToQueryResult(count);
        }

        internal StateBucket<TState> GetStateBucket<TState>()
            where TState : struct, IOutputState
        {
            var stateId = _registry.RequireOutput<TState>();
            if (_stateBuckets.TryGetValue(stateId, out var bucket))
            {
                return (StateBucket<TState>)bucket;
            }

            throw new InvalidOperationException($"Output state '{_registry.Describe(stateId)}' is not registered.");
        }

        internal CascadeCapacitySnapshot CaptureCapacitySnapshot(int warmedEntityCapacity)
        {
            ThrowIfDisposed();

            var minimumStateCapacityHint = int.MaxValue;
            var minimumMutationCapacity = int.MaxValue;

            foreach (var bucket in _stateBuckets.Values)
            {
                if (bucket.StateCapacityHint < minimumStateCapacityHint)
                {
                    minimumStateCapacityHint = bucket.StateCapacityHint;
                }

                if (bucket.MutationCapacity < minimumMutationCapacity)
                {
                    minimumMutationCapacity = bucket.MutationCapacity;
                }
            }

            return new CascadeCapacitySnapshot(
                _facts.QueueCapacity,
                _facts.TouchedEntityCapacity,
                _facts.FactCounterEntityCapacity,
                _facts.BucketCount,
                _facts.MinimumBucketEntityCapacity(),
                _facts.MinimumBucketTouchedEntityCapacity(),
                _facts.MinimumFactListCapacity(warmedEntityCapacity),
                _queryBuffer.Capacity,
                _transactionBuffer.Capacity,
                _batchBuffer.Capacity,
                _commitActions.Capacity,
                minimumStateCapacityHint == int.MaxValue ? 0 : minimumStateCapacityHint,
                minimumMutationCapacity == int.MaxValue ? 0 : minimumMutationCapacity);
        }

        private void EmitCore<TFact>(EntityRef entity, in TFact fact, int parentDepth)
            where TFact : struct, IFact
        {
            _facts.Emit(_entities, entity, _registry.RequireFact<TFact>(), in fact, parentDepth, CurrentGuardrails);
        }

        private FactGuardrails CurrentGuardrails { get; set; } = new FactGuardrails();

        private SimulationResult HandleIncomplete(
            ReduceOptions options,
            int processedFacts,
            int reducerInvocations,
            int transactionalInvocations,
            string reason)
        {
            if (options.IncompleteCommitMode == IncompleteCommitMode.Throw)
            {
                throw new InvalidOperationException($"Cascade reduction did not close: {reason}.");
            }

            ClearMutations();
            LastResult = CreateResult(false, processedFacts, reducerInvocations, transactionalInvocations);
            return LastResult;
        }

        private bool BudgetExceeded(ReduceOptions options, Stopwatch stopwatch, int processedFacts)
        {
            if (processedFacts >= options.MaxFacts)
            {
                return true;
            }

            return options.MaxMilliseconds > 0 && stopwatch.ElapsedMilliseconds > options.MaxMilliseconds;
        }

        private bool RunReadyTransactionalReducers(ReduceOptions options, ref int transactionalInvocations)
        {
            if (_registry.TransactionalReducers.Count == 0)
            {
                return false;
            }

            EnsureTransactionCapacity();
            _facts.CopyTouchedEntities(_transactionBuffer, out var touchedCount);
            var ranAny = false;

            for (var entityIndex = 0; entityIndex < touchedCount; entityIndex++)
            {
                var entity = _transactionBuffer[entityIndex];
                if (_entities.IsDestroyed(entity))
                {
                    continue;
                }

                for (var reducerIndex = 0; reducerIndex < _registry.TransactionalReducers.Count; reducerIndex++)
                {
                    var registration = _registry.TransactionalReducers[reducerIndex];
                    if (!_facts.HasAll(entity, registration.RequiredFactIds))
                    {
                        continue;
                    }

                    var firedKey = new FiredReducerKey(registration.Index, entity.Value);
                    if (!_firedTransactional.Add(firedKey))
                    {
                        continue;
                    }

                    transactionalInvocations++;
                    if (transactionalInvocations > options.Guardrails.MaxTransactionalReducerInvocationsPerTick)
                    {
                        throw new InvalidOperationException($"Transactional reducer invocation limit '{options.Guardrails.MaxTransactionalReducerInvocationsPerTick}' exceeded.");
                    }

                    registration.Reduce(this, entity);
                    ranAny = true;
                }
            }

            return ranAny;
        }

        private bool RunReadyBatchReducers(ReduceOptions options, ref int transactionalInvocations)
        {
            if (_registry.BatchTransactionalReducers.Count == 0)
            {
                return false;
            }

            EnsureTransactionCapacity();
            _facts.CopyTouchedEntities(_transactionBuffer, out var touchedCount);
            var ranAny = false;

            for (var reducerIndex = 0; reducerIndex < _registry.BatchTransactionalReducers.Count; reducerIndex++)
            {
                var registration = _registry.BatchTransactionalReducers[reducerIndex];
                var batchCount = 0;
                EnsureBatchCapacity(touchedCount);

                for (var entityIndex = 0; entityIndex < touchedCount; entityIndex++)
                {
                    var entity = _transactionBuffer[entityIndex];
                    if (_entities.IsDestroyed(entity) || !_facts.HasAll(entity, registration.RequiredFactIds))
                    {
                        continue;
                    }

                    var firedKey = new FiredReducerKey(registration.Index, entity.Value);
                    if (!_firedBatchEntities.Add(firedKey))
                    {
                        continue;
                    }

                    _batchBuffer[batchCount] = entity;
                    batchCount++;
                }

                if (batchCount == 0)
                {
                    continue;
                }

                transactionalInvocations++;
                if (transactionalInvocations > options.Guardrails.MaxTransactionalReducerInvocationsPerTick)
                {
                    throw new InvalidOperationException($"Transactional reducer invocation limit '{options.Guardrails.MaxTransactionalReducerInvocationsPerTick}' exceeded.");
                }

                registration.ReduceBatch(this, _batchBuffer.AsSpan(batchCount));
                ranAny = true;
            }

            return ranAny;
        }

        private void CommitTouchedOutputs()
        {
            _commitActions.Clear();
            EnsureTransactionCapacity();
            _facts.CopyTouchedEntities(_transactionBuffer, out var touchedCount);

            for (var entityIndex = 0; entityIndex < touchedCount; entityIndex++)
            {
                var entity = _transactionBuffer[entityIndex];
                if (_entities.IsDestroyed(entity))
                {
                    continue;
                }

                for (var outputIndex = 0; outputIndex < _registry.Outputs.Count; outputIndex++)
                {
                    var output = _registry.Outputs[outputIndex];
                    if (output.IsAffectedBy(_facts, entity))
                    {
                        var action = output.CreateCommitAction(this, entity);
                        if (action != null)
                        {
                            _commitActions.Add(action);
                        }
                    }
                }
            }

            for (var i = 0; i < _commitActions.Count; i++)
            {
                _commitActions[i].Apply();
            }

            _commitActions.Clear();
        }

        private void CreateRegisteredStateBuckets()
        {
            for (var i = 0; i < _registry.Outputs.Count; i++)
            {
                var output = _registry.Outputs[i];
                if (_stateBuckets.ContainsKey(output.StateId))
                {
                    throw new InvalidOperationException($"Output state '{output.Name}' registered twice.");
                }

                _stateBuckets.Add(output.StateId, output.CreateStateBucket());
            }
        }

        private void ClearMutations()
        {
            for (var i = 0; i < _registry.Outputs.Count; i++)
            {
                _registry.Outputs[i].ClearMutations(this);
            }

            _mutationCount = 0;
        }

        private void RefreshMutationCount()
        {
            var count = 0;
            for (var i = 0; i < _registry.Outputs.Count; i++)
            {
                count += _registry.Outputs[i].MutationCount(this);
            }

            _mutationCount = count;
        }

        private SimulationResult CreateResult(
            bool complete,
            int processedFacts,
            int reducerInvocations,
            int transactionalInvocations)
        {
            RefreshMutationCount();
            return new SimulationResult(
                _tick,
                complete,
                _facts.AcceptedFacts,
                processedFacts,
                _facts.DeduplicatedFacts,
                _facts.RejectedDestroyedEntityFacts,
                reducerInvocations,
                transactionalInvocations,
                _facts.TouchedEntityCount,
                _mutationCount);
        }

        private void ThrowIfNotLive(EntityRef entity)
        {
            _entities.Validate(entity);
            if (_entities.IsDestroyed(entity))
            {
                throw new InvalidOperationException($"Destroyed entity '{entity}' cannot receive output state.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed || _feature.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(FactSimulation));
            }
        }

        private void EnsureQueryCapacity(int required)
        {
            _queryBuffer.EnsureCapacity(required);
        }

        private void EnsureTransactionCapacity()
        {
            var required = Math.Max(_entities.Count, 1);
            _transactionBuffer.EnsureCapacity(required);
        }

        private void EnsureBatchCapacity(int required)
        {
            _batchBuffer.EnsureCapacity(required);
        }

        private static void EnsureListCapacity<T>(List<T> list, int capacity)
        {
            if (list.Capacity < capacity)
            {
                list.Capacity = capacity;
            }
        }

        private static int NormalizeCapacity(int capacity)
            => Math.Max(capacity, 1);

        private readonly struct FiredReducerKey : IEquatable<FiredReducerKey>
        {
            private readonly int _registrationIndex;
            private readonly int _entityValue;

            internal FiredReducerKey(int registrationIndex, int entityValue)
            {
                _registrationIndex = registrationIndex;
                _entityValue = entityValue;
            }

            public bool Equals(FiredReducerKey other)
                => _registrationIndex == other._registrationIndex && _entityValue == other._entityValue;

            public override bool Equals(object? obj)
                => obj is FiredReducerKey other && Equals(other);

            public override int GetHashCode()
                => unchecked((_registrationIndex * 397) ^ _entityValue);
        }
    }
}
