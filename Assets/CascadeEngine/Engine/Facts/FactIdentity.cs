#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Deduplication key for one tick-local fact payload.
    /// </summary>
    internal readonly struct FactIdentity : IEquatable<FactIdentity>
    {
        private const int HashMultiplier = 397;

        private readonly EntityRef _entity;
        private readonly Type _factType;
        private readonly object _payload;

        internal FactIdentity(EntityRef entity, Type factType, object payload)
        {
            _entity = entity;
            _factType = factType;
            _payload = payload;
        }

        public bool Equals(FactIdentity other)
        {
            return _entity.Equals(other._entity)
                   && _factType == other._factType
                   && EqualityComparer<object>.Default.Equals(_payload, other._payload);
        }

        public override bool Equals(object? obj)
            => obj is FactIdentity other && Equals(other);

        public override int GetHashCode()
        {
            var hash = _entity.GetHashCode();
            hash = (hash * HashMultiplier) ^ _factType.GetHashCode();
            hash = (hash * HashMultiplier) ^ (_payload == null ? 0 : _payload.GetHashCode());
            return hash;
        }
    }
}