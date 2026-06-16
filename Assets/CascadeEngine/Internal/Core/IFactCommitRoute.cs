#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal commit-facing fact route exposing affected outputs without fact-id map lookups.
    /// </summary>
    internal interface IFactCommitRoute
    {
        int AffectedOutputCount { get; }

        IOutputRegistration AffectedOutputAt(int index);
    }
}
