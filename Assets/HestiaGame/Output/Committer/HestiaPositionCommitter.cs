#nullable enable

using CascadeEngineApi;
using UnityEngine;

namespace Hestia
{
    /// <summary>
    /// Projects the commit-selected resolved movement into durable position once.
    /// </summary>
    public sealed class HestiaPositionCommitter : IOutputCommitter<HestiaPositionState>
    {
        private const float Epsilon = 0.0001f;

        public CommitDecision<HestiaPositionState> Commit(
            ICommitContext ctx,
            EntityRef entity,
            in Optional<HestiaPositionState> previous)
        {
            if (!ctx.Facts(entity).TryGetLatest<MoveResolvedFact>(out var winner))
            {
                return CommitDecision<HestiaPositionState>.Unchanged();
            }

            var old = previous.HasValue ? previous.Value : new HestiaPositionState(0f);
            var next = new HestiaPositionState(winner.Position);
            return Mathf.Abs(old.Position - next.Position) < Epsilon
                ? CommitDecision<HestiaPositionState>.Unchanged()
                : CommitDecision<HestiaPositionState>.Set(next);
        }

    }
}
