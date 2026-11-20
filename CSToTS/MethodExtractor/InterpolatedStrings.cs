using System.Reflection;
using System.Text.Json;

namespace CSToTS;

internal static partial class MethodExtractor
{
    private static void HandleInterpolatedStringHandlerCall(
        MethodInfo method,
        List<string> stack,
        Dictionary<string, List<string>> interpolatedStrings)
    {
        ParameterInfo[] parameters = method.GetParameters();
        string[] arguments = new string[parameters.Length];
        for (int i = parameters.Length - 1; i >= 0; i--)
            arguments[i] = Pop(stack);

        string handler = method.IsStatic ? "" : Pop(stack);
        if (method.Name == "AppendLiteral")
        {
            string literal = JsonSerializer.Deserialize<string>(arguments.FirstOrDefault() ?? "\"\"") ?? "";
            AddInterpolatedStringPart(handler, EscapeTemplateLiteral(literal), interpolatedStrings);
        }
        else if (method.Name == "AppendFormatted")
        {
            string value = arguments.FirstOrDefault() ?? "undefined";
            string expression = $"String({value} ?? \"\")";
            if (parameters.Length > 0 && parameters[0].ParameterType == typeof(bool))
                expression = $"({value} ? \"True\" : \"False\")";
            if (parameters.Length > 1 && parameters[1].ParameterType == typeof(int))
            {
                string alignment = arguments[1];
                expression = $"(({alignment}) < 0 ? {expression}.padEnd(-({alignment})) : {expression}.padStart({alignment}))";
            }
            AddInterpolatedStringPart(handler, $"${{{expression}}}", interpolatedStrings);
        }
        else if (method.Name == "ToStringAndClear")
        {
            List<string> parts = interpolatedStrings.Remove(handler, out List<string>? valueParts) ? valueParts : [];
            stack.Add($"`{string.Join("", parts)}`");
        }
    }

    private static string EscapeTemplateLiteral(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("${", "\\${", StringComparison.Ordinal);

    private static void AddInterpolatedStringPart(
        string handler,
        string part,
        Dictionary<string, List<string>> interpolatedStrings)
    {
        if (!interpolatedStrings.TryGetValue(handler, out List<string>? parts))
        {
            parts = [];
            interpolatedStrings.Add(handler, parts);
        }
        parts.Add(part);
    }
}
