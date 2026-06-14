#nullable enable

using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Tiny registration-time list that rejects duplicate fact ids.
    /// </summary>
    internal sealed class FactTypeList
    {
        private readonly List<FactType> _types = new List<FactType>();

        internal void Add(FactType type)
        {
            for (var i = 0; i < _types.Count; i++)
            {
                if (_types[i].Id == type.Id)
                {
                    if (!_types[i].CanCreateBucket && type.CanCreateBucket)
                    {
                        _types[i] = type;
                    }

                    return;
                }
            }

            _types.Add(type);
        }

        internal FactType[] ToArray()
            => _types.ToArray();

        internal void Clear()
            => _types.Clear();
    }
}
