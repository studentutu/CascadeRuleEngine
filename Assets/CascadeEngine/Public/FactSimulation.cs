#nullable enable

using System;
using System.Collections.Generic;

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
        private readonly EntityRefBuffer _queryBuffer = new EntityRefBuffer(64);
        private readonly EntityRefBuffer _transactionBuffer = new EntityRefBuffer(64);
        private readonly EntityRefBuffer _batchBuffer = new EntityRefBuffer(64);
        private readonly FiredReducerTracker _firedTransactional;
        private readonly FiredReducerTracker _firedBatchEntities;
        private readonly PartialSimulation _partial;
        private int[] _commitOutputMarks = new int[0];
        private int _commitOutputMark;
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
            _firedTransactional = new FiredReducerTracker(_registry.TransactionalReducers.Count, 64);
            _firedBatchEntities = new FiredReducerTracker(_registry.BatchTransactionalReducers.Count, 64);
            _partial = new PartialSimulation(
                this,
                _registry,
                _entities,
                _facts,
                _transactionBuffer,
                _batchBuffer,
                _firedTransactional,
                _firedBatchEntities);
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

        public SimulationTick Tick => _partial.Tick;
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
                hints.FactListCapacityMode,
                _registry.KnownFactTypes);

            _queryBuffer.EnsureCapacity(NormalizeCapacity(hints.QueryEntityCapacity));
            _transactionBuffer.EnsureCapacity(NormalizeCapacity(hints.TransactionEntityCapacity));
            _batchBuffer.EnsureCapacity(NormalizeCapacity(hints.BatchEntityCapacity));
            EnsureCommitOutputMarkCapacity();
            _firedTransactional.Warmup(_registry.TransactionalReducers.Count, entityCapacity);
            _firedBatchEntities.Warmup(_registry.BatchTransactionalReducers.Count, entityCapacity);

            var outputStateCapacity = NormalizeCapacity(hints.OutputStateCapacityPerOutput);
            var mutationCapacity = NormalizeCapacity(hints.MutationCapacityPerOutput);
            var commitActionCapacity = NormalizeCapacity(hints.CommitActionCapacity);
            for (var i = 0; i < _registry.Outputs.Count; i++)
            {
                _registry.Outputs[i].Warmup(this, outputStateCapacity, mutationCapacity, commitActionCapacity);
            }
        }

        public EntityRef CreateEntity()
        {
            ThrowIfDisposed();

            var entity = _entities.Create();
            _facts.EnsureEntityCapacity(_entities.Count);
            _firedTransactional.Warmup(_registry.TransactionalReducers.Count, _entities.Count);
            _firedBatchEntities.Warmup(_registry.BatchTransactionalReducers.Count, _entities.Count);
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
            EmitCore(entity, in fact, _partial.CurrentCausalDepth);
        }

        public void EmitGlobal<TFact>(in TFact fact)
            where TFact : struct, IFact
        {
            ThrowIfDisposed();
            EmitCore(EntityRef.Global, in fact, _partial.CurrentCausalDepth);
        }

        public SimulationResult RunTick(ReduceOptions options)
        {
            ThrowIfDisposed();
            ThrowIfOptionsInvalid(options);

            LastResult = _partial.RunTick(options);
            return LastResult;
        }

        /// <summary>
        /// [INTEGRATION] Range: one reduction pass. Condition: open or pending tick. Output: true only after closure and commit.
        /// </summary>
        public bool RunTickIncremental(ReduceOptions options, out SimulationResult result)
        {
            ThrowIfDisposed();
            ThrowIfOptionsInvalid(options);

            var complete = _partial.RunTickIncremental(options, out result);
            LastResult = result;
            return complete;
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

            ClearQueuedCommitActions();
            _firedTransactional.DisposeTracker();
            _firedBatchEntities.DisposeTracker();
            _facts.DisposeStore();
            _commitOutputMarks = Array.Empty<int>();

            UnbindRegisteredStateBuckets();
            foreach (var bucket in _stateBuckets.Values)
            {
                bucket.DisposeBucket();
            }

            _stateBuckets.Clear();
            _entities.DisposeStore();
            _feature.Dispose();
            _partial.DisposePartial();
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

            var route = OutputStateRouteCache<TState>.Require(this);
            if (!ReferenceEquals(route.Output, output))
            {
                throw new InvalidOperationException($"Output '{output.Name}' belongs to a different feature registration.");
            }

            route.Bucket.ForEachMutation(handler);
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

            var bucket = GetStateBucket<TState>();
            var count = 0;
            EnsureQueryCapacity(_entities.Count);
            for (var i = 0; i < _entities.Count; i++)
            {
                var entity = new EntityRef(i);
                if (_entities.IsLive(entity) && bucket.Has(entity))
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

            var bucketA = GetStateBucket<TStateA>();
            var bucketB = GetStateBucket<TStateB>();
            var count = 0;
            EnsureQueryCapacity(_entities.Count);
            for (var i = 0; i < _entities.Count; i++)
            {
                var entity = new EntityRef(i);
                if (_entities.IsLive(entity) && bucketA.Has(entity) && bucketB.Has(entity))
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
            => OutputStateRouteCache<TState>.Require(this).Bucket;

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
                MinimumCommitActionCapacity(),
                minimumStateCapacityHint == int.MaxValue ? 0 : minimumStateCapacityHint,
                minimumMutationCapacity == int.MaxValue ? 0 : minimumMutationCapacity);
        }

        private void EmitCore<TFact>(EntityRef entity, in TFact fact, int parentDepth)
            where TFact : struct, IFact
        {
            var route = _registry.RequireFactRoute<TFact>();
            var factId = route.FactId;
            try
            {
                _facts.Emit(
                    _entities,
                    entity,
                    factId,
                    in fact,
                    route.ResolvePriority(in fact),
                    parentDepth,
                    _partial.CurrentGuardrails);
            }
            catch (InvalidOperationException exception) when (_partial.IsActive)
            {
                throw _partial.CreateReductionException(
                    "fact acceptance guardrail failed",
                    factId,
                    _registry.Describe(factId),
                    entity,
                    parentDepth,
                    _partial.CurrentReducerName,
                    exception);
            }
        }

        internal void CommitTouchedOutputs()
        {
            ClearQueuedCommitActions();
            EnsureTransactionCapacity();
            EnsureCommitOutputMarkCapacity();
            _facts.CopyTouchedEntities(_transactionBuffer, out var touchedCount);

            for (var entityIndex = 0; entityIndex < touchedCount; entityIndex++)
            {
                var entity = _transactionBuffer[entityIndex];
                if (_entities.IsDestroyed(entity))
                {
                    continue;
                }

                QueueAffectedOutputCommits(entity);
            }

            for (var i = 0; i < _registry.Outputs.Count; i++)
            {
                _registry.Outputs[i].ApplyQueuedCommitActions();
            }

            ClearQueuedCommitActions();
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

                var bucket = output.CreateStateBucket();
                _stateBuckets.Add(output.StateId, bucket);
                output.BindStateBucket(this, bucket);
            }
        }

        private void UnbindRegisteredStateBuckets()
        {
            for (var i = 0; i < _registry.Outputs.Count; i++)
            {
                _registry.Outputs[i].UnbindStateBucket(this);
            }
        }

        internal int MutationCountCore => _mutationCount;

        internal void ClearMutations()
        {
            for (var i = 0; i < _registry.Outputs.Count; i++)
            {
                _registry.Outputs[i].ClearMutations(this);
            }

            _mutationCount = 0;
        }

        internal void RefreshMutationCount()
        {
            var count = 0;
            for (var i = 0; i < _registry.Outputs.Count; i++)
            {
                count += _registry.Outputs[i].MutationCount(this);
            }

            _mutationCount = count;
        }

        internal void ClearQueuedCommitActions()
        {
            for (var i = 0; i < _registry.Outputs.Count; i++)
            {
                _registry.Outputs[i].ClearQueuedCommitActions();
            }
        }

        private int MinimumCommitActionCapacity()
        {
            var minimum = int.MaxValue;
            for (var i = 0; i < _registry.Outputs.Count; i++)
            {
                var capacity = _registry.Outputs[i].CommitActionCapacity;
                if (capacity < minimum)
                {
                    minimum = capacity;
                }
            }

            return minimum == int.MaxValue ? 0 : minimum;
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

        internal void EnsureTransactionCapacity()
        {
            var required = Math.Max(_entities.Count, 1);
            _transactionBuffer.EnsureCapacity(required);
        }

        internal void EnsureBatchCapacity(int required)
        {
            _batchBuffer.EnsureCapacity(required);
        }

        private void EnsureCommitOutputMarkCapacity()
        {
            var required = NormalizeCapacity(_registry.Outputs.Count);
            if (_commitOutputMarks.Length >= required)
            {
                return;
            }

            Array.Resize(ref _commitOutputMarks, required);
        }

        private void QueueAffectedOutputCommits(EntityRef entity)
        {
            var factIds = _facts.FactIds(entity);
            var mark = NextCommitOutputMark();

            for (var factIndex = 0; factIndex < factIds.Length; factIndex++)
            {
                if (!_registry.TryGetAffectedOutputs(factIds[factIndex], out var outputs))
                {
                    continue;
                }

                for (var outputIndex = 0; outputIndex < outputs.Count; outputIndex++)
                {
                    var output = outputs[outputIndex];
                    if (_commitOutputMarks[output.Index] == mark)
                    {
                        continue;
                    }

                    _commitOutputMarks[output.Index] = mark;
                    output.QueueCommitAction(this, entity);
                }
            }
        }

        private int NextCommitOutputMark()
        {
            _commitOutputMark++;
            if (_commitOutputMark != int.MaxValue)
            {
                return _commitOutputMark;
            }

            Array.Clear(_commitOutputMarks, 0, _commitOutputMarks.Length);
            _commitOutputMark = 1;
            return _commitOutputMark;
        }

        private static void ThrowIfOptionsInvalid(ReduceOptions options)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.Guardrails == null)
            {
                throw new ArgumentNullException(nameof(options.Guardrails));
            }
        }

        private static int NormalizeCapacity(int capacity)
            => Math.Max(capacity, 1);
    }
}
