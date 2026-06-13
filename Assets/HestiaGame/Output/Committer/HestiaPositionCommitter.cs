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
            var moves = ctx.Facts(entity).All<MoveResolvedFact>();
            if (moves.Length == 0)
            {
                return CommitDecision<HestiaPositionState>.Unchanged();
            }

            var winner = moves[0];
            for (var i = 1; i < moves.Length; i++)
            {
                var candidate = moves[i];
                if (candidate.Priority > winner.Priority)
                {
                    winner = candidate;
                    continue;
                }

                if (candidate.Priority == winner.Priority
                    && Mathf.Abs(candidate.Position - winner.Position) >= Epsilon)
                {
                    throw new CommitConflictException(
                        entity,
                        typeof(HestiaPositionState),
                        "MoveResolvedFact has equal priority with conflicting positions.");
                }
            }

            var old = previous.HasValue ? previous.Value : new HestiaPositionState(0f);
            var next = new HestiaPositionState(winner.Position);
            return Mathf.Abs(old.Position - next.Position) < Epsilon
                ? CommitDecision<HestiaPositionState>.Unchanged()
                : CommitDecision<HestiaPositionState>.Set(next);
        }
    }
}
