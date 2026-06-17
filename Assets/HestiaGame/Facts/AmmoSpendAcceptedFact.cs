#nullable enable

using System;
using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Derived fact: ammo spend passed reducer validation and can be folded by committers.
    /// </summary>
    public readonly struct AmmoSpendAcceptedFact : IFact, IEquatable<AmmoSpendAcceptedFact>
    {
        public AmmoSpendAcceptedFact(int amount, int priority)
        {
            Amount = amount;
            Priority = priority;
        }

        public int Amount { get; }
        public int Priority { get; }

        public bool Equals(AmmoSpendAcceptedFact other)
            => Amount == other.Amount && Priority == other.Priority;

        public override bool Equals(object? obj)
            => obj is AmmoSpendAcceptedFact other && Equals(other);

        public override int GetHashCode()
            => Amount.CombineHestiaHash(Priority);

        public void Dispose()
        {
        }
    }
}
