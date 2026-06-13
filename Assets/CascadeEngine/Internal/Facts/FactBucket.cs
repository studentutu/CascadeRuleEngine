#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed tick-local fact storage for one fact type.
    /// </summary>
    internal sealed class FactBucket<TFact> : IFactBucket
        where TFact : struct, IFact
    {
        private readonly Dictionary<int, EntityFactList<TFact>> _factsByEntity =
            new Dictionary<int, EntityFactList<TFact>>();

        public Type FactType => typeof(TFact);

        internal void Add(EntityRef entity, in TFact fact)
        {
            var key = entity.Value;
            if (!_factsByEntity.TryGetValue(key, out var facts))
            {
                facts = new EntityFactList<TFact>();
                _factsByEntity.Add(key, facts);
            }

            facts.Add(in fact);
        }

        public bool Has(EntityRef entity)
            => _factsByEntity.TryGetValue(entity.Value, out var facts) && facts.Count > 0;

        public int CountFor(EntityRef entity)
            => _factsByEntity.TryGetValue(entity.Value, out var facts) ? facts.Count : 0;

        internal bool TryGetLatest(EntityRef entity, out TFact fact)
        {
            if (_factsByEntity.TryGetValue(entity.Value, out var facts))
            {
                return facts.TryGetLatest(out fact);
            }

            fact = default;
            return false;
        }

        internal ReadOnlySpan<TFact> All(EntityRef entity)
        {
            if (_factsByEntity.TryGetValue(entity.Value, out var facts))
            {
                return facts.AsSpan();
            }

            return ReadOnlySpan<TFact>.Empty;
        }

        public void Clear()
            => _factsByEntity.Clear();
    }
}
