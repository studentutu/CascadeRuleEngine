#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Strategy on how output is resolved if committers have competing facts.
    /// </summary>
    public enum CommitConflictPolicy
    {
        None,
        PriorityWinnerOrThrowOnTie,
        CollapseToLatestMarker
    }
}
