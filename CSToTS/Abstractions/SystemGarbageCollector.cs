using System.Reflection;

namespace CSToTS.Abstractions;

internal static class SystemGarbageCollector
{
    internal static bool TryConvertCall(MethodInfo method, IReadOnlyList<string> arguments, out string expression)
    {
        if (method.DeclaringType != typeof(global::System.GC))
        {
            expression = string.Empty;
            return false;
        }

        switch (method.Name)
        {
            case nameof(global::System.GC.KeepAlive):
            case nameof(global::System.GC.SuppressFinalize):
            case nameof(global::System.GC.ReRegisterForFinalize):
            case nameof(global::System.GC.Collect):
            case nameof(global::System.GC.WaitForPendingFinalizers):
            case nameof(global::System.GC.AddMemoryPressure):
            case nameof(global::System.GC.RemoveMemoryPressure):
                expression = string.Empty;
                return true;
            case nameof(global::System.GC.AllocateArray):
            case nameof(global::System.GC.AllocateUninitializedArray):
                Type elementType = method.GetGenericArguments()[0];
                expression = $"Array.from({{ length: {arguments[0]} }}, () => {GetDefaultValue(elementType)})";
                return true;
            default:
                expression = $"((): never => {{ throw new Error({System.Text.Json.JsonSerializer.Serialize($"System.GC.{method.Name} has no portable JavaScript equivalent.")}); }})()";
                return true;
        }
    }

    private static string GetDefaultValue(Type type)
    {
        if (type == typeof(bool))
            return "false";
        if (type == typeof(char))
            return "\"\\0\"";
        if (type.IsValueType)
            return "0";
        return "null";
    }
}
