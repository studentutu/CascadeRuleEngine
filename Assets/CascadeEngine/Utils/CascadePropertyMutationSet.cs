#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Deduplicated property mutations from the last successful tick.
    /// </summary>
    public sealed class CascadePropertyMutationSet
    {
        private readonly HashSet<long> _keys = new HashSet<long>();
        private readonly List<CascadePropertyMutation> _items = new List<CascadePropertyMutation>();

        /// <summary>
        /// Number of unique entity-property mutations currently recorded.
        /// </summary>
        public int Count
            => _items.Count;

        /// <summary>
        /// Range: mutation index. Condition: caller drains mutation output. Output: mutation item in commit discovery order.
        /// </summary>
        public CascadePropertyMutation this[int index]
        {
            get
            {
                if ((uint)index >= _items.Count)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return _items[index];
            }
        }

        /// <summary>
        /// Range: committed entity-property pair. Condition: property changed this tick. Output: pair is queued once in mutation order.
        /// </summary>
        public void Mark(CascadeEntityId entityId, CascadePropertyKey property)
        {
            if (!_keys.Add(CreateKey(entityId, property)))
            {
                return;
            }

            _items.Add(new CascadePropertyMutation(entityId, property));
        }

        /// <summary>
        /// Range: mutations from the last successful tick. Condition: aggregate property check. Output: true if any entity mutated the property.
        /// </summary>
        public bool Contains(CascadePropertyKey property)
        {
            for (var i = 0; i < _items.Count; i++)
            {
                if (_items[i].Property == property)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Range: mutations from the last successful tick. Condition: exact entity-property check. Output: true if that pair mutated.
        /// </summary>
        public bool Contains(CascadeEntityId entityId, CascadePropertyKey property)
            => _keys.Contains(CreateKey(entityId, property));

        /// <summary>
        /// Range: all mutation output. Condition: beginning of tick or caller consumed output. Output: mutation list and dedupe keys are cleared.
        /// </summary>
        public void Clear()
        {
            _keys.Clear();
            _items.Clear();
        }

        private static long CreateKey(CascadeEntityId entityId, CascadePropertyKey property)
            => ((long)entityId.Value << 32) ^ (uint)property.Index;
    }
}
