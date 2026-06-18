#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Reusable fact view bound to one entity.
    /// </summary>
    internal sealed class EntityFactView : IEntityFactView
    {
        private readonly FactStore _facts;
        private readonly FactFeatureRegistry _registry;
        private EntityRef _entity;
        private CascadeTypeId _selectedFact;
        private bool _filterToSelectedFact;

        internal EntityFactView(FactStore facts, FactFeatureRegistry registry)
        {
            _facts = facts;
            _registry = registry;
        }

        internal IEntityFactView Bind(EntityRef entity)
        {
            _entity = entity;
            _selectedFact = default;
            _filterToSelectedFact = false;
            return this;
        }

        internal IEntityFactView Bind(EntityRef entity, CascadeTypeId selectedFact)
        {
            _entity = entity;
            _selectedFact = selectedFact;
            _filterToSelectedFact = true;
            return this;
        }

        public bool Has<TFact>()
            where TFact : struct, IFact
        {
            var factId = _registry.RequireFact<TFact>();
            return IsVisible(factId) && _facts.Has(_entity, factId);
        }

        public bool TryGetLatest<TFact>(out TFact fact)
            where TFact : struct, IFact
        {
            var factId = _registry.RequireFact<TFact>();
            if (IsVisible(factId))
            {
                return _facts.TryGetLatest(_entity, factId, out fact);
            }

            fact = default;
            return false;
        }

        public ReadOnlySpan<TFact> All<TFact>()
            where TFact : struct, IFact
        {
            var factId = _registry.RequireFact<TFact>();
            return IsVisible(factId)
                ? _facts.All<TFact>(_entity, factId)
                : ReadOnlySpan<TFact>.Empty;
        }

        private bool IsVisible(CascadeTypeId factId)
            => !_filterToSelectedFact || factId == _selectedFact;
    }
}
