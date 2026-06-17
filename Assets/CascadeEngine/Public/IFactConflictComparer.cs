#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Output-specific equal-priority conflict check for one fact type.
    /// </summary>
    public interface IFactConflictComparer<TFact>
        where TFact : struct, IFact
    {
        bool Conflicts(in TFact currentWinner, in TFact candidate);
    }
}
