#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Position of one queued fact: its typed fact kind plus the slot inside that kind's queue.
    /// </summary>
    internal readonly struct CascadeFactRef
    {
        internal CascadeFactRef(CascadeFactKey key, int slot)
        {
            Key = key;
            Slot = slot;
        }

        internal CascadeFactKey Key { get; }
        internal int Slot { get; }
    }
}