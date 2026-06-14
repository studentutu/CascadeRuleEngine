#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Generic cache for one fact or output-state type id.
    /// </summary>
    internal static class CascadeTypeIdentity<T>
    {
        private static CascadeTypeId _id;
        private static bool _hasId;

        internal static CascadeTypeId Id
        {
            get
            {
                if (!_hasId)
                {
                    _id = CascadeTypeIdentity.Resolve(typeof(T));
                    _hasId = true;
                }

                return _id;
            }
        }

        internal static readonly string DebugName = CascadeTypeIdentity.DebugName(typeof(T));
    }
}
