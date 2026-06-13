#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// [INTEGRATION] Output adapter methods: api for Hestia output states.
    /// </summary>
    public static class HestiaGameOutputExtensions
    {
        public static void SetInitialAmmo(this HestiaGameContext context, EntityRef entity, int ammo)
            => context.Simulation.SetStateSilently(entity, new HestiaAmmoState(ammo));

        public static void SetInitialPosition(this HestiaGameContext context, EntityRef entity, float position)
            => context.Simulation.SetStateSilently(entity, new HestiaPositionState(position));

        public static HestiaAmmoState GetAmmo(this HestiaGameContext context, EntityRef entity)
            => context.Simulation.State.Get<HestiaAmmoState>(entity);

        public static bool TryGetAmmo(this HestiaGameContext context, EntityRef entity, out HestiaAmmoState ammo)
            => context.Simulation.State.TryGet(entity, out ammo);

        public static HestiaPositionState GetPosition(this HestiaGameContext context, EntityRef entity)
            => context.Simulation.State.Get<HestiaPositionState>(entity);

        public static bool TryGetPosition(
            this HestiaGameContext context,
            EntityRef entity,
            out HestiaPositionState position)
        {
            return context.Simulation.State.TryGet(entity, out position);
        }
    }
}
