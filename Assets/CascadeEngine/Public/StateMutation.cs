#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Typed output diff for one entity and one output state.
    /// </summary>
    public readonly struct StateMutation<TState>
        where TState : struct, IOutputState
    {
        internal StateMutation(bool hadPrevious, TState previous, bool hasNext, TState next)
        {
            HadPrevious = hadPrevious;
            Previous = previous;
            HasNext = hasNext;
            Next = next;
        }

        public bool HadPrevious { get; }
        public TState Previous { get; }
        public bool HasNext { get; }
        public TState Next { get; }
    }
}
