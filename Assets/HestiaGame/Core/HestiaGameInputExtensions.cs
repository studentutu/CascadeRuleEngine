#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// [INTEGRATION] Input adapter methods: api for Hestia inputs (convert to facts).
    /// </summary>
    public static class HestiaGameInputExtensions
    {
        private const int NormalPriority = 100;
        private const int RelevantPriority = 500;
        private const int PlayerVisiblePriority = 1000;

        public static void InputFireWeapon(
            this HestiaGameContext context,
            EntityRef entity,
            int amount = 1,
            int priority = PlayerVisiblePriority)
        {
            context.Simulation.Emit(entity, new AmmoSpendRequestedFact(amount, priority));
        }

        public static void InputMove(
            this HestiaGameContext context,
            EntityRef entity,
            float desiredPosition,
            int priority = NormalPriority)
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

            context.Simulation.Emit(entity, new FootstepCueFact(RelevantPriority));
            return true;
        }
    }
}
