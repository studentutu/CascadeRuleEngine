#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Core Cascade tick runner for fact reduction, touched-entity commit, mutation output, and cleanup.
    /// </summary>
    public abstract class CascadeEngine<TContext>
        where TContext : CascadeReducerContext
    {
        private readonly CascadeFactBuffer _facts;
        private readonly CascadeReducerMap<TContext> _reducers = new CascadeReducerMap<TContext>();
        private readonly CascadePropertyCommitMap _committers = new CascadePropertyCommitMap();
        private readonly CascadePropertyMutationSet _mutations = new CascadePropertyMutationSet();
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
            Action<CascadePropertyCommitMap> registerPropertyCommitters)
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
            _touchedEntities = new CascadeTouchedEntitySet(entityCapacity);
            _reducerContext = createReducerContext(Entities, _facts, _touchedEntities);
            _maxReducerRunsPerTick = maxReducerRunsPerTick;

            registerReducers(_reducers);
            registerPropertyCommitters(_committers);
        }

        public CascadeTickCounters LastCounters { get; private set; }

        protected CascadeEntityStateStore Entities { get; }

        /// <summary>
        /// Range: mutations from the last successful tick. Condition: caller needs output size. Output: number of changed entity-property pairs.
        /// </summary>
        public int MutationCount
            => _mutations.Count;

        /// <summary>
        /// Range: mutation index from the last successful tick. Condition: caller drains changed properties. Output: exact entity-property mutation.
        /// </summary>
        public CascadePropertyMutation GetMutation(int index)
            => _mutations[index];

        /// <summary>
        /// Range: mutations from the last successful tick. Condition: aggregate property check. Output: true if any entity mutated the property.
        /// </summary>
        public bool WasPropertyMutated(CascadePropertyKey property)
            => _mutations.Contains(property);

        /// <summary>
        /// Range: mutations from the last successful tick. Condition: exact entity-property check. Output: true if that pair mutated.
        /// </summary>
        public bool WasPropertyMutated(CascadeEntityId entityId, CascadePropertyKey property)
            => _mutations.Contains(entityId, property);

        /// <summary>
        /// Range: mutations from the last successful tick. Condition: caller consumed output. Output: clears mutation output only.
        /// </summary>
        public void ClearMutations()
        {
            _mutations.Clear();
        }

        /// <summary>
        /// [INTEGRATION] Range: queued facts this tick. Condition: reducers stage properties or produce facts. Output: committed state and changed entity-property pairs.
        /// </summary>
        public void RunTick()
        {
            _mutations.Clear();

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
                Entities.CommitTouched(_touchedEntities, _committers, _mutations);

                LastCounters = new CascadeTickCounters(
                    _facts.Count,
                    processedFacts,
                    reducerRuns,
                    _mutations.Count,
                    _skippedNonRelevant,
                    _reducers.Count,
                    touchedEntities);
            }
            catch
            {
                _mutations.Clear();
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
