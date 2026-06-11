#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Deduplicated property publish queue produced by commit functions.
    /// </summary>
    public sealed class CascadePublishedPropertySet
    {
        private readonly HashSet<long> _keys = new HashSet<long>();
        private readonly List<CascadePublishedPropertyChange> _items = new List<CascadePublishedPropertyChange>();

        public int Count
            => _items.Count;

        public CascadePublishedPropertyChange this[int index]
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
        /// Range: committed entity-property pair. Condition: property should fan out to subscribers. Output: pair is queued once in publish order.
        /// </summary>
        public void Publish(CascadeEntityId entityId, CascadePropertyKey property)
        {
            if (!_keys.Add(CreateKey(entityId, property)))
            {
                return;
            }

            _items.Add(new CascadePublishedPropertyChange(entityId, property));
        }

        /// <summary>
        /// Range: all published property changes. Condition: tick cleanup. Output: publish queue and dedupe keys are cleared.
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
