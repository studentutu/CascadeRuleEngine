#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Commit behavior when reduction fails to close inside the configured budget.
    /// </summary>
    public enum IncompleteCommitMode
    {
        Throw,
        DoNotCommitAnything
    }
}
