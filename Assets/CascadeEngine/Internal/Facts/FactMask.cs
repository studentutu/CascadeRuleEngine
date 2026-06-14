#nullable enable

using System;

namespace CascadeEngineApi
{
    /// <summary>
    /// Small immutable fact requirement set used by transactional reducers and fact views.
    /// </summary>
    public sealed class FactMask
    {
        public FactMask(params FactType[] factTypes)
        {
            if (factTypes == null)
            {
                throw new ArgumentNullException(nameof(factTypes));
            }

            FactIds = new CascadeTypeId[factTypes.Length];
            for (var i = 0; i < factTypes.Length; i++)
            {
                FactIds[i] = factTypes[i].Id;
            }
        }

        internal CascadeTypeId[] FactIds { get; }

        public static FactMask Create<TA>()
            where TA : struct, IFact
            => new FactMask(FactType.Of<TA>());

        public static FactMask Create<TA, TB>()
            where TA : struct, IFact
            where TB : struct, IFact
            => new FactMask(FactType.Of<TA>(), FactType.Of<TB>());
    }
}
