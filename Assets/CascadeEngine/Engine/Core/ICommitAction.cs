#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal delayed durable write produced by a committer decision.
    /// </summary>
    internal interface ICommitAction
    {
        void Apply();
    }
}
