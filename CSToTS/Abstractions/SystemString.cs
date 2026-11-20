using System.Reflection;

namespace CSToTS.Abstractions;

internal static class SystemString
{
    internal static bool TryConvertCall(MethodInfo method, IReadOnlyList<string> arguments, out string expression)
    {
        if (method.DeclaringType != typeof(string) || method.Name != nameof(string.Concat))
        {
            expression = string.Empty;
            return false;
        }

        ParameterInfo[] parameters = method.GetParameters();
        if (parameters.Length == 1 && TryGetEnumerableElementType(parameters[0].ParameterType, out Type? elementType))
        {
            string valueExpression = Stringify("__concatItem$", elementType);
            expression = $"Array.from({arguments[0]} == null ? [] : {arguments[0]}, (__concatItem$: any) => {valueExpression}).join(\"\")";
            return true;
        }

        expression = arguments.Count == 0
            ? "\"\""
            : string.Join(" + ", arguments.Select((argument, index) =>
                Stringify(argument, parameters[index].ParameterType)));
        return true;
    }

    private static bool TryGetEnumerableElementType(Type type, out Type elementType)
    {
        if (type.IsArray)
        {
            elementType = type.GetElementType()!;
            return true;
        }

        Type? enumerableType = type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            ? type
            : type.GetInterfaces().FirstOrDefault(candidate =>
                candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        if (enumerableType != null)
        {
            elementType = enumerableType.GetGenericArguments()[0];
            return true;
        }

        elementType = typeof(object);
        return false;
    }

    private static string Stringify(string expression, Type type)
    {
        Type? nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType != null)
            type = nullableType;

        if (type == typeof(bool))
            return $"({expression} == null ? \"\" : ({expression} ? \"True\" : \"False\"))";

        if (type == typeof(object) || type.IsGenericParameter)
        {
            return $"({expression} == null ? \"\" : typeof {expression} === \"boolean\" ? ({expression} ? \"True\" : \"False\") : String({expression}))";
        }

        return $"String({expression} == null ? \"\" : {expression})";
    }
}
