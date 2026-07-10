#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// Input fact: gameplay requested ammo spend this tick.
    /// </summary>
    public readonly struct AmmoSpendRequestedFact : IFact<AmmoSpendRequestedFact>
    {
        public AmmoSpendRequestedFact(int amount)
        {
            Amount = amount;
        }

        public int Amount { get; }

        public bool Equals(AmmoSpendRequestedFact other)
            => Amount == other.Amount;
    }
}
