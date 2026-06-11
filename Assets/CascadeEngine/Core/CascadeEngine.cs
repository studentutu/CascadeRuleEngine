#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Core Cascade tick runner for fact reduction, touched-entity commit, dirty consumers, and cleanup.
    /// </summary>
    public abstract class CascadeEngine<TContext>
        where TContext : CascadeReducerContext
    {
        private readonly CascadeFactBuffer _facts;
        private readonly CascadeReducerMap<TContext> _reducers = new CascadeReducerMap<TContext>();
        private readonly CascadePropertyCommitMap _committers = new CascadePropertyCommitMap();
        private readonly CascadeDirtyConsumerSet _dirtyConsumers;
        private readonly CascadeTouchedEntitySet _touchedEntities;
        private readonly TContext _reducerContext;
        private readonly int _maxReducerRunsPerTick;

        private int _skippedNonRelevant;

        protected CascadeEngine(
            int entityCapacity,
            int factCapacity,
            int maxReducerRunsPerTick,
            Func<CascadeEntityStateStore, CascadeFactBuffer, CascadeTouchedEntitySet, TContext> createReducerContext,
            Action<CascadeReducerMap<TContext>> registerReducers,
            Action<CascadePropertyCommitMap> registerPropertyCommitters,
            int dirtyConsumerCapacity = Bitmask512.BitCount)
        {
            if (entityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityCapacity));
            }

            if (factCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(factCapacity));
            }

            if (maxReducerRunsPerTick <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxReducerRunsPerTick));
            }

            if (dirtyConsumerCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(dirtyConsumerCapacity));
            }

            if (createReducerContext == null)
            {
                throw new ArgumentNullException(nameof(createReducerContext));
            }

            if (registerReducers == null)
            {
                throw new ArgumentNullException(nameof(registerReducers));
            }

            if (registerPropertyCommitters == null)
            {
                throw new ArgumentNullException(nameof(registerPropertyCommitters));
            }

            Entities = new CascadeEntityStateStore(entityCapacity);
            _facts = new CascadeFactBuffer(factCapacity);
            _dirtyConsumers = new CascadeDirtyConsumerSet(entityCapacity, dirtyConsumerCapacity);
            _touchedEntities = new CascadeTouchedEntitySet(entityCapacity);
            _reducerContext = createReducerContext(Entities, _facts, _touchedEntities);
            _maxReducerRunsPerTick = maxReducerRunsPerTick;

            registerReducers(_reducers);
            registerPropertyCommitters(_committers);
        }

        public CascadeTickCounters LastCounters { get; private set; }

        protected CascadeEntityStateStore Entities { get; }

        /// <summary>
        /// Range: dirty work from the last successful tick. Condition: quick aggregate consumer check. Output: true if any entity dirtied the consumer.
        /// </summary>
        public bool IsConsumerDirty(CascadeConsumerKey consumer)
            => _dirtyConsumers.Contains(consumer);

        /// <summary>
        /// Range: dirty work from the last successful tick. Condition: entity-scoped consumer check. Output: true if the exact entity-consumer pair is dirty.
        /// </summary>
        public bool IsConsumerDirty(CascadeEntityId entityId, CascadeConsumerKey consumer)
            => _dirtyConsumers.Contains(entityId, consumer);

        /// <summary>
        /// Count of entities with at least one dirty consumer after the last successful tick.
        /// </summary>
        public int DirtyConsumerEntityCount
            => _dirtyConsumers.EntityCount;

        /// <summary>
        /// Count of exact entity-consumer work items after the last successful tick.
        /// </summary>
        public int DirtyConsumerWorkCount
            => _dirtyConsumers.Count;

        /// <summary>
        /// Range: dirty entity index from the last successful tick. Condition: entity-level consumer scan. Output: entity id with at least one dirty consumer.
        /// </summary>
        public CascadeEntityId GetDirtyConsumerEntityId(int index)
            => _dirtyConsumers.GetEntity(index);

        /// <summary>
        /// Range: dirty entity index from the last successful tick. Condition: consumer needs committed state. Output: committed entity with at least one dirty consumer.
        /// </summary>
        public CascadeEntityState GetDirtyConsumerEntity(int index)
            => Entities.Get(GetDirtyConsumerEntityId(index));

        /// <summary>
        /// Range: dirty work index from the last successful tick. Condition: consumer drain after RunTick. Output: exact entity-consumer refresh item.
        /// </summary>
        public CascadeConsumerWorkItem GetDirtyConsumerWorkItem(int index)
            => _dirtyConsumers.GetWorkItem(index);

        /// <summary>
        /// Range: dirty work index from the last successful tick. Condition: consumer needs committed state. Output: committed entity for that work item.
        /// </summary>
        public CascadeEntityState GetDirtyConsumerWorkEntity(int index)
            => Entities.Get(GetDirtyConsumerWorkItem(index).EntityId);

        /// <summary>
        /// Range: dirty work from the last successful tick. Condition: consumers are drained or intentionally discarded. Output: clears dirty consumer work only.
        /// </summary>
        public void ClearDirtyConsumers()
        {
            _dirtyConsumers.Clear();
        }

        /// <summary>
        /// [INTEGRATION] Range: queued facts this tick. Condition: reducers stage properties or produce facts. Output: committed touched entities and dirty consumers.
        /// </summary>
        public void RunTick()
        {
            _dirtyConsumers.Clear();

            var processedFacts = 0;
            var reducerRuns = 0;

            try
            {
                while (processedFacts < _facts.Count)
                {
                    var fact = _facts[processedFacts];
                    processedFacts++;

                    if (Entities.IsDestroyed(fact.EntityId))
                    {
                        continue;
                    }

                    var reducer = _reducers.GetRequired(fact.Key);

                    if (reducerRuns >= _maxReducerRunsPerTick)
                    {
                        throw new InvalidOperationException(
                            $"Reducer fact cycle detected after '{reducerRuns}' reducer runs. Current fact: '{fact.Key.Name}'.");
                    }

                    _reducerContext.Bind(fact.EntityId);
                    reducer(_reducerContext, fact);
                    reducerRuns++;
                }

                var touchedEntities = _touchedEntities.Count;
                Entities.CommitTouched(_touchedEntities, _committers, _dirtyConsumers);

                LastCounters = new CascadeTickCounters(
                    _facts.Count,
                    processedFacts,
                    reducerRuns,
                    _dirtyConsumers.Count,
                    _skippedNonRelevant,
                    _reducers.Count,
                    touchedEntities);
            }
            catch
            {
                _dirtyConsumers.Clear();
                ClearTickState();
                throw;
            }

            ClearTickState();
        }

        /// <summary>
        /// Range: current tick input queue. Condition: facade receives input/event fact before RunTick. Output: fact is queued for reduction.
        /// </summary>
        protected void AddFact(CascadeFact fact)
        {
            _facts.Add(fact);
        }

        /// <summary>
        /// Range: current tick input queue. Condition: facade receives a fact for an entity. Output: fact is queued; no reducer runs until RunTick.
        /// </summary>
        protected void EnqueueFact(
            CascadeEntityId entityId,
            CascadeFactKey factKey,
            CascadePropertyKey target,
            CascadeValue payload,
            int priority = 0)
        {
            AddFact(new CascadeFact(entityId, factKey, target, payload, priority));
        }

        /// <summary>
        /// Range: current tick input queue. Condition: facade receives typed payload data. Output: payload is wrapped and queued as a fact.
        /// </summary>
        protected void EnqueueFact<T>(
            CascadeEntityId entityId,
            CascadeFactKey factKey,
            CascadePropertyKey target,
            T payload,
            int priority = 0)
        {
            EnqueueFact(entityId, factKey, target, CascadeValue.From(payload), priority);
        }

        /// <summary>
        /// Range: entity inside the store. Condition: facade or reducer owner decides the entity is dead. Output: committed/staged state and flags are cleared.
        /// </summary>
        public void DestroyEntity(CascadeEntityId entityId)
        {
            Entities.Destroy(entityId);
        }

        /// <summary>
        /// Range: current tick instrumentation. Condition: facade rejects an input/event before fact creation. Output: increments skipped non-relevant counter.
        /// </summary>
        protected void SkipNonRelevant()
        {
            _skippedNonRelevant++;
        }

        private void ClearTickState()
        {
            Entities.ClearTouched(_touchedEntities);
            _touchedEntities.Clear();
            _facts.Clear();
            _skippedNonRelevant = 0;
        }
    }
}
