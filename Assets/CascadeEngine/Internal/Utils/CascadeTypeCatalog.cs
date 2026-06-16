#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Registration-owned map from CLR type names to compact runtime ids.
    /// </summary>
    internal sealed class CascadeTypeCatalog
    {
        private readonly Dictionary<string, CascadeTypeId> _idsByName =
            new Dictionary<string, CascadeTypeId>(StringComparer.Ordinal);

        private readonly Dictionary<CascadeTypeId, string> _namesById =
            new Dictionary<CascadeTypeId, string>();

        private readonly Dictionary<CascadeTypeId, string> _fullNamesById =
            new Dictionary<CascadeTypeId, string>();

        internal CascadeTypeId Register<T>()
        {
            var type = typeof(T);
            if (type.FullName == null)
            {
                throw new InvalidOperationException($"Cascade type '{type.Name}' must have a full CLR name.");
            }

            return Register(type.Name, type.FullName);
        }

        private CascadeTypeId Register(string name, string fullName)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (fullName == null)
            {
                throw new ArgumentNullException(nameof(fullName));
            }

            var id = CascadeTypeId.FromName(name);

            if (_idsByName.TryGetValue(name, out var existingNameId))
            {
                if (existingNameId != id)
                {
                    throw new InvalidOperationException(
                        $"Cascade type name '{name}' resolved to inconsistent ids '{existingNameId}' and '{id}'.");
                }
            }
            else
            {
                _idsByName.Add(name, id);
            }

            if (_namesById.TryGetValue(id, out var existingName))
            {
                if (!string.Equals(existingName, name, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Cascade type id '{id}' is used by both '{existingName}' and '{name}'. Type names must produce unique ids inside one feature registration.");
                }

                if (_fullNamesById.TryGetValue(id, out var existingFullName)
                    && !string.Equals(existingFullName, fullName, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Cascade type name '{name}' is used by both '{existingFullName}' and '{fullName}'. Type names must be unique inside one feature registration.");
                }

                if (!_fullNamesById.ContainsKey(id))
                {
                    _fullNamesById.Add(id, fullName);
                }

                return id;
            }

            _namesById.Add(id, name);
            _fullNamesById.Add(id, fullName);

            return id;
        }

        internal string NameOf<T>()
            => typeof(T).Name;

        internal string Describe(CascadeTypeId id)
            => _namesById.TryGetValue(id, out var name)
                ? name
                : id.ToString();

        internal void AbsorbFrom(CascadeTypeCatalog other)
        {
            if (other == null)
            {
                throw new ArgumentNullException(nameof(other));
            }

            foreach (var pair in other._namesById)
            {
                Register(pair.Value, other._fullNamesById[pair.Key]);
            }
        }

        internal void Clear()
        {
            _idsByName.Clear();
            _namesById.Clear();
            _fullNamesById.Clear();
        }
    }
}
