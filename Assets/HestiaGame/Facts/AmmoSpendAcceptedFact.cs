#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Derived fact: ammo spend passed reducer validation and can be folded by committers.
    /// </summary>
    public readonly struct AmmoSpendAcceptedFact : IFact<AmmoSpendAcceptedFact>
    {
        public AmmoSpendAcceptedFact(int amount)
        {
            Amount = amount;
        }

        public int Amount { get; }

        public bool Equals(AmmoSpendAcceptedFact other)
            => Amount == other.Amount;
        public void Dispose() { }
    }
}
