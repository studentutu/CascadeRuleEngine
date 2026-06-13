#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Registered documentation for how one output expects its committer to reconcile competing facts.
    /// </summary>
    public enum CommitConflictPolicy
    {
        None,
        PriorityWinnerOrThrowOnTie,
        FoldAllInStableFactOrder,
        CollapseToSingleMarker
    }
}
