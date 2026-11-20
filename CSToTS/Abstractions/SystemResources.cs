using System.Reflection;
using System.Text.Json;

namespace CSToTS.Abstractions;

internal static class SystemResources
{
    internal static bool TryConvertCall(MethodInfo method, out string expression)
    {
        if (method.DeclaringType?.FullName == "System.SR"
            && method.IsStatic
            && method.IsSpecialName
            && method.Name.StartsWith("get_", StringComparison.Ordinal)
            && method.ReturnType == typeof(string)
            && method.GetParameters().Length == 0
            && method.Invoke(null, null) is string resourceString)
        {
            expression = JsonSerializer.Serialize(resourceString);
            return true;
        }

        expression = string.Empty;
        return false;
    }
}
