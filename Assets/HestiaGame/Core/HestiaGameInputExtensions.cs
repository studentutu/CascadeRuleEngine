#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// [INTEGRATION] Input adapter methods: api for Hestia inputs (convert to facts).
    /// </summary>
    public static class HestiaGameInputExtensions
    {
        public static void InputFireWeapon(
            this HestiaGameContext context,
            EntityRef entity,
            int amount = 1,
            FactPriority priority = FactPriority.PlayerVisible)
        {
            context.Simulation.Emit(entity, new AmmoSpendRequestedFact(amount, priority));
        }

        public static void InputMove(
            this HestiaGameContext context,
            EntityRef entity,
            float desiredPosition,
            FactPriority priority = FactPriority.Normal)
        {
            context.Simulation.Emit(entity, new MoveRequestedFact(desiredPosition, priority));
        }

        public static bool InputFootstepCue(
            this HestiaGameContext context,
            EntityRef entity,
            bool isRelevant)
        {
            if (!isRelevant)
            {
                return false;
            }

            context.Simulation.Emit(entity, new FootstepCueFact(FactPriority.Relevant));
            return true;
        }
    }
}
