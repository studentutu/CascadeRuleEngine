#nullable enable

using System;
using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Input fact: gameplay requested ammo spend this tick.
    /// </summary>
    public readonly struct AmmoSpendRequestedFact : IFact, IEquatable<AmmoSpendRequestedFact>
    {
        public AmmoSpendRequestedFact(int amount)
        {
            Amount = amount;
        }

        public int Amount { get; }

        public bool Equals(AmmoSpendRequestedFact other)
            => Amount == other.Amount;

        public override bool Equals(object? obj)
            => obj is AmmoSpendRequestedFact other && Equals(other);

        public override int GetHashCode()
            => Amount;

        public void Dispose()
        {
        }
    }
}
