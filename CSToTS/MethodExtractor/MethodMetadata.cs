using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;

namespace CSToTS;

internal static partial class MethodExtractor
{
    private static bool TryGetLocalFunctionName(string methodName, out string localFunctionName)
    {
        localFunctionName = "";
        int marker = methodName.IndexOf(">g__", StringComparison.Ordinal);
        int suffix = methodName.LastIndexOf('|');
        if (!methodName.StartsWith('<') || marker <= 1 || suffix <= marker + 4)
            return false;

        localFunctionName = FixInvalidName(methodName[(marker + 4)..suffix]);
        return localFunctionName.Length > 0;
    }

    public static bool IsLocalFunction(MethodInfo method) =>
        TryGetLocalFunctionName(method.Name, out _);

    public static string? GetLocalFunctionName(MethodInfo method) =>
        TryGetLocalFunctionName(method.Name, out string name) ? name : null;

    public static MethodInfo[] GetLocalFunctions(MethodBase method)
    {
        List<MethodInfo> localFunctions = [];
        foreach (IlInstruction instruction in ReadInstructions(method))
        {
            if (instruction.OpCode.Name is not ("call" or "callvirt" or "ldftn" or "ldvirtftn")
                || instruction.Operand is not int token)
                continue;

            if (ResolveMember(method, token) is MethodInfo localFunction
                && IsLocalFunction(localFunction)
                && !localFunctions.Contains(localFunction))
                localFunctions.Add(localFunction);
        }
        return [.. localFunctions];
    }

    public static IEnumerable<Type> GetReferencedTypes(MethodBase method)
    {
        MethodBody? body = method.GetMethodBody();
        if (body == null)
            yield break;

        foreach (LocalVariableInfo local in body.LocalVariables)
            yield return local.LocalType;

        foreach (ExceptionHandlingClause clause in body.ExceptionHandlingClauses)
        {
            if (clause.Flags == ExceptionHandlingClauseOptions.Clause && clause.CatchType != null)
                yield return clause.CatchType;
        }

        foreach (IlInstruction instruction in ReadInstructions(method))
        {
            if (instruction.Operand is not int token)
                continue;

            if (instruction.OpCode.OperandType == OperandType.InlineType)
            {
                Type? referencedType = ResolveType(method, token);
                if (referencedType != null)
                    yield return referencedType;
            }
            else if (instruction.OpCode.OperandType is OperandType.InlineMethod or OperandType.InlineField
                or OperandType.InlineTok)
            {
                MemberInfo? member = ResolveMember(method, token);
                if (member == null)
                    continue;

                if (member.DeclaringType != null)
                    yield return member.DeclaringType;

                switch (member)
                {
                    case MethodInfo referencedMethod:
                        yield return referencedMethod.ReturnType;
                        foreach (ParameterInfo parameter in referencedMethod.GetParameters())
                            yield return parameter.ParameterType;
                        break;
                    case ConstructorInfo constructor:
                        foreach (ParameterInfo parameter in constructor.GetParameters())
                            yield return parameter.ParameterType;
                        break;
                    case FieldInfo field:
                        yield return field.FieldType;
                        break;
                }
            }
        }
    }

    public static IEnumerable<MethodInfo> GetReferencedMethods(MethodBase method)
    {
        foreach (IlInstruction instruction in ReadInstructions(method))
        {
            if (instruction.OpCode.OperandType != OperandType.InlineMethod
                || instruction.Operand is not int token)
                continue;

            if (ResolveMember(method, token) is MethodInfo referencedMethod)
                yield return referencedMethod;
        }
    }
}
