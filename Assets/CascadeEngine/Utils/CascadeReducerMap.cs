#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Explicit fact-kind to reducer-function table.
    /// </summary>
    public sealed class CascadeReducerMap<TContext>
    {
        private readonly CascadeReducerFunction<TContext>?[] _reducers =
            new CascadeReducerFunction<TContext>?[Bitmask512.BitCount];
        private Bitmask512 _registeredKinds;

        public int Count { get; private set; }

        /// <summary>
        /// [INTEGRATION] Range: fact kind index 0-511. Condition: one reducer per fact kind. Output: registered reducer table entry.
        /// </summary>
        public void Register(CascadeFactKey factKey, CascadeReducerFunction<TContext> reducer)
        {
            if (reducer == null)
            {
                throw new ArgumentNullException(nameof(reducer));
            }

            if (!_registeredKinds.Set(factKey.Index))
            {
                throw new InvalidOperationException($"Reducer already registered for fact '{factKey.Name}'.");
            }

            _reducers[factKey.Index] = reducer;
            Count++;
        }

        public bool TryGet(CascadeFactKey factKey, out CascadeReducerFunction<TContext> reducer)
        {
            if (!_registeredKinds.IsSet(factKey.Index))
            {
                reducer = default!;
                return false;
            }

            reducer = _reducers[factKey.Index]!;
            return true;
        }

        public CascadeReducerFunction<TContext> GetRequired(CascadeFactKey factKey)
        {
            if (!TryGet(factKey, out var reducer))
            {
                throw new InvalidOperationException($"No reducer registered for fact '{factKey.Name}'.");
            }

            return reducer;
        }
    }
}
