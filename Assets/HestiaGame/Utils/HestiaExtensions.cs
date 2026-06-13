#nullable enable

namespace Hestia
{
    /// <summary>
    /// Reusable Hestia-domain helpers that keep small value objects consistent.
    /// </summary>
    public static class HestiaExtensions
    {
        private const int HashMultiplier = 397;

        public static int CombineHestiaHash(this int hash, int value)
        {
            return (hash * HashMultiplier) ^ value;
        }
    }
}
