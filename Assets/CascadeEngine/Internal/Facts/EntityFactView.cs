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
        private EntityRef _entity;

        internal EntityFactView(FactStore facts)
        {
            _facts = facts;
        }

        internal IEntityFactView Bind(EntityRef entity)
        {
            _entity = entity;
            return this;
        }

        public bool Has<TFact>()
            where TFact : struct, IFact
            => _facts.Has(_entity, typeof(TFact));

        public bool TryGetLatest<TFact>(out TFact fact)
            where TFact : struct, IFact
            => _facts.TryGetLatest(_entity, out fact);

        public ReadOnlySpan<TFact> All<TFact>()
            where TFact : struct, IFact
            => _facts.All<TFact>(_entity);
    }
}
