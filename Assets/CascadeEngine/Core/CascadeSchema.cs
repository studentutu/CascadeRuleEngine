#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Single declaration point for one engine: typed properties, typed facts with their reducers, and entity flags.
    /// Auto-assigns indices, rejects duplicate names, and seals when bound to an engine.
    /// </summary>
    public sealed class CascadeSchema
    {
        private readonly List<CascadePropertyKey> _properties = new List<CascadePropertyKey>();
        private readonly List<CascadeFactKey> _facts = new List<CascadeFactKey>();
        private readonly HashSet<string> _propertyNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _factNames = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _flagNames = new HashSet<string>(StringComparer.Ordinal);
        private int _flagCount;
        private bool _sealed;

        /// <summary>
        /// Range: positive entity capacity. Condition: bootstrap, before any declarations. Output: empty schema sized for the capacity.
        /// </summary>
        public CascadeSchema(int entityCapacity)
        {
            if (entityCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(entityCapacity));
            }

            EntityCapacity = entityCapacity;
        }

        public int EntityCapacity { get; }
        public int PropertyCount => _properties.Count;
        public int FactCount => _facts.Count;
        public int FlagCount => _flagCount;

        internal IReadOnlyList<CascadePropertyKey> Properties => _properties;
        internal IReadOnlyList<CascadeFactKey> Facts => _facts;

        internal int TotalFactCapacity
        {
            get
            {
                return _facts.Count;
            }
        }

        /// <summary>
        /// Range: unique property name. Condition: declaration phase. Output: typed property committed by exact value equality.
        /// </summary>
        public CascadeProperty<T> AddProperty<T>(string name)
            => AddPropertyCore<T>(name, null, alwaysPublish: false);

        /// <summary>
        /// Range: unique property name. Condition: declaration phase. Output: typed property committed by the custom equality policy (e.g. epsilon).
        /// </summary>
        public CascadeProperty<T> AddProperty<T>(string name, CascadeValueEquality<T> areEqual)
        {
            if (areEqual == null)
            {
                throw new ArgumentNullException(nameof(areEqual));
            }

            return AddPropertyCore<T>(name, areEqual, alwaysPublish: false);
        }

        /// <summary>
        /// Range: unique property name. Condition: declaration phase. Output: marker property that publishes a mutation on every staged write, even when unchanged.
        /// </summary>
        public CascadeProperty<T> AddMarkerProperty<T>(string name)
            => AddPropertyCore<T>(name, null, alwaysPublish: true);

        /// <summary>
        /// Range: unique flag name, up to 512 flags. Condition: declaration phase. Output: flag key with auto-assigned bit index.
        /// </summary>
        public CascadeEntityFlagKey AddFlag(string name)
        {
            ThrowIfSealed();
            ValidateName(_flagNames, name, "flag");
            if (_flagCount >= Bitmask512.BitCount)
            {
                throw new InvalidOperationException($"Flag capacity '{Bitmask512.BitCount}' exceeded.");
            }

            var key = new CascadeEntityFlagKey(_flagCount, name);
            _flagCount++;
            return key;
        }

        /// <summary>
        /// Range: unique fact name, positive capacity. Condition: declaration phase. Output: typed fact kind permanently bound to its single reducer.
        /// </summary>
        public CascadeFact<TPayload> AddFact<TPayload>(string name, CascadeReducer<TPayload> reducer, int factCapacity = 64)
        {
            ThrowIfSealed();
            ValidateName(_factNames, name, "fact");
            if (reducer == null)
            {
                throw new ArgumentNullException(nameof(reducer));
            }

            if (factCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(factCapacity));
            }

            var fact = new CascadeFact<TPayload>(this, _facts.Count, name, factCapacity, reducer);
            _facts.Add(fact);
            return fact;
        }

        /// <summary>
        /// [INTEGRATION] Range: unsealed schema. Condition: engine construction binds the schema. Output: declarations frozen; one schema serves exactly one engine.
        /// </summary>
        internal void Seal()
        {
            if (_sealed)
            {
                throw new InvalidOperationException("Schema is already bound to an engine. Create one schema instance per engine.");
            }

            _sealed = true;
        }

        private CascadeProperty<T> AddPropertyCore<T>(string name, CascadeValueEquality<T>? areEqual, bool alwaysPublish)
        {
            ThrowIfSealed();
            ValidateName(_propertyNames, name, "property");

            var property = new CascadeProperty<T>(this, _properties.Count, name, EntityCapacity, areEqual, alwaysPublish);
            _properties.Add(property);
            return property;
        }

        private void ThrowIfSealed()
        {
            if (_sealed)
            {
                throw new InvalidOperationException("Schema is sealed. Declare all keys before constructing the engine.");
            }
        }

        private static void ValidateName(HashSet<string> names, string name, string category)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException($"Cascade {category} name must be non-empty.", nameof(name));
            }

            if (!names.Add(name))
            {
                throw new InvalidOperationException($"Duplicate cascade {category} name '{name}'.");
            }
        }
    }
}