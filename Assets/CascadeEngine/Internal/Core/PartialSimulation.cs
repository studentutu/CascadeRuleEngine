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
        private int _reducerInvocations;
        private int _transactionalInvocations;
        private int _passes;
        private bool _active;

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

        internal SimulationResult RunTick(ReduceOptions options)
        {
            BeginTick(options);

            try
            {
                while (true)
                {
                    var stepStartProcessedFacts = _processedFacts;
                    var startTimestamp = Stopwatch.GetTimestamp();
                    var step = RunReductionPass(options, startTimestamp, stepStartProcessedFacts, out var budgetReason);
                    if (step == ReductionStepStatus.Complete)
                    {
                        return CompleteTick();
                    }

                    if (step == ReductionStepStatus.BudgetExceeded)
                    {
                        return HandleIncomplete(options, budgetReason);
                    }
                }
            }
            catch
            {
                FailActiveTick();
                throw;
            }
        }

        internal bool RunTickIncremental(ReduceOptions options, out SimulationResult result)
        {
            BeginTick(options);

            try
            {
                var stepStartProcessedFacts = _processedFacts;
                var startTimestamp = Stopwatch.GetTimestamp();
                var step = RunReductionPass(options, startTimestamp, stepStartProcessedFacts, out var budgetReason);
                if (step == ReductionStepStatus.Complete)
                {
                    result = CompleteTick();
                    return true;
                }

                result = CreateResult(false, budgetReason);
                return false;
            }
            catch
            {
                FailActiveTick();
                throw;
            }
        }

        internal void DisposePartial()
        {
            _currentCausalDepth = 0;
            ClearDiagnosticContext();
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

        private SimulationResult HandleIncomplete(ReduceOptions options, string reason)
        {
            if (options.IncompleteCommitMode == IncompleteCommitMode.Throw)
            {
                throw CreateReductionException(reason);
            }

            _simulation.ClearMutations();
            var result = CreateResult(false, reason);
            EndActiveTick();
            return result;
        }

        private ReductionStepStatus RunReductionPass(
            ReduceOptions options,
            long startTimestamp,
            int stepStartProcessedFacts,
            out string budgetReason)
        {
            budgetReason = string.Empty;
            _passes++;
            if (_passes > options.MaxPasses)
            {
                throw CreateReductionException("maximum pass count exceeded");
            }

            while (_facts.HasQueuedFacts)
            {
                if (BudgetExceeded(options, startTimestamp, stepStartProcessedFacts, out budgetReason))
                {
                    return ReductionStepStatus.BudgetExceeded;
                }

                _facts.TryPop(options.BudgetMode, out var queued);
                _processedFacts++;
                SetLastFactContext(in queued);

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
                    if (TimeBudgetExceeded(options, startTimestamp, out budgetReason))
                    {
                        return ReductionStepStatus.BudgetExceeded;
                    }

                    _reducerInvocations++;
                    var reducer = reducers[i];
                    SetCurrentFactContext(in queued, reducer.DebugName);
                    if (_reducerInvocations > options.Guardrails.MaxReducerInvocationsPerTick)
                    {
                        throw CreateReductionException("maximum reducer invocation count exceeded");
                    }

                    _currentCausalDepth = queued.Depth + 1;
                    reducer.Reduce(_simulation, in queued);
                    _currentCausalDepth = 0;
                    ClearCurrentFactContext();
                }
            }

            RunReadyTransactionalReducers(options, startTimestamp, out budgetReason);
            if (budgetReason.Length > 0)
            {
                return ReductionStepStatus.BudgetExceeded;
            }

            RunReadyBatchReducers(options, startTimestamp, out budgetReason);
            if (budgetReason.Length > 0)
            {
                return ReductionStepStatus.BudgetExceeded;
            }

            return _facts.HasQueuedFacts
                ? ReductionStepStatus.Incomplete
                : ReductionStepStatus.Complete;
        }

        private bool BudgetExceeded(
            ReduceOptions options,
            long startTimestamp,
            int stepStartProcessedFacts,
            out string reason)
        {
            if (_processedFacts - stepStartProcessedFacts >= options.MaxFacts)
            {
                reason = "maximum fact budget exceeded";
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

                    if (TimeBudgetExceeded(options, startTimestamp, out budgetReason))
                    {
                        return ranAny;
                    }

                    if (!_firedTransactional.MarkIfNew(registration.Index, entity))
                    {
                        continue;
                    }

                    _transactionalInvocations++;
                    SetCurrentTransactionalContext(registration.DebugName, registration.RequiredFactIds, entity);
                    if (_transactionalInvocations > options.Guardrails.MaxTransactionalReducerInvocationsPerTick)
                    {
                        throw CreateReductionException("maximum transactional reducer invocation count exceeded");
                    }

                    _currentCausalDepth = 1;
                    registration.Reduce(_simulation, entity);
                    _currentCausalDepth = 0;
                    ClearCurrentFactContext();
                    ranAny = true;
                }
            }

            return ranAny;
        }

        private bool RunReadyBatchReducers(
            ReduceOptions options,
            long startTimestamp,
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
                if (TimeBudgetExceeded(options, startTimestamp, out budgetReason))
                {
                    return ranAny;
                }

                var batchCount = 0;
                _simulation.EnsureBatchCapacity(touchedCount);

                for (var entityIndex = 0; entityIndex < touchedCount; entityIndex++)
                {
                    var entity = _transactionBuffer[entityIndex];
                    if (_entities.IsDestroyed(entity) || !_facts.HasAll(entity, registration.RequiredFactIds))
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
                SetCurrentTransactionalContext(registration.DebugName, registration.RequiredFactIds, _batchBuffer[0]);
                if (_transactionalInvocations > options.Guardrails.MaxTransactionalReducerInvocationsPerTick)
                {
                    throw CreateReductionException("maximum transactional reducer invocation count exceeded");
                }

                _currentCausalDepth = 1;
                registration.ReduceBatch(_simulation, _batchBuffer.AsSpan(batchCount));
                _currentCausalDepth = 0;
                ClearCurrentFactContext();
                ranAny = true;
            }

            return ranAny;
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
            _reducerInvocations = 0;
            _transactionalInvocations = 0;
            _passes = 0;
            ClearDiagnosticContext();
            _active = true;
        }

        private SimulationResult CompleteTick()
        {
            _simulation.CommitTouchedOutputs();
            var result = CreateResult(true, string.Empty);
            EndActiveTick();
            return result;
        }

        private void EndActiveTick()
        {
            _facts.Clear();
            _currentCausalDepth = 0;
            ClearCurrentFactContext();
            ClearDiagnosticContext();
            _active = false;
        }

        private void FailActiveTick()
        {
            _simulation.ClearQueuedCommitActions();
            _simulation.ClearMutations();
            EndActiveTick();
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
