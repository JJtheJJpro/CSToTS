using System.Reflection;
using CSToTS.Abstractions;

namespace CSToTS;

internal static partial class MethodExtractor
{
    private static string ResolveCall(MethodBase method, MemberInfo? member, List<string> stack, bool newObject)
    {
        ParameterInfo[] parameters = member switch
        {
            MethodInfo methodMember => methodMember.GetParameters(),
            ConstructorInfo constructorMember => constructorMember.GetParameters(),
            _ => []
        };
        string[] arguments = new string[parameters.Length];
        for (int i = parameters.Length - 1; i >= 0; i--)
        {
            string argument = Pop(stack);
            Type parameterType = parameters[i].ParameterType;
            if (parameterType.IsByRef)
                parameterType = parameterType.GetElementType()!;
            arguments[i] = parameterType == typeof(bool)
                ? argument switch { "0" => "false", "1" => "true", _ => argument }
                : argument;
        }

        if (member is MethodInfo localFunction && GetLocalFunctionName(localFunction) is string localName)
            return $"{localName}({string.Join(", ", arguments)})";

        if (member is MethodInfo methodInfo
            && SystemConsole.TryConvertCall(methodInfo, arguments, null, out string expression))
            return expression;

        if (member is MethodInfo stringMethod
            && SystemString.TryConvertCall(stringMethod, arguments, out string stringExpression))
            return stringExpression;

        if (member is MethodInfo resourceMethod
            && SystemResources.TryConvertCall(resourceMethod, out string resourceExpression))
            return resourceExpression;

        if (member is MethodInfo garbageCollectorMethod
            && SystemGarbageCollector.TryConvertCall(garbageCollectorMethod, arguments, out string garbageCollectorExpression))
            return garbageCollectorExpression;

        if (member is ConstructorInfo constructor)
        {
            if (!newObject)
            {
                if (!constructor.IsStatic)
                    Pop(stack);
                return "";
            }

            if (constructor.DeclaringType?.IsGenericType == true
                && constructor.DeclaringType.GetGenericTypeDefinition() == typeof(List<>))
            {
                ParameterInfo[] constructorParameters = constructor.GetParameters();
                if (arguments.Length == 0
                    || constructorParameters.Length == 1
                        && constructorParameters[0].ParameterType == typeof(int))
                    return "[]";

                if (constructorParameters.Length == 1)
                {
                    Type collectionParameter = constructorParameters[0].ParameterType;
                    if (collectionParameter.IsArray
                        || collectionParameter.IsGenericType
                            && collectionParameter.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                        return $"[...{arguments[0]}]";
                }
            }

            return $"new {GetTypeName(constructor.DeclaringType)}({string.Join(", ", arguments)})";
        }

        if (member is MethodInfo collectionMethod
            && ConvertArrayBackedCollectionCall(collectionMethod, arguments, stack) is string collectionExpression)
            return collectionExpression;

        if (member is MethodInfo specialMethod)
        {
            string rawName = specialMethod.Name;
            if (IsSpanToReadOnlySpanImplicitConversion(specialMethod))
                return arguments[0];
            if (rawName == "FromResult")
                return $"Promise.resolve({string.Join(", ", arguments)})";
            if (rawName is "get_HasValue" or "get_HasValueOrDefault")
            {
                string receiver = Pop(stack);
                return $"{receiver} != null";
            }
            if (rawName == "GetValueOrDefault")
            {
                string receiver = Pop(stack);
                return $"{receiver} ?? 0";
            }
            if (rawName.StartsWith("get_", StringComparison.Ordinal))
            {
                if (specialMethod.GetParameters().Length > 0)
                {
                    if (specialMethod.IsStatic)
                        return $"{GetTypeName(specialMethod.DeclaringType)}.{FixInvalidName(rawName)}({string.Join(", ", arguments)})";
                    string indexedReceiver = Pop(stack);
                    return $"{indexedReceiver}.{FixInvalidName(rawName)}({string.Join(", ", arguments)})";
                }
                if (specialMethod.IsStatic)
                    return $"{GetTypeName(specialMethod.DeclaringType)}.{FixInvalidName(rawName.Substring(4))}";
                string receiver = Pop(stack);
                return $"{receiver}.{FixInvalidName(rawName.Substring(4))}";
            }
            if (rawName == "ToString")
            {
                string receiver = Pop(stack);
                return $"{receiver}.toString()";
            }
            if (specialMethod.DeclaringType == typeof(Task))
                return $"Promise.resolve({string.Join(", ", arguments)})";
        }

        string name = member switch
        {
            MethodBase baseMethod => FixInvalidName(baseMethod.Name),
            MemberInfo namedMember => FixInvalidName(namedMember.Name),
            _ => "call"
        };
        if (member is MethodBase baseMember && !baseMember.IsStatic)
        {
            string receiver = Pop(stack);
            if (member is MethodInfo instanceMethod
                && SystemConsole.TryConvertCall(instanceMethod, arguments, receiver, out string instanceExpression))
                return instanceExpression;
            return $"{receiver}.{name}({string.Join(", ", arguments)})";
        }
        if (member is MethodInfo mi && mi.DeclaringType != null)
            return $"{GetTypeName(mi.DeclaringType)}.{name}({string.Join(", ", arguments)})";
        return $"{name}({string.Join(", ", arguments)})";
    }
}
