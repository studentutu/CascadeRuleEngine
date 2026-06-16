#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Strategy on how output is resolved if committers have competing facts.
    /// </summary>
    public enum CommitConflictPolicy : byte
    {
        PriorityWinnerOrThrowOnTie = 0,
        CollapseToLatestMarker = 1,
    }
}
