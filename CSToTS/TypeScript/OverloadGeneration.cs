using System.Globalization;
using System.Reflection;
using System.Text;

namespace CSToTS;

public static partial class TypeScript
{
    private static string GetOverloadDispatchCondition(MethodInfo method) =>
        GetOverloadDispatchCondition(method.GetParameters());

    private static string GetOverloadDispatchCondition(ParameterInfo[] parameters)
    {
        int paramsIndex = Array.FindIndex(parameters, parameter =>
            parameter.GetCustomAttribute<ParamArrayAttribute>() != null);
        int fixedParameterCount = paramsIndex < 0 ? parameters.Length : paramsIndex;
        int minimumArgumentCount = parameters
            .Take(fixedParameterCount)
            .Count(parameter => !parameter.IsOptional);

        List<string> conditions = [];
        if (paramsIndex >= 0)
        {
            conditions.Add($"__overloadArguments$.length >= {minimumArgumentCount}");
        }
        else if (minimumArgumentCount == parameters.Length)
        {
            conditions.Add($"__overloadArguments$.length === {parameters.Length}");
        }
        else
        {
            conditions.Add($"__overloadArguments$.length >= {minimumArgumentCount}");
            conditions.Add($"__overloadArguments$.length <= {parameters.Length}");
        }

        for (int i = 0; i < fixedParameterCount; i++)
        {
            string expression = $"__overloadArguments$[{i}]";
            string typeCheck = GetOverloadParameterTypeCheck(parameters[i], expression);
            conditions.Add(parameters[i].IsOptional
                ? $"({expression} === undefined || {typeCheck})"
                : typeCheck);
        }

        if (paramsIndex >= 0)
        {
            Type elementType = parameters[paramsIndex].ParameterType.GetElementType()!;
            string typeCheck = GetOverloadTypeCheck(elementType, "__overloadArgument$");
            conditions.Add($"__overloadArguments$.slice({paramsIndex}).every((__overloadArgument$: any) => {typeCheck})");
        }

        return string.Join(" && ", conditions);
    }

    private static string GetOverloadParameterTypeCheck(ParameterInfo parameter, string expression)
    {
        if (parameter.ParameterType.IsByRef)
            return $"typeof {expression} === \"object\" && {expression} !== null";

        Type type = parameter.ParameterType;
        Type? nullableType = Nullable.GetUnderlyingType(type);
        bool permitsNull = nullableType != null;
        if (nullableType != null)
            type = nullableType;
        else if (!type.IsValueType)
            permitsNull = new NullabilityInfoContext().Create(parameter).ReadState == NullabilityState.Nullable;

        string typeCheck = GetOverloadTypeCheck(type, expression);
        return permitsNull ? $"{expression} === null || ({typeCheck})" : typeCheck;
    }

    private static string GetOverloadTypeCheck(Type type, string expression)
    {
        if (type == typeof(object) || type.IsGenericParameter)
            return "true";
        if (type.IsEnum)
            return $"typeof {expression} === \"number\"";
        if (PrimitiveTypes.TryGetValue(type, out string? primitiveType))
        {
            return primitiveType switch
            {
                "boolean" => $"typeof {expression} === \"boolean\"",
                "string" => $"typeof {expression} === \"string\"",
                "number" => $"typeof {expression} === \"number\"",
                "bigint" => $"typeof {expression} === \"bigint\"",
                _ => "true"
            };
        }
        if (type.IsArray)
            return $"Array.isArray({expression})";

        Type genericDefinition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        if (GenericTypeNames.TryGetValue(genericDefinition, out string? mappedType))
        {
            return mappedType switch
            {
                "[]" => $"Array.isArray({expression})",
                "Map" => $"{expression} instanceof Map",
                "Set" => $"{expression} instanceof Set",
                _ => $"typeof {expression} === \"object\" && {expression} !== null"
            };
        }

        if (type.IsClass && type.Assembly != typeof(object).Assembly)
            return $"{expression} instanceof {GetConvertedName(type, true)}";
        return $"typeof {expression} === \"object\" && {expression} !== null";
    }

    private static int GetOverloadSpecificity(MethodInfo method) =>
        GetOverloadSpecificity(method.GetParameters());

    private static int GetOverloadSpecificity(ParameterInfo[] parameters)
    {
        int specificity = 0;
        foreach (ParameterInfo parameter in parameters)
        {
            if (parameter.GetCustomAttribute<ParamArrayAttribute>() != null)
                continue;

            Type type = parameter.ParameterType.IsByRef
                ? parameter.ParameterType.GetElementType()!
                : parameter.ParameterType;
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type == typeof(object) || type.IsGenericParameter)
                continue;
            if (PrimitiveTypes.ContainsKey(type) || type.IsEnum || type.IsArray)
            {
                specificity += 3;
                continue;
            }

            Type genericDefinition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
            if (GenericTypeNames.ContainsKey(genericDefinition))
            {
                specificity += 3;
                continue;
            }

            specificity += type.IsClass ? 2 : 1;
        }
        return specificity;
    }

    private static void AppendOverloadParameterBindings(StringBuilder builder, MethodInfo method, int indent) =>
        AppendOverloadParameterBindings(builder, method.GetParameters(), indent);

    private static void AppendOverloadParameterBindings(StringBuilder builder, ParameterInfo[] parameters, int indent)
    {
        for (int i = 0; i < parameters.Length; i++)
        {
            ParameterInfo parameter = parameters[i];
            string name = FixInvalidName(parameter.Name);
            string type = parameter.ParameterType.IsByRef
                ? "any"
                : GetTypeScriptParameterType(parameter);
            string value = parameter.GetCustomAttribute<ParamArrayAttribute>() != null
                ? $"__overloadArguments$.slice({i})"
                : $"__overloadArguments$[{i}]";

            if (parameter.IsOptional)
                value = $"({value} === undefined ? {GetOptionalParameterDefaultValue(parameter)} : {value})";

            builder.AppendLine($"{Space(0, indent)}let {name}: {type} = {value};");
        }
    }

    private static string GetOptionalParameterDefaultValue(ParameterInfo parameter)
    {
        if (!parameter.HasDefaultValue
            || parameter.DefaultValue == Missing.Value
            || parameter.DefaultValue == DBNull.Value)
            return "undefined";
        if (parameter.DefaultValue == null)
            return "null";
        if (parameter.DefaultValue is string stringValue)
            return System.Text.Json.JsonSerializer.Serialize(stringValue);
        if (parameter.DefaultValue is char character)
            return System.Text.Json.JsonSerializer.Serialize(character.ToString());
        if (parameter.DefaultValue is bool boolean)
            return boolean ? "true" : "false";
        if (parameter.DefaultValue.GetType().IsEnum)
            return Convert.ToInt64(parameter.DefaultValue, CultureInfo.InvariantCulture)
                .ToString(CultureInfo.InvariantCulture);
        return Convert.ToString(parameter.DefaultValue, CultureInfo.InvariantCulture) ?? "undefined";
    }
}
