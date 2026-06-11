#nullable enable

using CascadeEngineApi;

namespace Hestia
{
    /// <summary>
    /// [INTEGRATION] Consumer port for dirty Hestia Cascade work.
    /// </summary>
    public interface IHestiaGameCascadeConsumer
    {
        void RefreshHudAmmoText(CascadeEntityId entityId, int ammo);
        void RefreshHudAmmoIcon(CascadeEntityId entityId, bool isAmmoEmpty);
        void RefreshCharacterMotor(CascadeEntityId entityId, float position);
        void RefreshReplication(CascadeEntityId entityId, float position);
        void PlayAudioCue(CascadeEntityId entityId);
    }
}
