#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Tiny registration-time list that rejects duplicate fact types.
    /// </summary>
    internal sealed class FactTypeList
    {
        private readonly List<Type> _types = new List<Type>();

        internal void Add(Type type)
        {
            for (var i = 0; i < _types.Count; i++)
            {
                if (_types[i] == type)
                {
                    return;
                }
            }

            _types.Add(type);
        }

        internal Type[] ToArray()
            => _types.ToArray();

        internal void Clear()
            => _types.Clear();
    }
}
