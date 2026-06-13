#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Cascade tick runner over one sealed schema: queue facts, run reducers, commit staged properties, publish typed mutations.
    /// </summary>
    public sealed class CascadeEngine
    {
        private readonly CascadeSchema _schema;
        private readonly IReadOnlyList<CascadePropertyKey> _properties;
        private readonly CascadeEntityStore _entities;
        private readonly CascadeFactQueue _factQueue;
        private readonly CascadeMutationLog _mutations;
        private readonly CascadeTouchedEntitySet _touchedEntities;
        private readonly CascadeReducerContext _context;
        private readonly int _maxReducerRunsPerTick;
        private int _skippedNonRelevant;

        /// <summary>
        /// [INTEGRATION] Range: unsealed schema, positive run budget. Condition: bootstrap. Output: engine bound to the schema; schema is sealed.
        /// </summary>
        public CascadeEngine(CascadeSchema schema, int maxReducerRunsPerTick = 256)
        {
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            if (maxReducerRunsPerTick <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxReducerRunsPerTick));
            }

            schema.Seal();
            _schema = schema;
            _properties = schema.Properties;
            _entities = new CascadeEntityStore(schema.EntityCapacity);
            _factQueue = new CascadeFactQueue(schema.Facts, schema.TotalFactCapacity);
            _mutations = new CascadeMutationLog(schema.EntityCapacity);
            _touchedEntities = new CascadeTouchedEntitySet(schema.EntityCapacity);
            _context = new CascadeReducerContext(this);
            _maxReducerRunsPerTick = maxReducerRunsPerTick;
        }

        /// <summary>
        /// Counters from the last successful tick.
        /// </summary>
        public CascadeTickCounters LastCounters { get; private set; }

        /// <summary>
        /// Number of published mutations from the last successful tick.
        /// </summary>
        public int MutationCount
            => _mutations.Count;

        /// <summary>
        /// Range: mutation index. Condition: consumer drains untyped output, no side effects. Output: mutation in commit order (property declaration order, then staging order).
        /// </summary>
        public CascadePropertyMutation GetMutation(int index)
            => _mutations[index];

        /// <summary>
        /// Range: last tick output. Condition: aggregate check, no side effects. Output: true when any entity mutated the property.
        /// </summary>
        public bool WasPropertyMutated(CascadePropertyKey property)
        {
            ValidateOwnership(property);
            return property.MutatedCount > 0;
        }

        /// <summary>
        /// Range: last tick output. Condition: exact entity-property check, no side effects. Output: true when that pair mutated.
        /// </summary>
        public bool WasPropertyMutated(CascadeEntityId entityId, CascadePropertyKey property)
        {
            ValidateOwnership(property);
            return property.WasMutated(entityId);
        }

        /// <summary>
        /// [INTEGRATION] Range: last tick output for one property. Condition: consumer routes typed changes; cache the handler delegate to stay allocation-free. Output: handler runs per mutated entity with previous and next values.
        /// </summary>
        public void ForEachMutation<T>(CascadeProperty<T> property, CascadeMutationHandler<T> handler)
        {
            ValidateOwnership(property);
            if (handler == null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            property.ForEachMutation(handler);
        }

        /// <summary>
        /// Range: all mutation output. Condition: consumer finished routing. Output: mutation output cleared before the next tick.
        /// </summary>
        public void ClearMutations()
        {
            _mutations.Clear();
            for (var i = 0; i < _properties.Count; i++)
            {
                _properties[i].ClearMutationOutput();
            }
        }

        /// <summary>
        /// [INTEGRATION] Range: entity in capacity, fact from this schema. Condition: host input or event arrives. Output: typed fact queued for the next tick.
        /// </summary>
        public void EnqueueFact<TPayload>(CascadeEntityId entityId, CascadeFact<TPayload> fact, TPayload payload)
        {
            ValidateOwnership(fact);
            _entities.ValidateEntity(entityId);
            _factQueue.Enqueue(fact, entityId, payload);
        }

        /// <summary>
        /// [INTEGRATION] Range: entity in capacity, signal fact from this schema. Condition: payload-less host input. Output: signal fact queued for the next tick.
        /// </summary>
        public void EnqueueFact(CascadeEntityId entityId, CascadeFact<CascadeSignal> fact)
            => EnqueueFact(entityId, fact, default);

        /// <summary>
        /// [INTEGRATION] Range: queued facts and staged work. Condition: host drives the pipeline once per frame/step. Output: committed state, published mutations, refreshed counters; on failure staged work is discarded and the error surfaces.
        /// </summary>
        public void RunTick()
        {
            ClearMutations();

            var processedFacts = 0;
            var reducerRuns = 0;

            try
            {
                while (processedFacts < _factQueue.Count)
                {
                    var factRef = _factQueue.GetRef(processedFacts);
                    processedFacts++;

                    var entityId = factRef.Key.GetEntity(factRef.Slot);
                    if (_entities.IsDestroyed(entityId))
                    {
                        continue;
                    }

                    if (reducerRuns >= _maxReducerRunsPerTick)
                    {
                        throw new InvalidOperationException(
                            $"Reducer fact cycle detected after '{reducerRuns}' reducer runs. Current fact: '{factRef.Key.Name}'.");
                    }

                    _context.Bind(entityId);
                    factRef.Key.Dispatch(_context, factRef.Slot);
                    reducerRuns++;
                }

                var touchedEntities = _touchedEntities.Count;
                CommitStagedProperties();

                LastCounters = new CascadeTickCounters(
                    _factQueue.Count,
                    processedFacts,
                    reducerRuns,
                    _mutations.Count,
                    _skippedNonRelevant,
                    _schema.FactCount,
                    touchedEntities);
            }
            catch
            {
                ClearMutations();
                AbortStagedProperties();
                ClearTickState();
                throw;
            }

            ClearTickState();
        }

        /// <summary>
        /// Range: any host-side skip decision. Condition: work is projection-only and the entity is not relevant. Output: skip counted for the current tick.
        /// </summary>
        public void SkipNonRelevant()
            => _skippedNonRelevant++;

        /// <summary>
        /// Range: entity in capacity. Condition: entity lifetime ends. Output: entity destroyed; flags and committed values reset; staged work dropped at commit.
        /// </summary>
        public void DestroyEntity(CascadeEntityId entityId)
        {
            _entities.Destroy(entityId);
            for (var i = 0; i < _properties.Count; i++)
            {
                _properties[i].ClearEntity(entityId);
            }
        }

        /// <summary>
        /// Range: entity in capacity. Condition: query, no side effects. Output: true when the entity is destroyed.
        /// </summary>
        public bool IsDestroyed(CascadeEntityId entityId)
            => _entities.IsDestroyed(entityId);

        /// <summary>
        /// [INTEGRATION] Range: entity in capacity, property from this schema. Condition: consumer reads published truth, no side effects. Output: committed value or default.
        /// </summary>
        public T ReadCommitted<T>(CascadeEntityId entityId, CascadeProperty<T> property)
        {
            ValidateOwnership(property);
            _entities.ValidateEntity(entityId);
            return property.ReadCommitted(entityId);
        }

        /// <summary>
        /// Range: live entity, property from this schema. Condition: initialization or authoritative external load, never from reducers. Output: committed value set without publishing a mutation.
        /// </summary>
        public void SetCommitted<T>(CascadeEntityId entityId, CascadeProperty<T> property, T value)
        {
            ValidateOwnership(property);
            _entities.ValidateEntity(entityId);
            if (_entities.IsDestroyed(entityId))
            {
                throw new InvalidOperationException("Destroyed entities cannot receive committed state.");
            }

            property.SetCommitted(entityId, value);
        }

        /// <summary>
        /// Range: entity in capacity. Condition: query, no side effects. Output: true when the flag is set.
        /// </summary>
        public bool HasFlag(CascadeEntityId entityId, CascadeEntityFlagKey flag)
            => _entities.HasFlag(entityId, flag);

        /// <summary>
        /// Range: live entity. Condition: host configures entity capabilities. Output: flag set.
        /// </summary>
        public void SetFlag(CascadeEntityId entityId, CascadeEntityFlagKey flag)
            => _entities.SetFlag(entityId, flag);

        /// <summary>
        /// Range: live entity. Condition: host configures entity capabilities. Output: flag cleared.
        /// </summary>
        public void ClearFlag(CascadeEntityId entityId, CascadeEntityFlagKey flag)
            => _entities.ClearFlag(entityId, flag);

        /// <summary>
        /// Range: live entity. Condition: host configures entity capabilities. Output: flag set or cleared by the argument.
        /// </summary>
        public void SetFlag(CascadeEntityId entityId, CascadeEntityFlagKey flag, bool enabled)
        {
            if (enabled)
            {
                _entities.SetFlag(entityId, flag);
            }
            else
            {
                _entities.ClearFlag(entityId, flag);
            }
        }

        internal void MarkTouched(CascadeEntityId entityId)
            => _touchedEntities.Mark(entityId);

        internal void ValidateEntity(CascadeEntityId entityId)
            => _entities.ValidateEntity(entityId);

        internal void ValidateOwnership(CascadePropertyKey property)
        {
            if (property == null)
            {
                throw new ArgumentNullException(nameof(property));
            }

            if (!ReferenceEquals(property.Owner, _schema))
            {
                throw new InvalidOperationException($"Property '{property.Name}' belongs to a different schema.");
            }
        }

        internal void ValidateOwnership(CascadeFactKey fact)
        {
            if (fact == null)
            {
                throw new ArgumentNullException(nameof(fact));
            }

            if (!ReferenceEquals(fact.Owner, _schema))
            {
                throw new InvalidOperationException($"Fact '{fact.Name}' belongs to a different schema.");
            }
        }

        private void CommitStagedProperties()
        {
            for (var i = 0; i < _properties.Count; i++)
            {
                _properties[i].CommitStaged(_entities, _mutations);
            }
        }

        private void AbortStagedProperties()
        {
            for (var i = 0; i < _properties.Count; i++)
            {
                _properties[i].AbortStaged();
            }
        }

        private void ClearTickState()
        {
            _factQueue.Clear();
            _touchedEntities.Clear();
            _skippedNonRelevant = 0;
        }
    }
}