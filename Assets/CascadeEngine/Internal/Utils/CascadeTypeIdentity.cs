#nullable enable

using System;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace CascadeEngineApi
{
    /// <summary>
    /// Resolves static CascadeId declarations once per CLR type. Not used as a runtime routing key.
    /// </summary>
    internal static class CascadeTypeIdentity
    {
        private const string CascadeIdMemberName = "CascadeId";
        private const BindingFlags Flags =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy;

        internal static CascadeTypeId Resolve(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            var field = type.GetField(CascadeIdMemberName, Flags);
            if (field != null)
            {
                if (field.FieldType != typeof(CascadeTypeId))
                {
                    throw new InvalidOperationException(
                        $"Type '{type.FullName}' has a CascadeId field, but it is not CascadeTypeId.");
                }

                return CascadeTypeDiagnostics.Register((CascadeTypeId)field.GetValue(null), type);
            }

            var property = type.GetProperty(CascadeIdMemberName, Flags);
            if (property != null)
            {
                if (property.PropertyType != typeof(CascadeTypeId) || property.GetGetMethod(true) == null)
                {
                    throw new InvalidOperationException(
                        $"Type '{type.FullName}' has a CascadeId property, but it is not a readable CascadeTypeId.");
                }

                return CascadeTypeDiagnostics.Register((CascadeTypeId)property.GetValue(null, null), type);
            }

            throw new InvalidOperationException(
                $"Type '{type.FullName}' must declare a static CascadeTypeId CascadeId field or property.");
        }

        internal static string DebugName(Type type)
            => type.Name;

        internal static CascadeTypeId RequireId<T>()
        {
            try
            {
                return CascadeTypeIdentity<T>.Id;
            }
            catch (TypeInitializationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }
    }
}
