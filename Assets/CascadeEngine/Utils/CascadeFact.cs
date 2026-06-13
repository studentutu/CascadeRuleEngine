#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed fact kind: preallocated payload queue plus the single reducer bound at schema declaration.
    /// </summary>
    public sealed class CascadeFact<TPayload> : CascadeFactKey
    {
        private readonly CascadeReducer<TPayload> _reducer;

        // TODO: this is backwards, entity stores his own facts, we just don't have ability to store multi-generic payloads without boxing or performance hit (unwrap)
        // - move to fact with payload to entity, reducer should call .unwrap<T>() to get the underlying data from a child of TPayload.
        // - also, we don't need arrays on the fact, all we need is 1 hashset per entity to store dirty facts.
        private readonly CascadeEntityId[] _entities;
        private readonly TPayload[] _payloads;
        private int _count;

        internal CascadeFact(CascadeSchema owner, int index, string name, int capacity, CascadeReducer<TPayload> reducer)
            : base(owner, index, name)
        {
            _reducer = reducer;
            _entities = new CascadeEntityId[capacity];
            _payloads = new TPayload[capacity];
        }

        /// <summary>
        /// Range: queue below capacity. Condition: host input or reducer production. Output: slot index of the queued fact.
        /// </summary>
        internal int Enqueue(CascadeEntityId entityId, TPayload payload)
        {
            if (_count >= _entities.Length)
            {
                throw new InvalidOperationException($"Fact capacity '{_entities.Length}' exceeded for '{Name}'.");
            }

            _entities[_count] = entityId;
            _payloads[_count] = payload;
            return _count++;
        }

        internal override CascadeEntityId GetEntity(int slot)
            => _entities[slot];

        internal override void Dispatch(CascadeReducerContext context, int slot)
            => _reducer(context, _payloads[slot]);

        internal override void ClearQueue()
        {
            for (var i = 0; i < _count; i++)
            {
                _payloads[i] = default!;
            }

            _count = 0;
        }
    }
}