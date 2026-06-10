#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Dirty consumer set backed by one bitmask.
    /// </summary>
    public sealed class CascadeDirtyConsumerSet
    {
        private Bitmask512 _mask;

        public bool Contains(CascadeConsumerKey consumer)
            => _mask.IsSet(consumer.Index);

        public void Mark(CascadeConsumerKey consumer)
        {
            _mask.SetDirty(consumer.Index);
        }

        public void Clear()
        {
            _mask.ClearAll();
        }

        public int Count
            => _mask.CountSetBits();
    }
}
