#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Diagnostic-only map from stable type ids back to CLR types for exceptions and tooling.
    /// </summary>
    public static class CascadeTypeDiagnostics
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<CascadeTypeId, Type> TypesById = new Dictionary<CascadeTypeId, Type>();

        public static bool TryGetType(CascadeTypeId id, out Type type)
        {
            lock (Sync)
            {
                if (TypesById.TryGetValue(id, out type))
                {
                    return true;
                }
            }

            type = null!;
            return false;
        }

        internal static CascadeTypeId Register(CascadeTypeId id, Type type)
        {
            if (id.IsEmpty)
            {
                throw new InvalidOperationException($"Type '{type.FullName}' declares an empty CascadeId.");
            }

            lock (Sync)
            {
                if (TypesById.TryGetValue(id, out var existing))
                {
                    if (existing == type)
                    {
                        return id;
                    }

                    throw new InvalidOperationException(
                        $"Cascade type id '{id}' is used by both '{existing.FullName}' and '{type.FullName}'.");
                }

                TypesById.Add(id, type);
            }

            return id;
        }

        internal static string Describe(CascadeTypeId id)
        {
            return TryGetType(id, out var type)
                ? type.Name
                : id.ToString();
        }
    }
}
