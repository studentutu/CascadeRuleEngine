#nullable enable

namespace CascadeEngineApi
{
    /// <summary>
    /// Internal lifecycle handle for a typed fact priority resolver registration.
    /// </summary>
    internal interface IFactPriorityRegistration
    {
        void Bind(FactFeatureRegistry registry);

        void Unbind(FactFeatureRegistry registry);

        void DisposeRegistration();
    }
}
