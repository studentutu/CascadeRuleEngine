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
            _dirtyConsumers = new CascadeDirtyConsumerSet(entityCapacity);
            _touchedEntities = new CascadeTouchedEntitySet(entityCapacity);
            _reducerContext = createReducerContext(Entities, _facts, _touchedEntities);
            _maxReducerRunsPerTick = maxReducerRunsPerTick;

            registerReducers(_reducers);
            registerPropertyCommitters(_committers);
        }

        public CascadeTickCounters LastCounters { get; private set; }

        protected CascadeEntityStateStore Entities { get; }

        public bool IsConsumerDirty(CascadeConsumerKey consumer)
            => _dirtyConsumers.Contains(consumer);

        public bool IsConsumerDirty(CascadeEntityId entityId, CascadeConsumerKey consumer)
            => _dirtyConsumers.Contains(entityId, consumer);

        public int DirtyConsumerEntityCount
            => _dirtyConsumers.EntityCount;

        public CascadeEntityId GetDirtyConsumerEntityId(int index)
            => _dirtyConsumers.GetEntity(index);

        public CascadeEntityState GetDirtyConsumerEntity(int index)
            => Entities.Get(GetDirtyConsumerEntityId(index));

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
                ClearTickState();
                throw;
            }

            ClearTickState();
        }

        protected void AddFact(CascadeFact fact)
        {
            _facts.Add(fact);
        }

        public void DestroyEntity(CascadeEntityId entityId)
        {
            Entities.Destroy(entityId);
        }

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
