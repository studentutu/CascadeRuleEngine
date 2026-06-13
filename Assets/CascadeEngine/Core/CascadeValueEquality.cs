#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Per-property change policy: returns true when previous and next count as equal (no commit, no mutation).
    /// </summary>
    public delegate bool CascadeValueEquality<T>(T previous, T next);
}