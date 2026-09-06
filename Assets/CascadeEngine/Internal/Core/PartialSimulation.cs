#nullable enable

using System;
using System.Diagnostics;

namespace CascadeEngineApi
{
    /// <summary>
    /// Open tick reduction state. Owns resumable reduction progress and diagnostics until closure or failure.
    /// </summary>
    internal sealed class PartialSimulation
    {
        private readonly FactSimulation _simulation;
        private readonly FactFeatureRegistry _registry;
        private readonly EntityStore _entities;
        private readonly FactStore _facts;
        private readonly EntityRefBuffer _transactionBuffer;
        private readonly EntityRefBuffer _batchBuffer;
        private readonly FiredReducerTracker _firedTransactional;
        private readonly FiredReducerTracker _firedBatchEntities;

        private int _currentCausalDepth;
        private CascadeTypeId _currentFactId;
        private EntityRef _currentEntity;
        private string _currentFactName = string.Empty;
        private string _currentReducerName = string.Empty;
        private CascadeTypeId _lastFactId;
        private EntityRef _lastEntity;
        private int _lastCausalDepth;
        private string _lastFactName = string.Empty;
        private string _lastReducerName = string.Empty;
        private int _processedFacts;
        private int _processedWorkItems;
        private int _reducerInvocations;
        private int _transactionalInvocations;
        private int _passes;
        private QueuedFact _pendingFact;
        private int _pendingFactReducerIndex;
        private int _stateReducerIndex;
        private int _stateEntityIndex;
        private int _negativeQueuedFactCount;
        private int _negativeFactIndex;
        private int _negativeFactReducerIndex;
        private bool _hasPendingFact;
        private bool _stateReducersComplete;
        private bool _negativePhaseStarted;
        private bool _negativeReducersComplete;
        private bool _reducerInvocationActive;
        private bool _active;
        private bool _endingTick;

        internal PartialSimulation(
            FactSimulation simulation,
            FactFeatureRegistry registry,
            EntityStore entities,
            FactStore facts,
            EntityRefBuffer transactionBuffer,
            EntityRefBuffer batchBuffer,
            FiredReducerTracker firedTransactional,
            FiredReducerTracker firedBatchEntities)
        {
            _simulation = simulation;
            _registry = registry;
            _entities = entities;
            _facts = facts;
            _transactionBuffer = transactionBuffer;
            _batchBuffer = batchBuffer;
            _firedTransactional = firedTransactional;
            _firedBatchEntities = firedBatchEntities;
        }

        internal SimulationTick Tick { get; private set; }
        internal FactGuardrails CurrentGuardrails { get; private set; } = new FactGuardrails();
        internal int CurrentCausalDepth => _currentCausalDepth;
        internal string CurrentReducerName => _currentReducerName;
        internal bool IsActive => _active;

        internal void ValidateHostInput()
        {
            if (_endingTick)
            {
                throw new InvalidOperationException("Cannot submit input while tick facts are being disposed.");
            }

            if (_active && _negativePhaseStarted && !_reducerInvocationActive)
            {
                throw new InvalidOperationException(
                    "The open tick input is sealed because closure-safe Without evaluation has started. Resume the tick to closure, then emit the input for the next tick.");
            }
        }

        internal void ValidateFactEmission(
            bool isNegativeConditionInput,
            CascadeTypeId factId)
        {
            if (!_negativePhaseStarted || !isNegativeConditionInput)
            {
                return;
            }

            throw new InvalidOperationException(
                $"Fact '{_registry.Describe(factId)}' is a sealed Without condition and cannot be emitted after positive closure.");
        }

        internal SimulationResult RunTick(ReduceOptions options)
        {
            BeginTick(options);

            try
            {
                while (true)
                {
                    var stepStartProcessedFacts = _processedFacts;
                    var stepStartProcessedWorkItems = _processedWorkItems;
                    var startTimestamp = Stopwatch.GetTimestamp();
                    var step = RunReductionPass(
                        options,
                        startTimestamp,
                        stepStartProcessedFacts,
                        stepStartProcessedWorkItems,
                        out var budgetReason);
                    if (step == ReductionStepStatus.Complete)
                    {
                        return CompleteTick();
                    }

                    if (step == ReductionStepStatus.BudgetExceeded)
                    {
                        throw CreateReductionException(budgetReason);
                    }
                }
            }
            catch (Exception error)
            {
                FailActiveTick(error);
                throw;
            }
        }

        internal bool RunTickIncremental(ReduceOptions options, out SimulationResult result)
        {
            BeginTick(options);

            try
            {
                var stepStartProcessedFacts = _processedFacts;
                var stepStartProcessedWorkItems = _processedWorkItems;
                var startTimestamp = Stopwatch.GetTimestamp();
                var step = RunReductionPass(
                    options,
                    startTimestamp,
                    stepStartProcessedFacts,
                    stepStartProcessedWorkItems,
                    out var budgetReason);
                if (step == ReductionStepStatus.Complete)
                {
                    result = CompleteTick();
                    return true;
                }

                result = CreateResult(false, budgetReason);
                return false;
            }
            catch (Exception error)
            {
                FailActiveTick(error);
                throw;
            }
        }

        internal void DisposePartial()
        {
            _currentCausalDepth = 0;
            ClearPendingFactDispatch();
            ClearDiagnosticContext();
            ResetNegativePhase();
            _reducerInvocationActive = false;
            _active = false;
        }

        internal CascadeReductionException CreateReductionException(
            string budgetReason,
            CascadeTypeId factId,
            string factName,
            EntityRef entity,
            int causalDepth,
            string reducerName,
            Exception? innerException)
        {
            var message =
                $"Cascade reduction failed: {budgetReason}. Tick={Tick.Value}, Entity={entity}, Fact={factName}({factId}), CausalDepth={causalDepth}, Reducer={reducerName}.";
            return new CascadeReductionException(
                message,
                Tick,
                factId,
                factName,
                entity,
                causalDepth,
                reducerName,
                budgetReason,
                innerException);
        }

        private ReductionStepStatus RunReductionPass(
            ReduceOptions options,
            long startTimestamp,
            int stepStartProcessedFacts,
            int stepStartProcessedWorkItems,
            out string budgetReason)
        {
            budgetReason = string.Empty;

            while (_hasPendingFact || _facts.HasQueuedFacts)
            {
                if (!_hasPendingFact)
                {
                    if (BudgetExceeded(
                            options,
                            startTimestamp,
                            stepStartProcessedFacts,
                            stepStartProcessedWorkItems,
                            out budgetReason))
                    {
                        return ReductionStepStatus.BudgetExceeded;
                    }

                    _facts.TryPop(out _pendingFact);
                    _hasPendingFact = true;
                    _pendingFactReducerIndex = 0;
                    _processedFacts++;
                    SetLastFactContext(in _pendingFact);
                }

                if (_entities.IsRetired(_pendingFact.Entity))
                {
                    ClearPendingFactDispatch();
                    continue;
                }

                var reduceRoute = _pendingFact.ReduceRoute;
                while (_pendingFactReducerIndex < reduceRoute.ReducerCount)
                {
                    var reducer = reduceRoute.ReducerAt(_pendingFactReducerIndex);
                    if (reducer.HasForbiddenFacts)
                    {
                        _pendingFactReducerIndex++;
                        continue;
                    }

                    if (WorkOrTimeBudgetExceeded(
                            options,
                            startTimestamp,
                            stepStartProcessedWorkItems,
                            out budgetReason))
                    {
                        return ReductionStepStatus.BudgetExceeded;
                    }

                    _reducerInvocations++;
                    _processedWorkItems++;
                    SetCurrentFactContext(in _pendingFact, reducer.DebugName);
                    ThrowIfTickWorkLimitExceeded(options);
                    if (_reducerInvocations > options.Guardrails.MaxReducerInvocationsPerTick)
                    {
                        throw CreateReductionException("maximum reducer invocation count exceeded");
                    }

                    _currentCausalDepth = _pendingFact.Depth + 1;
                    BeginReducerInvocation();
                    try
                    {
                        reducer.Reduce(_simulation, in _pendingFact);
                    }
                    finally
                    {
                        EndReducerInvocation();
                        _currentCausalDepth = 0;
                        ClearCurrentFactContext();
                    }

                    _pendingFactReducerIndex++;
                }

                ClearPendingFactDispatch();
            }

            RunReadyTransactionalReducers(
                options,
                startTimestamp,
                stepStartProcessedWorkItems,
                out budgetReason);
            if (budgetReason.Length > 0)
            {
                return ReductionStepStatus.BudgetExceeded;
            }

            RunReadyBatchReducers(
                options,
                startTimestamp,
                stepStartProcessedWorkItems,
                out budgetReason);
            if (budgetReason.Length > 0)
            {
                return ReductionStepStatus.BudgetExceeded;
            }

            if (_facts.HasQueuedFacts)
            {
                CompleteReductionPass(options);
                return ReductionStepStatus.Incomplete;
            }

            RunReadyStateReducers(
                options,
                startTimestamp,
                stepStartProcessedWorkItems,
                out budgetReason);
            if (budgetReason.Length > 0)
            {
                return ReductionStepStatus.BudgetExceeded;
            }

            if (_facts.HasQueuedFacts)
            {
                CompleteReductionPass(options);
                return ReductionStepStatus.Incomplete;
            }

            RunReadyNegativeReducers(
                options,
                startTimestamp,
                stepStartProcessedWorkItems,
                out budgetReason);
            if (budgetReason.Length > 0)
            {
                return ReductionStepStatus.BudgetExceeded;
            }

            CompleteReductionPass(options);
            return _facts.HasQueuedFacts
                ? ReductionStepStatus.Incomplete
                : ReductionStepStatus.Complete;
        }

        private bool BudgetExceeded(
            ReduceOptions options,
            long startTimestamp,
            int stepStartProcessedFacts,
            int stepStartProcessedWorkItems,
            out string reason)
        {
            if (_processedFacts - stepStartProcessedFacts >= options.MaxFacts)
            {
                reason = "maximum fact budget exceeded";
                return true;
            }

            return WorkOrTimeBudgetExceeded(
                options,
                startTimestamp,
                stepStartProcessedWorkItems,
                out reason);
        }

        private bool WorkOrTimeBudgetExceeded(
            ReduceOptions options,
            long startTimestamp,
            int stepStartProcessedWorkItems,
            out string reason)
        {
            if (_processedWorkItems - stepStartProcessedWorkItems >= options.MaxWorkItems)
            {
                reason = "maximum work item budget exceeded";
                return true;
            }

            return TimeBudgetExceeded(options, startTimestamp, out reason);
        }

        private bool TimeBudgetExceeded(ReduceOptions options, long startTimestamp, out string reason)
        {
            if (options.MaxMilliseconds > 0 && ElapsedMilliseconds(startTimestamp) > options.MaxMilliseconds)
            {
                reason = "maximum millisecond budget exceeded";
                return true;
            }

            reason = string.Empty;
            return false;
        }

        private bool RunReadyTransactionalReducers(
            ReduceOptions options,
            long startTimestamp,
            int stepStartProcessedWorkItems,
            out string budgetReason)
        {
            budgetReason = string.Empty;
            if (_registry.TransactionalReducers.Count == 0)
            {
                return false;
            }

            _simulation.EnsureTransactionCapacity();
            _facts.CopyTouchedEntities(_transactionBuffer, out var touchedCount);
            var ranAny = false;

            for (var entityIndex = 0; entityIndex < touchedCount; entityIndex++)
            {
                var entity = _transactionBuffer[entityIndex];
                if (_entities.IsRetired(entity))
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

                    if (WorkOrTimeBudgetExceeded(
                            options,
                            startTimestamp,
                            stepStartProcessedWorkItems,
                            out budgetReason))
                    {
                        return ranAny;
                    }

                    if (!_firedTransactional.MarkIfNew(registration.Index, entity))
                    {
                        continue;
                    }

                    _transactionalInvocations++;
                    _processedWorkItems++;
                    SetCurrentTransactionalContext(registration.DebugName, registration.RequiredFactIds, entity);
                    ThrowIfTickWorkLimitExceeded(options);
                    if (_transactionalInvocations > options.Guardrails.MaxTransactionalReducerInvocationsPerTick)
                    {
                        throw CreateReductionException("maximum transactional reducer invocation count exceeded");
                    }

                    _currentCausalDepth = 1;
                    BeginReducerInvocation();
                    try
                    {
                        registration.Reduce(_simulation, entity);
                    }
                    finally
                    {
                        EndReducerInvocation();
                        _currentCausalDepth = 0;
                        ClearCurrentFactContext();
                    }

                    ranAny = true;
                }
            }

            return ranAny;
        }

        private bool RunReadyBatchReducers(
            ReduceOptions options,
            long startTimestamp,
            int stepStartProcessedWorkItems,
            out string budgetReason)
        {
            budgetReason = string.Empty;
            if (_registry.BatchTransactionalReducers.Count == 0)
            {
                return false;
            }

            _simulation.EnsureTransactionCapacity();
            _facts.CopyTouchedEntities(_transactionBuffer, out var touchedCount);
            var ranAny = false;

            for (var reducerIndex = 0; reducerIndex < _registry.BatchTransactionalReducers.Count; reducerIndex++)
            {
                var registration = _registry.BatchTransactionalReducers[reducerIndex];
                if (WorkOrTimeBudgetExceeded(
                        options,
                        startTimestamp,
                        stepStartProcessedWorkItems,
                        out budgetReason))
                {
                    return ranAny;
                }

                var batchCount = 0;
                _simulation.EnsureBatchCapacity(touchedCount);

                for (var entityIndex = 0; entityIndex < touchedCount; entityIndex++)
                {
                    var entity = _transactionBuffer[entityIndex];
                    if (_entities.IsRetired(entity) || !_facts.HasAll(entity, registration.RequiredFactIds))
                    {
                        continue;
                    }

                    if (!_firedBatchEntities.MarkIfNew(registration.Index, entity))
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

                _transactionalInvocations++;
                _processedWorkItems++;
                SetCurrentTransactionalContext(registration.DebugName, registration.RequiredFactIds, _batchBuffer[0]);
                ThrowIfTickWorkLimitExceeded(options);
                if (_transactionalInvocations > options.Guardrails.MaxTransactionalReducerInvocationsPerTick)
                {
                    throw CreateReductionException("maximum transactional reducer invocation count exceeded");
                }

                _currentCausalDepth = 1;
                BeginReducerInvocation();
                try
                {
                    registration.ReduceBatch(_simulation, _batchBuffer.AsSpan(batchCount));
                }
                finally
                {
                    EndReducerInvocation();
                    _currentCausalDepth = 0;
                    ClearCurrentFactContext();
                }

                ranAny = true;
            }

            return ranAny;
        }

        private bool RunReadyStateReducers(
            ReduceOptions options,
            long startTimestamp,
            int stepStartProcessedWorkItems,
            out string budgetReason)
        {
            budgetReason = string.Empty;
            if (_stateReducersComplete)
            {
                return false;
            }

            var ranAny = false;
            while (_stateReducerIndex < _registry.StateReducers.Count)
            {
                var registration = _registry.StateReducers[_stateReducerIndex];
                while (_stateEntityIndex < registration.EntityCount)
                {
                    var entity = registration.EntityAt(_stateEntityIndex);
                    if (_entities.IsRetired(entity))
                    {
                        _stateEntityIndex++;
                        continue;
                    }

                    if (WorkOrTimeBudgetExceeded(
                            options,
                            startTimestamp,
                            stepStartProcessedWorkItems,
                            out budgetReason))
                    {
                        return ranAny;
                    }

                    _stateEntityIndex++;
                    _transactionalInvocations++;
                    _processedWorkItems++;
                    SetCurrentStateContext(registration.DebugName, registration.StateId, entity);
                    ThrowIfTickWorkLimitExceeded(options);
                    if (_transactionalInvocations > options.Guardrails.MaxTransactionalReducerInvocationsPerTick)
                    {
                        throw CreateReductionException("maximum transactional reducer invocation count exceeded");
                    }

                    _currentCausalDepth = 1;
                    BeginReducerInvocation();
                    try
                    {
                        registration.Reduce(_simulation, entity);
                    }
                    finally
                    {
                        EndReducerInvocation();
                        _currentCausalDepth = 0;
                        ClearCurrentFactContext();
                    }

                    ranAny = true;
                }

                _stateReducerIndex++;
                _stateEntityIndex = 0;
            }

            _stateReducersComplete = true;
            return ranAny;
        }

        private void RunReadyNegativeReducers(
            ReduceOptions options,
            long startTimestamp,
            int stepStartProcessedWorkItems,
            out string budgetReason)
        {
            budgetReason = string.Empty;
            if (_negativeReducersComplete)
            {
                return;
            }

            if (!_negativePhaseStarted)
            {
                BeginNegativePhase();
            }

            while (_negativeFactIndex < _negativeQueuedFactCount)
            {
                if (TimeBudgetExceeded(options, startTimestamp, out budgetReason))
                {
                    return;
                }

                var queued = _facts.QueuedFactAt(_negativeFactIndex);
                if (_entities.IsRetired(queued.Entity))
                {
                    AdvanceNegativeFact();
                    continue;
                }

                var reduceRoute = queued.ReduceRoute;
                while (_negativeFactReducerIndex < reduceRoute.ReducerCount)
                {
                    var reducer = reduceRoute.ReducerAt(_negativeFactReducerIndex);
                    if (!reducer.HasForbiddenFacts
                        || _facts.HasAny(queued.Entity, reducer.ForbiddenFactIds))
                    {
                        _negativeFactReducerIndex++;
                        continue;
                    }

                    if (WorkOrTimeBudgetExceeded(
                            options,
                            startTimestamp,
                            stepStartProcessedWorkItems,
                            out budgetReason))
                    {
                        return;
                    }

                    _reducerInvocations++;
                    _processedWorkItems++;
                    SetCurrentFactContext(in queued, reducer.DebugName);
                    ThrowIfTickWorkLimitExceeded(options);
                    if (_reducerInvocations > options.Guardrails.MaxReducerInvocationsPerTick)
                    {
                        throw CreateReductionException(
                            "maximum reducer invocation count exceeded");
                    }

                    _currentCausalDepth = queued.Depth + 1;
                    BeginReducerInvocation();
                    try
                    {
                        reducer.Reduce(_simulation, in queued);
                    }
                    finally
                    {
                        EndReducerInvocation();
                        _currentCausalDepth = 0;
                        ClearCurrentFactContext();
                    }

                    _negativeFactReducerIndex++;
                }

                AdvanceNegativeFact();
            }

            _negativeReducersComplete = true;
        }

        private void BeginNegativePhase()
        {
            _negativeQueuedFactCount = _facts.QueuedFactCount;
            _negativePhaseStarted = true;
        }

        private void AdvanceNegativeFact()
        {
            _negativeFactIndex++;
            _negativeFactReducerIndex = 0;
        }

        private void CompleteReductionPass(ReduceOptions options)
        {
            _passes++;
            if (_passes > options.MaxPasses)
            {
                throw CreateReductionException("maximum pass count exceeded");
            }
        }

        private void ThrowIfTickWorkLimitExceeded(ReduceOptions options)
        {
            if (_processedWorkItems > options.Guardrails.MaxWorkItemsPerTick)
            {
                throw CreateReductionException("maximum tick work item count exceeded");
            }
        }

        private void BeginTick(ReduceOptions options)
        {
            if (_active)
            {
                CurrentGuardrails = options.Guardrails;
                return;
            }

            _simulation.ClearMutations();
            Tick = new SimulationTick(Tick.Value + 1);
            CurrentGuardrails = options.Guardrails;
            _firedTransactional.BeginTick();
            _firedBatchEntities.BeginTick();
            _processedFacts = 0;
            _processedWorkItems = 0;
            _reducerInvocations = 0;
            _transactionalInvocations = 0;
            _passes = 0;
            ClearPendingFactDispatch();
            _stateReducerIndex = 0;
            _stateEntityIndex = 0;
            _stateReducersComplete = _registry.StateReducers.Count == 0;
            ResetNegativePhase();
            _negativeReducersComplete = !_registry.HasNegativeReducers;
            _reducerInvocationActive = false;
            ClearDiagnosticContext();
            _active = true;
        }

        private SimulationResult CompleteTick()
        {
            _simulation.CommitTouchedOutputs();
            _simulation.CommitEntityLifecycle();
            var result = CreateResult(true, string.Empty);
            _simulation.RecordCompletedResult(result);
            EndActiveTick();
            return result;
        }

        private void EndActiveTick()
        {
            _endingTick = true;
            try
            {
                _facts.Clear();
            }
            finally
            {
                DisposePartial();
                _endingTick = false;
            }
        }

        private void FailActiveTick(Exception originalError)
        {
            // Cleanup can fail after publication. A closed tick must retain its durable state and journal.
            if (!_active)
            {
                return;
            }
            _simulation.ClearQueuedCommitActions();
            _simulation.ClearMutations();
            _simulation.RollbackEntityLifecycle();
            try
            {
                EndActiveTick();
            }
            catch (Exception cleanupError)
            {
                var errors = new CleanupErrors();
                errors.Add(originalError);
                errors.Add(cleanupError);
                errors.ThrowIfAny();
            }
        }

        private SimulationResult CreateResult(bool complete, string budgetReason)
        {
            _simulation.RefreshMutationCount();
            var counters = new SimulationResultCounters(
                _facts.AcceptedFacts,
                _processedFacts,
                _facts.DeduplicatedFacts,
                _facts.RejectedDestroyedEntityFacts,
                _reducerInvocations,
                _transactionalInvocations,
                _processedWorkItems,
                _facts.TouchedEntityCount,
                _simulation.MutationCountCore);

            var diagnostics = CreateDiagnostics(budgetReason);
            return new SimulationResult(Tick, complete, counters, diagnostics);
        }

        private SimulationResultDiagnostics CreateDiagnostics(string budgetReason)
        {
            ResolveDiagnosticContext(
                out var factId,
                out var factName,
                out var entity,
                out var causalDepth,
                out var reducerName);

            return new SimulationResultDiagnostics(
                budgetReason,
                factId,
                factName,
                entity,
                causalDepth,
                reducerName);
        }

        private void SetLastFactContext(in QueuedFact queued)
        {
            _lastFactId = queued.FactId;
            _lastFactName = _registry.Describe(queued.FactId);
            _lastEntity = queued.Entity;
            _lastCausalDepth = queued.Depth;
            _lastReducerName = string.Empty;
        }

        private void SetCurrentFactContext(in QueuedFact queued, string reducerName)
        {
            _currentFactId = queued.FactId;
            _currentFactName = _registry.Describe(queued.FactId);
            _currentEntity = queued.Entity;
            _currentCausalDepth = queued.Depth;
            _currentReducerName = reducerName ?? string.Empty;
            _lastFactId = _currentFactId;
            _lastFactName = _currentFactName;
            _lastEntity = _currentEntity;
            _lastCausalDepth = _currentCausalDepth;
            _lastReducerName = _currentReducerName;
        }

        private void SetCurrentTransactionalContext(
            string reducerName,
            CascadeTypeId[] requiredFactIds,
            EntityRef entity)
        {
            var factId = requiredFactIds.Length > 0 ? requiredFactIds[0] : default;
            _currentFactId = factId;
            _currentFactName = factId.IsEmpty ? string.Empty : _registry.Describe(factId);
            _currentEntity = entity;
            _currentCausalDepth = 0;
            _currentReducerName = reducerName ?? string.Empty;
            _lastFactId = _currentFactId;
            _lastFactName = _currentFactName;
            _lastEntity = _currentEntity;
            _lastCausalDepth = _currentCausalDepth;
            _lastReducerName = _currentReducerName;
        }

        private void SetCurrentStateContext(
            string reducerName,
            CascadeTypeId stateId,
            EntityRef entity)
        {
            _currentFactId = stateId;
            _currentFactName = _registry.Describe(stateId);
            _currentEntity = entity;
            _currentCausalDepth = 0;
            _currentReducerName = reducerName ?? string.Empty;
            _lastFactId = _currentFactId;
            _lastFactName = _currentFactName;
            _lastEntity = _currentEntity;
            _lastCausalDepth = _currentCausalDepth;
            _lastReducerName = _currentReducerName;
        }

        private void BeginReducerInvocation()
        {
            if (_reducerInvocationActive)
            {
                throw new InvalidOperationException("Reducer invocations cannot be nested.");
            }

            _reducerInvocationActive = true;
        }

        private void EndReducerInvocation()
            => _reducerInvocationActive = false;

        private void ResetNegativePhase()
        {
            _negativeQueuedFactCount = 0;
            _negativeFactIndex = 0;
            _negativeFactReducerIndex = 0;
            _negativePhaseStarted = false;
            _negativeReducersComplete = false;
        }

        private void ClearPendingFactDispatch()
        {
            _pendingFact = default;
            _pendingFactReducerIndex = 0;
            _hasPendingFact = false;
        }

        private void ClearCurrentFactContext()
        {
            _currentFactId = default;
            _currentFactName = string.Empty;
            _currentEntity = default;
            _currentReducerName = string.Empty;
        }

        private void ClearDiagnosticContext()
        {
            ClearCurrentFactContext();
            _lastFactId = default;
            _lastFactName = string.Empty;
            _lastEntity = default;
            _lastCausalDepth = 0;
            _lastReducerName = string.Empty;
        }

        private void ResolveDiagnosticContext(
            out CascadeTypeId factId,
            out string factName,
            out EntityRef entity,
            out int causalDepth,
            out string reducerName)
        {
            factId = _currentFactId.IsEmpty ? _lastFactId : _currentFactId;
            factName = _currentFactName.Length == 0 ? _lastFactName : _currentFactName;
            entity = _currentFactId.IsEmpty ? _lastEntity : _currentEntity;
            causalDepth = _currentFactId.IsEmpty ? _lastCausalDepth : _currentCausalDepth;
            reducerName = _currentReducerName.Length == 0 ? _lastReducerName : _currentReducerName;
        }

        private CascadeReductionException CreateReductionException(string budgetReason)
            => CreateReductionException(
                budgetReason,
                _currentFactId.IsEmpty ? _lastFactId : _currentFactId,
                _currentFactName.Length == 0 ? _lastFactName : _currentFactName,
                _currentFactId.IsEmpty ? _lastEntity : _currentEntity,
                _currentFactId.IsEmpty ? _lastCausalDepth : _currentCausalDepth,
                _currentReducerName.Length == 0 ? _lastReducerName : _currentReducerName,
                null);

        private static long ElapsedMilliseconds(long startTimestamp)
            => (Stopwatch.GetTimestamp() - startTimestamp) * 1000L / Stopwatch.Frequency;

        private enum ReductionStepStatus
        {
            Incomplete,
            Complete,
            BudgetExceeded
        }
    }
}
