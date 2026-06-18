#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Declares the intended commit merge semantics for one output state.
    /// </summary>
    public enum CommitConflictPolicy : byte
    {
        /// <summary>
        /// The commit phase exposes only the highest registration-priority fact type and throws when distinct facts tie for the winning priority.
        /// </summary>
        PriorityWinnerOrThrowOnTie = 0,

        /// <summary>
        /// The committer collapses one or more facts into a single marker-style output.
        /// </summary>
        CollapseToSingleMarker = 1,

        /// <summary>
        /// Legacy name for marker outputs. Do not use fact arrival order as durable truth.
        /// </summary>
        CollapseToLatestMarker = CollapseToSingleMarker,

        /// <summary>
        /// The committer folds all relevant facts into one durable output state.
        /// </summary>
        FoldAll = 2,
    }
}
