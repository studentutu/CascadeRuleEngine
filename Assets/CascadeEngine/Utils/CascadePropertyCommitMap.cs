#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Explicit property-key to commit-function table.
    /// </summary>
    public sealed class CascadePropertyCommitMap
    {
        private readonly CascadePropertyCommitFunction?[] _committers =
            new CascadePropertyCommitFunction?[Bitmask512.BitCount];
        private Bitmask512 _registeredProperties;

        public int Count { get; private set; }

        public void Register(CascadePropertyKey property, CascadePropertyCommitFunction committer)
        {
            if (committer == null)
            {
                throw new ArgumentNullException(nameof(committer));
            }

            if (!_registeredProperties.Set(property.Index))
            {
                throw new InvalidOperationException($"Committer already registered for property '{property.Name}'.");
            }

            _committers[property.Index] = committer;
            Count++;
        }

        public bool TryGet(CascadePropertyKey property, out CascadePropertyCommitFunction committer)
        {
            if (!_registeredProperties.IsSet(property.Index))
            {
                committer = default!;
                return false;
            }

            committer = _committers[property.Index]!;
            return true;
        }

        public CascadePropertyCommitFunction GetRequired(CascadePropertyKey property)
        {
            if (!TryGet(property, out var committer))
            {
                throw new InvalidOperationException($"No committer registered for property '{property.Name}'.");
            }

            return committer;
        }
    }
}
