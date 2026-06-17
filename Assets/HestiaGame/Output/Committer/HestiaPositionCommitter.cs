#nullable enable

using CascadeEngineApi;
using UnityEngine;

namespace Hestia
{
    /// <summary>
    /// Selects the highest-priority resolved movement and writes durable position once.
    /// </summary>
    public sealed class HestiaPositionCommitter : IOutputCommitter<HestiaPositionState>
    {
        private const float Epsilon = 0.0001f;

        public CommitDecision<HestiaPositionState> Commit(
            ICommitContext ctx,
            EntityRef entity,
            in Optional<HestiaPositionState> previous)
        {
            if (!FactConflictResolution
                    .TrySelectHighestPriority<MoveResolvedFact, HestiaPositionState, MoveResolvedConflictComparer>(
                    ctx,
                    entity,
                    default,
                    out var winner))
            {
                return CommitDecision<HestiaPositionState>.Unchanged();
            }

            var old = previous.HasValue ? previous.Value : new HestiaPositionState(0f);
            var next = new HestiaPositionState(winner.Position);
            return Mathf.Abs(old.Position - next.Position) < Epsilon
                ? CommitDecision<HestiaPositionState>.Unchanged()
                : CommitDecision<HestiaPositionState>.Set(next);
        }

        private readonly struct MoveResolvedConflictComparer : IFactConflictComparer<MoveResolvedFact>
        {
            public bool Conflicts(in MoveResolvedFact currentWinner, in MoveResolvedFact candidate)
                => Mathf.Abs(candidate.Position - currentWinner.Position) >= Epsilon;
        }
    }
}
