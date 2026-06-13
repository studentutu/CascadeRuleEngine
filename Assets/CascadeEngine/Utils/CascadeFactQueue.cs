#nullable enable

using System;
using System.Collections.Generic;

namespace CascadeEngineApi
{
    /// <summary>
    /// Ordered queue of all facts in one tick (FIFO across kinds). Payloads stay in their typed CascadeFact arrays.
    /// </summary>
    internal sealed class CascadeFactQueue
    {
        private readonly IReadOnlyList<CascadeFactKey> _facts;
        private readonly CascadeFactRef[] _order;
        private int _count;

        /// <summary>
        /// Range: declared facts, total capacity across kinds. Condition: engine bootstrap. Output: empty preallocated queue.
        /// </summary>
        internal CascadeFactQueue(IReadOnlyList<CascadeFactKey> facts, int totalCapacity)
        {
            _facts = facts;
            _order = new CascadeFactRef[Math.Max(totalCapacity, 1)];
        }

        internal int Count
            => _count;

        /// <summary>
        /// Range: queue below total capacity. Condition: host input or reducer production. Output: payload stored in the typed fact, order preserved here.
        /// </summary>
        internal void Enqueue<TPayload>(CascadeFact<TPayload> fact, CascadeEntityId entityId, TPayload payload)
        {
            if (_count >= _order.Length)
            {
                throw new InvalidOperationException($"Total fact queue capacity '{_order.Length}' exceeded.");
            }

            var slot = fact.Enqueue(entityId, payload);
            _order[_count] = new CascadeFactRef(fact, slot);
            _count++;
        }

        /// <summary>
        /// Range: index below Count. Condition: engine fact loop, no side effects. Output: fact kind and slot in arrival order.
        /// </summary>
        internal CascadeFactRef GetRef(int index)
            => _order[index];

        /// <summary>
        /// Range: all queued facts. Condition: tick finished or failed. Output: order and all typed payload queues emptied.
        /// </summary>
        internal void Clear()
        {
            for (var i = 0; i < _facts.Count; i++)
            {
                _facts[i].ClearQueue();
            }

            _count = 0;
        }
    }
}