#nullable enable

using System;
using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Input fact: gameplay requested ammo spend this tick.
    /// </summary>
    public readonly struct AmmoSpendRequestedFact : IFact, IPrioritizedFact, IEquatable<AmmoSpendRequestedFact>
    {
        public AmmoSpendRequestedFact(int amount, FactPriority priority)
        {
            Amount = amount;
            Priority = priority;
        }

        public int Amount { get; }
        public FactPriority Priority { get; }

        public bool Equals(AmmoSpendRequestedFact other)
            => Amount == other.Amount && Priority == other.Priority;

        public override bool Equals(object? obj)
            => obj is AmmoSpendRequestedFact other && Equals(other);

        public override int GetHashCode()
            => Amount.CombineHestiaHash((int)Priority);

        public void Dispose()
        {
        }
    }
}
