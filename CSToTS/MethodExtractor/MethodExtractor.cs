using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;

namespace CSToTS
{
    internal static class BlobReaderExt
    {
        public static OpCode ReadOpCode(this ref BlobReader reader)
        {
            byte code = reader.ReadByte();
            if (code != 0xFE)
                return SingleByteOpCodes[code];
            return MultiByteOpCodes[reader.ReadByte()];
        }

        private static readonly OpCode[] SingleByteOpCodes = new OpCode[256];
        private static readonly OpCode[] MultiByteOpCodes = new OpCode[256];

        static BlobReaderExt()
        {
            foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
            {
                if (field.GetValue(null) is OpCode opCode)
                {
                    if (opCode.OpCodeType == OpCodeType.Nternal)
                        continue;
                    if (opCode.Size == 1)
                        SingleByteOpCodes[opCode.Value] = opCode;
                    else
                        MultiByteOpCodes[opCode.Value & 0xff] = opCode;
                }
            }
        }
    }

    internal static partial class MethodExtractor
    {
        private sealed record IlInstruction(int Offset, OpCode OpCode, object? Operand);

        public static string FixInvalidName(string? name)
        {
            if (name == null)
                return "_";
            if (name.Length == 0)
                return "_null_";

            string result = name.Replace("<", "__").Replace(">", "__").Replace(".", "_")
                .Replace(" ", "_").Replace("|", "_").Replace(",", "_").Replace("@", "");
            return result switch
            {
                "function" => "func",
                "constructor" => "_constructor",
                "delete" => "_delete",
                "default" => "_default",
                "class" => "_class",
                "interface" => "_interface",
                _ => result
            };
        }

        private static string GetTypeName(Type? type)
        {
            string? name = type?.Name;
            int genericArityStart = name?.IndexOf('`') ?? -1;
            if (genericArityStart >= 0)
                name = name![..genericArityStart];
            return FixInvalidName(name);
        }

        private static bool IsSpanToReadOnlySpanImplicitConversion(MethodInfo method)
        {
            Type? declaringType = method.DeclaringType;
            Type returnType = method.ReturnType;
            ParameterInfo[] parameters = method.GetParameters();
            return method.Name == "op_Implicit"
                && declaringType is { IsGenericType: true }
                && declaringType.GetGenericTypeDefinition() == typeof(Span<>)
                && returnType.IsGenericType
                && returnType.GetGenericTypeDefinition() == typeof(ReadOnlySpan<>)
                && parameters.Length == 1
                && parameters[0].ParameterType == declaringType;
        }

        private static bool IsArrayBackedCollection(Type? type)
        {
            if (type?.IsGenericType != true)
                return false;

            Type genericDefinition = type.GetGenericTypeDefinition();
            return genericDefinition == typeof(List<>)
                || genericDefinition == typeof(IList<>)
                || genericDefinition == typeof(ICollection<>)
                || genericDefinition == typeof(IReadOnlyCollection<>)
                || genericDefinition == typeof(IReadOnlyList<>);
        }

        private static string? ConvertArrayBackedCollectionCall(
            MethodInfo method,
            string[] arguments,
            List<string> stack)
        {
            if (!IsArrayBackedCollection(method.DeclaringType))
                return null;

            switch (method.Name)
            {
                case "get_Count":
                    return $"{Pop(stack)}.length";
                case "get_Item":
                    return $"{Pop(stack)}[{arguments[0]}]";
                case "set_Item":
                {
                    string receiver = Pop(stack);
                    return $"{receiver}[{arguments[0]}] = {arguments[1]}";
                }
                case "Add":
                    return $"{Pop(stack)}.push({arguments[0]})";
                case "AddRange":
                    return $"{Pop(stack)}.push(...{arguments[0]})";
                case "Clear":
                    return $"{Pop(stack)}.length = 0";
                case "Contains":
                    return $"{Pop(stack)}.includes({arguments[0]})";
                case "IndexOf":
                    return $"{Pop(stack)}.indexOf({arguments[0]})";
                case "Insert":
                    return $"{Pop(stack)}.splice({arguments[0]}, 0, {arguments[1]})";
                case "RemoveAt":
                    return $"{Pop(stack)}.splice({arguments[0]}, 1)";
                case "ToArray":
                    return $"[...{Pop(stack)}]";
                default:
                    return null;
            }
        }

        private static string? BranchOperator(string opCodeName)
        {
            string name = opCodeName.EndsWith(".s", StringComparison.Ordinal)
                ? opCodeName[..^2]
                : opCodeName;
            return name switch
            {
                "beq" => "===",
                "bne.un" => "!==",
                "bge" or "bge.un" => ">=",
                "bgt" or "bgt.un" => ">",
                "ble" or "ble.un" => "<=",
                "blt" or "blt.un" => "<",
                _ => null
            };
        }

        private static string ArgumentName(MethodBase method, int index)
        {
            ParameterInfo[] parameters = method.GetParameters();
            if (!method.IsStatic)
            {
                if (index == 0)
                    return "this";
                index--;
            }
            return index >= 0 && index < parameters.Length
                ? FixInvalidName(parameters[index].Name)
                : $"arg{index}";
        }

        private static string FieldName(FieldInfo field)
        {
            if (field.Name.Contains("<") && field.Name.Contains(">"))
            {
                int start = field.Name.IndexOf('<') + 1;
                int end = field.Name.IndexOf('>');
                if (end > start)
                    return FixInvalidName(field.Name[start..end]);
            }
            int underscore = field.Name.LastIndexOf('_');
            if (underscore >= 0 && (field.Name.Contains("IExplicit", StringComparison.Ordinal)
                || field.Name.EndsWith("_Value", StringComparison.Ordinal)))
                return FixInvalidName(field.Name[(underscore + 1)..]);
            int dot = field.Name.LastIndexOf('.');
            if (dot >= 0 && field.Name.Contains("IExplicitPropertyContract", StringComparison.Ordinal))
                return FixInvalidName(field.Name[(dot + 1)..]);
            return FixInvalidName(field.Name);
        }

        private static string Pop(List<string> stack)
        {
            if (stack.Count == 0)
                return "/* missing value */";
            string value = stack[^1];
            stack.RemoveAt(stack.Count - 1);
            return value;
        }

        private static string ConvertConstantForType(string value, Type type) =>
            type == typeof(bool) && value is "0" or "1"
                ? value == "0" ? "false" : "true"
                : value;

        private static bool IsVoidMethod(MemberInfo? member)
        {
            return member is ConstructorInfo || member is MethodInfo mi && mi.ReturnType == typeof(void);
        }

        private static bool IsUnconditionalBranch(IlInstruction instruction) =>
            instruction.OpCode.Name is "br" or "br.s" or "leave" or "leave.s";

        private static bool TryGetBranchTargetIndex(
            IlInstruction[] instructions,
            IReadOnlyDictionary<int, int> instructionIndices,
            int instructionIndex,
            out int targetIndex)
        {
            if (instructions[instructionIndex].Operand is int targetOffset
                && instructionIndices.TryGetValue(targetOffset, out targetIndex))
                return true;

            targetIndex = -1;
            return false;
        }

        private static string BranchCondition(string opCodeName, string left, string right, bool branchTaken)
        {
            if (opCodeName.StartsWith("brtrue", StringComparison.Ordinal))
                return branchTaken ? left : $"!({left})";
            if (opCodeName.StartsWith("brfalse", StringComparison.Ordinal))
                return branchTaken ? $"!({left})" : left;

            string? comparison = BranchOperator(opCodeName);
            if (comparison == null)
                return "false";
            if (branchTaken)
                return $"({left} {comparison} {right})";

            string inverse = comparison switch
            {
                "===" => "!==",
                "!==" => "===",
                ">" => "<=",
                ">=" => "<",
                "<" => ">=",
                "<=" => ">",
                _ => "!=="
            };
            return $"({left} {inverse} {right})";
        }

        private static bool TryEvaluateExpression(
            MethodBase method,
            IlInstruction[] instructions,
            int startIndex,
            int endIndex,
            out string expression)
        {
            List<string> stack = [];
            for (int i = startIndex; i < endIndex; i++)
            {
                IlInstruction instruction = instructions[i];
                string name = instruction.OpCode.Name ?? "";
                object? operand = instruction.Operand;

                if (name is "nop")
                    continue;
                if (name is "ldarg.0" or "ldarg.1" or "ldarg.2" or "ldarg.3" or "ldarg.s" or "ldarg")
                {
                    int index = name switch
                    {
                        "ldarg.0" => 0,
                        "ldarg.1" => 1,
                        "ldarg.2" => 2,
                        "ldarg.3" => 3,
                        _ => Convert.ToInt32(operand)
                    };
                    stack.Add(ArgumentName(method, index));
                }
                else if (name is "ldloc.0" or "ldloc.1" or "ldloc.2" or "ldloc.3" or "ldloc.s" or "ldloc")
                {
                    int index = name switch
                    {
                        "ldloc.0" => 0,
                        "ldloc.1" => 1,
                        "ldloc.2" => 2,
                        "ldloc.3" => 3,
                        _ => Convert.ToInt32(operand)
                    };
                    stack.Add($"loc{index}");
                }
                else if (name.StartsWith("ldc.", StringComparison.Ordinal))
                    stack.Add(IntegerConstant(name, operand));
                else if (name == "ldstr" && operand is int stringToken)
                {
                    try { stack.Add(Literal(method.Module.ResolveString(stringToken))); }
                    catch { return Fail(out expression); }
                }
                else if (name == "ldnull")
                    stack.Add("null");
                else if (name is "add" or "sub" or "mul" or "div" or "rem" or "and" or "or" or "xor" or "shl" or "shr" or "shr.un")
                {
                    if (stack.Count < 2)
                        return Fail(out expression);
                    string right = Pop(stack);
                    string left = Pop(stack);
                    string symbol = name switch
                    {
                        "add" => "+", "sub" => "-", "mul" => "*", "div" => "/", "rem" => "%",
                        "and" => "&", "or" => "|", "xor" => "^", "shl" => "<<", "shr" or "shr.un" => ">>",
                        _ => "+"
                    };
                    stack.Add($"({left} {symbol} {right})");
                }
                else if (name is "ceq" or "cgt" or "cgt.un" or "clt" or "clt.un")
                {
                    if (stack.Count < 2)
                        return Fail(out expression);
                    string right = Pop(stack);
                    string left = Pop(stack);
                    string symbol = name == "ceq" ? "===" : name.StartsWith("cgt", StringComparison.Ordinal) ? ">" : "<";
                    stack.Add($"({left} {symbol} {right})");
                }
                else if (name is "neg" or "not")
                {
                    if (stack.Count == 0)
                        return Fail(out expression);
                    stack.Add(name == "neg" ? $"(-{Pop(stack)})" : $"(!{Pop(stack)})");
                }
                else if (name.StartsWith("conv.", StringComparison.Ordinal))
                {
                    if (stack.Count == 0)
                        return Fail(out expression);
                    stack.Add($"Number({Pop(stack)})");
                }
                else if (name == "dup" && stack.Count > 0)
                    stack.Add(stack[^1]);
                else
                    return Fail(out expression);
            }

            if (stack.Count == 1)
            {
                expression = stack[0];
                return true;
            }
            return Fail(out expression);
        }

        private static bool Fail(out string expression)
        {
            expression = "";
            return false;
        }

        private static bool TryBuildTernaryExpression(
            MethodBase method,
            IlInstruction[] instructions,
            IReadOnlyDictionary<int, int> instructionIndices,
            int branchIndex,
            string branchTakenCondition,
            out string expression,
            out int mergeIndex)
        {
            expression = "";
            mergeIndex = -1;
            if (!TryGetBranchTargetIndex(instructions, instructionIndices, branchIndex, out int targetIndex)
                || targetIndex <= branchIndex + 1)
                return false;

            for (int skipBranchIndex = branchIndex + 1; skipBranchIndex < targetIndex; skipBranchIndex++)
            {
                if (!IsUnconditionalBranch(instructions[skipBranchIndex])
                    || !TryGetBranchTargetIndex(instructions, instructionIndices, skipBranchIndex, out int candidateMerge)
                    || candidateMerge <= targetIndex)
                    continue;

                if (!TryEvaluateExpression(method, instructions, branchIndex + 1, skipBranchIndex, out string fallThroughValue)
                    || !TryEvaluateExpression(method, instructions, targetIndex, candidateMerge, out string targetValue))
                    continue;

                expression = $"({branchTakenCondition} ? {targetValue} : {fallThroughValue})";
                mergeIndex = candidateMerge;
                return true;
            }

            return false;
        }

        private static bool TryGetNullConditionalVoidCall(
            MethodBase method,
            IlInstruction[] instructions,
            IReadOnlyDictionary<int, int> instructionIndices,
            int branchIndex,
            out MethodInfo calledMethod,
            out int callIndex,
            out string? receiverExpression,
            out bool receiverRemainsOnStack)
        {
            calledMethod = null!;
            callIndex = -1;
            receiverExpression = null;
            receiverRemainsOnStack = false;
            if (instructions[branchIndex].OpCode.Name?.StartsWith("brtrue", StringComparison.Ordinal) != true
                || !TryGetBranchTargetIndex(instructions, instructionIndices, branchIndex, out int targetIndex)
                || targetIndex <= branchIndex)
                return false;

            bool duplicatedReceiver = branchIndex > 0
                && instructions[branchIndex - 1].OpCode.Name == "dup"
                && branchIndex + 2 < instructions.Length
                && instructions[branchIndex + 1].OpCode.Name == "pop"
                && instructions[branchIndex + 2].OpCode.Name == "ret"
                && targetIndex + 1 < instructions.Length
                && instructions[targetIndex + 1].OpCode.Name == "ret";
            if (duplicatedReceiver)
            {
                if (!TryGetVoidInstanceCall(method, instructions, targetIndex, out calledMethod))
                    return false;
                callIndex = targetIndex;
                receiverRemainsOnStack = true;
                return true;
            }

            int branchToMergeIndex = branchIndex + 1;
            if (branchToMergeIndex >= instructions.Length
                || !IsUnconditionalBranch(instructions[branchToMergeIndex])
                || !TryGetBranchTargetIndex(instructions, instructionIndices, branchToMergeIndex, out int mergeIndex)
                || mergeIndex <= targetIndex)
                return false;

            for (int candidateCallIndex = targetIndex + 1; candidateCallIndex < mergeIndex; candidateCallIndex++)
            {
                if (instructions[candidateCallIndex].OpCode.Name is not ("call" or "callvirt")
                    || !TryGetVoidInstanceCall(method, instructions, candidateCallIndex, out MethodInfo targetMethod)
                    || !TryEvaluateExpression(method, instructions, targetIndex, candidateCallIndex, out string targetReceiver))
                    continue;

                calledMethod = targetMethod;
                callIndex = candidateCallIndex;
                receiverExpression = targetReceiver;
                return true;
            }

            return false;
        }

        private static bool TryGetVoidInstanceCall(
            MethodBase method,
            IlInstruction[] instructions,
            int callIndex,
            out MethodInfo calledMethod)
        {
            calledMethod = null!;
            if (instructions[callIndex].Operand is not int token
                || ResolveMember(method, token) is not MethodInfo targetMethod
                || targetMethod.IsStatic
                || targetMethod.ReturnType != typeof(void)
                || targetMethod.GetParameters().Length != 0)
                return false;

            calledMethod = targetMethod;
            return true;
        }

        private static bool TryGetIfElseBranches(
            MethodBase method,
            IlInstruction[] instructions,
            IReadOnlyDictionary<int, int> instructionIndices,
            int branchIndex,
            int elseIndex,
            out int branchToEndIndex,
            out int endIndex)
        {
            for (int i = elseIndex - 1; i > branchIndex; i--)
            {
                if (IsUnconditionalBranch(instructions[i])
                    && TryGetBranchTargetIndex(instructions, instructionIndices, i, out endIndex)
                    && endIndex > elseIndex)
                {
                    if (!HasBalancedStack(method, instructions, branchIndex + 1, i)
                        || !HasBalancedStack(method, instructions, elseIndex, endIndex))
                        continue;
                    branchToEndIndex = i;
                    return true;
                }
            }

            branchToEndIndex = -1;
            endIndex = -1;
            return false;
        }

        private static bool TryGetLoopBackBranch(
            MethodBase method,
            IlInstruction[] instructions,
            IReadOnlyDictionary<int, int> instructionIndices,
            int branchIndex,
            int loopExitIndex,
            out int backBranchIndex)
        {
            for (int i = loopExitIndex - 1; i > branchIndex; i--)
            {
                if (IsUnconditionalBranch(instructions[i])
                    && TryGetBranchTargetIndex(instructions, instructionIndices, i, out int targetIndex)
                    && targetIndex <= branchIndex
                    && HasBalancedStack(method, instructions, branchIndex + 1, i))
                {
                    backBranchIndex = i;
                    return true;
                }
            }

            backBranchIndex = -1;
            return false;
        }

        private static bool TryGetPreTestLoop(
            MethodBase method,
            IlInstruction[] instructions,
            IReadOnlyDictionary<int, int> instructionIndices,
            int backBranchIndex,
            int bodyStartIndex,
            out string condition,
            out int conditionStartIndex,
            out int entryBranchIndex)
        {
            condition = "";
            conditionStartIndex = -1;
            entryBranchIndex = -1;

            for (int i = bodyStartIndex - 1; i >= 0; i--)
            {
                if (!IsUnconditionalBranch(instructions[i])
                    || !TryGetBranchTargetIndex(instructions, instructionIndices, i, out int candidateConditionStart)
                    || candidateConditionStart <= bodyStartIndex
                    || candidateConditionStart >= backBranchIndex)
                    continue;

                if (!TryEvaluateLoopCondition(
                    method,
                    instructions,
                    candidateConditionStart,
                    backBranchIndex,
                    out condition)
                    || !HasBalancedStack(method, instructions, bodyStartIndex, candidateConditionStart))
                    continue;

                conditionStartIndex = candidateConditionStart;
                entryBranchIndex = i;
                return true;
            }

            return false;
        }

        private static bool TryEvaluateLoopCondition(
            MethodBase method,
            IlInstruction[] instructions,
            int conditionStartIndex,
            int branchIndex,
            out string condition)
        {
            condition = "";
            IlInstruction branch = instructions[branchIndex];
            string branchName = branch.OpCode.Name ?? "";
            if (!branchName.StartsWith("brtrue", StringComparison.Ordinal)
                && !branchName.StartsWith("brfalse", StringComparison.Ordinal))
                return false;

            for (int storeIndex = branchIndex - 1; storeIndex >= conditionStartIndex; storeIndex--)
            {
                if (!TryGetLocalStoreIndex(instructions[storeIndex], out int localIndex))
                    continue;

                bool loadedCondition = false;
                bool validLoadSequence = true;
                for (int i = storeIndex + 1; i < branchIndex; i++)
                {
                    if ((instructions[i].OpCode.Name ?? "") == "nop")
                        continue;
                    if (!TryGetLocalLoadIndex(instructions[i], out int loadedIndex) || loadedIndex != localIndex)
                    {
                        validLoadSequence = false;
                        break;
                    }
                    loadedCondition = true;
                }
                if (!validLoadSequence || !loadedCondition)
                    continue;

                if (!TryEvaluateExpression(method, instructions, conditionStartIndex, storeIndex, out string value))
                    break;

                condition = BranchCondition(branchName, value, "", branchTaken: true);
                return true;
            }

            if (TryEvaluateExpression(method, instructions, conditionStartIndex, branchIndex, out string directCondition))
            {
                condition = BranchCondition(branchName, directCondition, "", branchTaken: true);
                return true;
            }

            return false;
        }

        private static bool TryGetLocalStoreIndex(IlInstruction instruction, out int index)
        {
            string name = instruction.OpCode.Name ?? "";
            index = name switch
            {
                "stloc.0" => 0,
                "stloc.1" => 1,
                "stloc.2" => 2,
                "stloc.3" => 3,
                "stloc.s" or "stloc" when instruction.Operand is int localIndex => localIndex,
                _ => -1
            };
            return index >= 0;
        }

        private static bool TryGetLocalLoadIndex(IlInstruction instruction, out int index)
        {
            string name = instruction.OpCode.Name ?? "";
            index = name switch
            {
                "ldloc.0" => 0,
                "ldloc.1" => 1,
                "ldloc.2" => 2,
                "ldloc.3" => 3,
                "ldloc.s" or "ldloc" when instruction.Operand is int localIndex => localIndex,
                _ => -1
            };
            return index >= 0;
        }

        private static bool HasBalancedStack(MethodBase method, IlInstruction[] instructions, int startIndex, int endIndex)
        {
            int depth = 0;
            for (int i = startIndex; i < endIndex; i++)
            {
                IlInstruction instruction = instructions[i];
                OpCode opCode = instruction.OpCode;
                string name = opCode.Name ?? "";
                if (name.StartsWith("br", StringComparison.Ordinal)
                    || name.StartsWith("leave", StringComparison.Ordinal)
                    || name is "switch" or "ret" or "throw")
                    return false;

                int popCount;
                int pushCount;
                if (opCode.StackBehaviourPop == StackBehaviour.Varpop
                    || opCode.StackBehaviourPush == StackBehaviour.Varpush)
                {
                    if (instruction.Operand is not int token || ResolveMember(method, token) is not MethodBase calledMethod)
                        return false;

                    ParameterInfo[] parameters = calledMethod.GetParameters();
                    popCount = parameters.Length;
                    if (name != "newobj" && !calledMethod.IsStatic)
                        popCount++;
                    pushCount = name == "newobj"
                        || calledMethod is MethodInfo calledMethodInfo && calledMethodInfo.ReturnType != typeof(void)
                        ? 1
                        : 0;
                }
                else
                {
                    popCount = GetStackBehaviourCount(opCode.StackBehaviourPop);
                    pushCount = GetStackBehaviourCount(opCode.StackBehaviourPush);
                    if (popCount < 0 || pushCount < 0)
                        return false;
                }

                if (depth < popCount)
                    return false;
                depth = depth - popCount + pushCount;
            }

            return depth == 0;
        }

        private static int GetStackBehaviourCount(StackBehaviour behaviour)
        {
            string name = behaviour.ToString();
            if (name == "Pop0" || name == "Push0")
                return 0;
            if (name.StartsWith("Pop", StringComparison.Ordinal))
                return name == "PopAll" ? -1 : name.Count(character => character == '_') + 1;
            if (name.StartsWith("Push", StringComparison.Ordinal))
                return name.Count(character => character == '_') + 1;
            return -1;
        }

        public static string[] GetMethodCode(MethodInfo method, Type containingType)
        {
            return GetMethodBaseCode(method);
        }

        public static string[] GetConstructorCode(ConstructorInfo constructor)
        {
            return GetMethodBaseCode(constructor);
        }

        private static string[] GetMethodBaseCode(MethodBase method)
        {
            return GetMethodBaseCode(method, []);
        }

        private static string[] GetMethodBaseCode(MethodBase method, HashSet<int> activeLocalFunctions)
        {
            IlInstruction[] instructions = ReadInstructions(method);
            if (instructions.Length == 0)
                return [];

            var stack = new List<string>();
            var code = new List<string>();
            Dictionary<string, List<string>> interpolatedStrings = [];
            foreach (MethodInfo localFunction in GetLocalFunctions(method))
            {
                if (!activeLocalFunctions.Add(localFunction.MetadataToken))
                    continue;

                string parameters = string.Join(", ", localFunction.GetParameters()
                    .Select(parameter => $"{FixInvalidName(parameter.Name)}: any"));
                code.Add($"function {GetLocalFunctionName(localFunction)}({parameters}) {{");
                foreach (string line in GetMethodBaseCode(localFunction, activeLocalFunctions))
                    code.Add($"{TypeScript.Space(0, 1)}{line}");
                code.Add("}");
                activeLocalFunctions.Remove(localFunction.MetadataToken);
            }

            LocalVariableInfo[] locals = method.GetMethodBody()?.LocalVariables.ToArray() ?? [];
            for (int i = 0; i < locals.Length; i++)
            {
                if (locals[i].LocalType == typeof(DefaultInterpolatedStringHandler))
                    continue;
                code.Add($"let loc{i}: {TypeScript.GetLocalVariableTypeName(locals[i].LocalType)};");
            }

            Dictionary<int, int> instructionIndices = instructions
                .Select((instruction, index) => (instruction.Offset, index))
                .ToDictionary(pair => pair.Offset, pair => pair.index);
            int ilLength = method.GetMethodBody()?.GetILAsByteArray()?.Length ?? 0;
            instructionIndices[ilLength] = instructions.Length;
            Dictionary<int, int> closeBlockCounts = [];
            HashSet<int> elseBranchIndices = [];
            HashSet<int> skippedBranchIndices = [];
            HashSet<int> loopBackBranchIndices = [];
            Dictionary<int, string[]> stackSnapshots = [];
            Dictionary<int, string> preTestLoops = [];
            int blockDepth = 0;

            void Emit(string line) => code.Add($"{TypeScript.Space(0, blockDepth)}{line}");

            for (int branchIndex = 0; branchIndex < instructions.Length; branchIndex++)
            {
                string branchName = instructions[branchIndex].OpCode.Name ?? "";
                if (!branchName.StartsWith("brtrue", StringComparison.Ordinal)
                    && !branchName.StartsWith("brfalse", StringComparison.Ordinal)
                    && BranchOperator(branchName) == null)
                    continue;

                if (!TryGetBranchTargetIndex(instructions, instructionIndices, branchIndex, out int bodyStartIndex)
                    || bodyStartIndex >= branchIndex
                    || !TryGetPreTestLoop(
                        method,
                        instructions,
                        instructionIndices,
                        branchIndex,
                        bodyStartIndex,
                        out string loopCondition,
                        out int conditionStartIndex,
                        out int entryBranchIndex))
                    continue;

                preTestLoops[bodyStartIndex] = loopCondition;
                skippedBranchIndices.Add(entryBranchIndex);
                for (int i = conditionStartIndex; i <= branchIndex; i++)
                    skippedBranchIndices.Add(i);
                closeBlockCounts[branchIndex + 1] = closeBlockCounts.GetValueOrDefault(branchIndex + 1) + 1;
            }

            for (int instructionIndex = 0; instructionIndex < instructions.Length; instructionIndex++)
            {
                if (preTestLoops.TryGetValue(instructionIndex, out string? loopCondition))
                {
                    Emit($"while ({loopCondition}) {{");
                    blockDepth++;
                }
                if (elseBranchIndices.Contains(instructionIndex))
                {
                    blockDepth--;
                    Emit("} else {");
                    blockDepth++;
                    stack.Clear();
                    stack.AddRange(stackSnapshots[instructionIndex]);
                }
                if (closeBlockCounts.Remove(instructionIndex, out int closeCount))
                {
                    for (int i = 0; i < closeCount; i++)
                    {
                        blockDepth--;
                        Emit("}");
                    }
                }

                if (skippedBranchIndices.Contains(instructionIndex)
                    || loopBackBranchIndices.Contains(instructionIndex))
                    continue;

                IlInstruction instruction = instructions[instructionIndex];
                OpCode op = instruction.OpCode;
                string name = op.Name ?? "";
                object? operand = instruction.Operand;

                if (name is "ldarg.0" or "ldarg.1" or "ldarg.2" or "ldarg.3" or "ldarg.s" or "ldarg")
                {
                    int index = name switch
                    {
                        "ldarg.0" => 0,
                        "ldarg.1" => 1,
                        "ldarg.2" => 2,
                        "ldarg.3" => 3,
                        "ldarg.s" or "ldarg" => Convert.ToInt32(operand),
                        _ => 0
                    };
                    stack.Add(ArgumentName(method, index));
                }
                else if (name is "ldarga" or "ldarga.s")
                {
                    stack.Add(ArgumentName(method, Convert.ToInt32(operand)));
                }
                else if (name is "starg" or "starg.s")
                {
                    int index = Convert.ToInt32(operand);
                    int parameterIndex = index - (method.IsStatic ? 0 : 1);
                    Type? parameterType = parameterIndex >= 0 && parameterIndex < method.GetParameters().Length
                        ? method.GetParameters()[parameterIndex].ParameterType
                        : null;
                    string value = Pop(stack);
                    Emit($"{ArgumentName(method, index)} = {(parameterType == null ? value : ConvertConstantForType(value, parameterType))};");
                }
                else if (name is "ldloc.0" or "ldloc.1" or "ldloc.2" or "ldloc.3" or "ldloc.s" or "ldloc")
                {
                    int index = name switch
                    {
                        "ldloc.0" => 0,
                        "ldloc.1" => 1,
                        "ldloc.2" => 2,
                        "ldloc.3" => 3,
                        "ldloc.s" or "ldloc" => Convert.ToInt32(operand),
                        _ => 0
                    };
                    stack.Add($"loc{index}");
                }
                else if (name is "ldloca" or "ldloca.s")
                {
                    stack.Add($"loc{Convert.ToInt32(operand)}");
                }
                else if (name.StartsWith("stloc", StringComparison.Ordinal))
                {
                    int index = name switch
                    {
                        "stloc.0" => 0,
                        "stloc.1" => 1,
                        "stloc.2" => 2,
                        "stloc.3" => 3,
                        "stloc.s" or "stloc" => Convert.ToInt32(operand),
                        _ => 0
                    };
                    string value = Pop(stack);
                    Type localType = index >= 0 && index < locals.Length ? locals[index].LocalType : typeof(object);
                    Emit($"loc{index} = {ConvertConstantForType(value, localType)};");
                }
                else if (name.StartsWith("ldc.", StringComparison.Ordinal))
                {
                    stack.Add(IntegerConstant(name, operand));
                }
                else if (name == "ldstr")
                {
                    try { stack.Add(Literal(method.Module.ResolveString(Convert.ToInt32(operand)))); }
                    catch { stack.Add("\"\""); }
                }
                else if (name == "ldnull")
                    stack.Add("null");
                else if (name is "ldsfld" or "ldsflda" or "ldfld" or "ldflda")
                {
                    if (operand is int token)
                    {
                        MemberInfo? member = ResolveMember(method, token);
                        string field = member is FieldInfo fi ? FieldName(fi) : "field";
                        bool isInstanceField = name is "ldfld" or "ldflda";
                        string fieldExpression = isInstanceField
                            ? $"{Pop(stack)}.{field}"
                            : member is FieldInfo staticField
                                ? staticField.DeclaringType == typeof(string) && staticField.Name == "Empty"
                                    ? "\"\""
                                    : $"{FixInvalidName(staticField.DeclaringType?.Name)}.{field}"
                                : field;
                        stack.Add(fieldExpression);
                    }
                }
                else if (name == "stsfld" || name == "stfld")
                {
                    if (operand is int token)
                    {
                        MemberInfo? member = ResolveMember(method, token);
                        string field = member is FieldInfo fi ? FieldName(fi) : "field";
                        string value = Pop(stack);
                        if (member is FieldInfo fieldInfo)
                            value = ConvertConstantForType(value, fieldInfo.FieldType);
                        if (name == "stfld")
                        {
                            if (value == "null" && member is FieldInfo fi3)
                            {
                                value = fi3.FieldType == typeof(bool) ? "false"
                                    : fi3.FieldType.IsValueType && fi3.FieldType != typeof(string) ? "0"
                                    : fi3.FieldType == typeof(string) ? "\"\"" : "null";
                            }
                            Emit($"{Pop(stack)}.{field} = {value};");
                        }
                        else
                        {
                            if (member is FieldInfo fi2 && (fi2.IsLiteral || fi2.IsInitOnly))
                            {
                                // readonly and const fields are initialized by their declarations,
                                // not by the emitted static constructor.
                            }
                            else
                            {
                                Emit($"{FixInvalidName(member is FieldInfo staticFieldInfo ? staticFieldInfo.DeclaringType?.Name : null)}.{field} = {value};");
                            }
                        }
                    }
                }
                else if (name == "dup")
                    stack.Add(stack.Count > 0 ? stack[^1] : "/* missing value */");
                else if (name == "pop")
                    Pop(stack);
                else if ((name.StartsWith("brtrue", StringComparison.Ordinal)
                    || name.StartsWith("brfalse", StringComparison.Ordinal)
                    || BranchOperator(name) != null)
                    && TryGetBranchTargetIndex(instructions, instructionIndices, instructionIndex, out int targetIndex))
                {
                    bool isUnaryBranch = name.StartsWith("brtrue", StringComparison.Ordinal)
                        || name.StartsWith("brfalse", StringComparison.Ordinal);
                    if (isUnaryBranch
                        && TryGetNullConditionalVoidCall(
                            method,
                            instructions,
                            instructionIndices,
                            instructionIndex,
                            out MethodInfo nullConditionalMethod,
                            out int nullConditionalCallIndex,
                            out string? nullConditionalReceiver,
                            out bool receiverRemainsOnStack))
                    {
                        string condition = Pop(stack);
                        string? receiver = receiverRemainsOnStack ? Pop(stack) : nullConditionalReceiver;
                        if (receiver == null)
                        {
                            stack.Add(condition);
                            continue;
                        }

                        List<string> receiverStack = [receiver];
                        string call = ResolveCall(method, nullConditionalMethod, receiverStack, newObject: false);
                        string receiverPrefix = $"{receiver}.";
                        if (call.StartsWith(receiverPrefix, StringComparison.Ordinal))
                        {
                            Emit($"{receiver}?.{call[receiverPrefix.Length..]};");
                            for (int skippedIndex = instructionIndex + 1; skippedIndex <= nullConditionalCallIndex + 1; skippedIndex++)
                                skippedBranchIndices.Add(skippedIndex);
                            continue;
                        }

                        if (receiverRemainsOnStack)
                            stack.Add(receiver);
                        stack.Add(condition);
                    }

                    string right = "";
                    string left;
                    if (isUnaryBranch)
                    {
                        left = Pop(stack);
                    }
                    else
                    {
                        right = Pop(stack);
                        left = Pop(stack);
                    }

                    string takenCondition = BranchCondition(name, left, right, branchTaken: true);
                    if (TryBuildTernaryExpression(
                        method,
                        instructions,
                        instructionIndices,
                        instructionIndex,
                        takenCondition,
                        out string ternaryExpression,
                        out int mergeIndex))
                    {
                        stack.Add(ternaryExpression);
                        instructionIndex = mergeIndex - 1;
                        continue;
                    }

                    if (targetIndex > instructionIndex)
                    {
                        string fallThroughCondition = BranchCondition(name, left, right, branchTaken: false);
                        if (TryGetLoopBackBranch(method, instructions, instructionIndices, instructionIndex, targetIndex, out int loopBackIndex))
                        {
                            Emit($"while ({fallThroughCondition}) {{");
                            blockDepth++;
                            loopBackBranchIndices.Add(loopBackIndex);
                            closeBlockCounts[targetIndex] = closeBlockCounts.GetValueOrDefault(targetIndex) + 1;
                        }
                        else if (TryGetIfElseBranches(
                            method,
                            instructions,
                            instructionIndices,
                            instructionIndex,
                            targetIndex,
                            out int branchToEndIndex,
                            out int ifElseEndIndex))
                        {
                            Emit($"if ({fallThroughCondition}) {{");
                            blockDepth++;
                            elseBranchIndices.Add(targetIndex);
                            stackSnapshots[targetIndex] = [.. stack];
                            closeBlockCounts[ifElseEndIndex] = closeBlockCounts.GetValueOrDefault(ifElseEndIndex) + 1;
                            skippedBranchIndices.Add(branchToEndIndex);
                        }
                        else
                        {
                            Emit($"if ({fallThroughCondition}) {{");
                            blockDepth++;
                            closeBlockCounts[targetIndex] = closeBlockCounts.GetValueOrDefault(targetIndex) + 1;
                        }
                    }
                    else
                    {
                        Emit($"if ({takenCondition}) {{ /* branch IL_{Convert.ToInt32(operand):X4} */ }}");
                    }
                }
                else if (IsUnconditionalBranch(instruction))
                {
                    if (!TryGetBranchTargetIndex(instructions, instructionIndices, instructionIndex, out int unconditionalTargetIndex)
                        || unconditionalTargetIndex != instructionIndex + 1)
                        Emit($"/* branch IL_{Convert.ToInt32(operand):X4} */");
                }
                else if (name == "switch")
                {
                    string value = Pop(stack);
                    int[] targets = operand as int[] ?? [];
                    Emit($"switch ({value}) {{ /* targets: {string.Join(", ", targets.Select(t => $"IL_{t:X4}"))} */ }}");
                }
                else if (name.StartsWith("ldelem", StringComparison.Ordinal))
                {
                    string index = Pop(stack);
                    stack.Add($"{Pop(stack)}[{index}]");
                }
                else if (name.StartsWith("stelem", StringComparison.Ordinal))
                {
                    string value = Pop(stack);
                    string index = Pop(stack);
                    Emit($"{Pop(stack)}[{index}] = {value};");
                }
                else if (name == "ldlen")
                    stack.Add($"{Pop(stack)}.length");
                else if (name.StartsWith("ldind.", StringComparison.Ordinal))
                {
                }
                else if (name.StartsWith("stind.", StringComparison.Ordinal))
                {
                    string value = Pop(stack);
                    string target = Pop(stack);
                    Emit($"{target} = {value};");
                }
                else if (name == "newarr")
                    stack.Add($"new Array({Pop(stack)})");
                else if (name is "call" or "callvirt" or "newobj")
                {
                    if (operand is int token)
                    {
                        MemberInfo? member = ResolveMember(method, token);
                        if (member is MethodInfo handlerMethod
                            && handlerMethod.DeclaringType == typeof(DefaultInterpolatedStringHandler))
                        {
                            HandleInterpolatedStringHandlerCall(handlerMethod, stack, interpolatedStrings);
                            continue;
                        }

                        string call = ResolveCall(method, member, stack, name == "newobj");
                        if (name == "newobj")
                            stack.Add(call);
                        else if (IsVoidMethod(member))
                        {
                            if (!string.IsNullOrEmpty(call))
                                Emit(call + ";");
                        }
                        else
                            stack.Add(call);
                    }
                }
                else if (name is "ldftn" or "ldvirtftn")
                {
                    if (operand is int token)
                    {
                        MemberInfo? member = ResolveMember(method, token);
                        stack.Add(member is MethodInfo mi ? FixInvalidName(mi.Name) : "/* method */");
                    }
                }
                else if (name is "add" or "sub" or "mul" or "div" or "rem" or "and" or "or" or "xor" or "shl" or "shr" or "shr.un")
                {
                    string right = Pop(stack);
                    string left = Pop(stack);
                    string symbol = name switch
                    {
                        "add" => "+", "sub" => "-", "mul" => "*", "div" => "/", "rem" => "%",
                        "and" => "&", "or" => "|", "xor" => "^", "shl" => "<<", "shr" or "shr.un" => ">>",
                        _ => "+"
                    };
                    stack.Add($"({left} {symbol} {right})");
                }
                else if (name is "neg" or "not")
                    stack.Add(name == "neg" ? $"(-{Pop(stack)})" : $"(!{Pop(stack)})");
                else if (name is "ceq" or "cgt" or "cgt.un" or "clt" or "clt.un")
                {
                    string right = Pop(stack);
                    string left = Pop(stack);
                    string symbol = name is "ceq" ? "===" : name.StartsWith("cgt", StringComparison.Ordinal) ? ">" : "<";
                    stack.Add($"({left} {symbol} {right})");
                }
                else if (name.StartsWith("conv.", StringComparison.Ordinal))
                    stack.Add($"Number({Pop(stack)})");
                else if (name is "castclass" or "isinst")
                    stack.Add(Pop(stack));
                else if (name is "box" or "unbox" or "unbox.any")
                    stack.Add(Pop(stack));
                else if (name == "initobj")
                {
                    Pop(stack);
                    Emit("/* initialized value */");
                }
                else if (name == "ldobj")
                    stack.Add(Pop(stack));
                else if (name == "stobj")
                {
                    string value = Pop(stack);
                    Emit($"{Pop(stack)} = {value};");
                }
                else if (name == "ldtoken")
                {
                    if (operand is int token)
                    {
                        MemberInfo? member = ResolveMember(method, token);
                        stack.Add(member is MemberInfo mi ? FixInvalidName(mi.Name) : "/* token */");
                    }
                }
                else if (name == "ret")
                {
                    if (method is MethodInfo mi && mi.ReturnType != typeof(void) && stack.Count > 0)
                    {
                        string value = Pop(stack);
                        if (mi.ReturnType == typeof(bool) && value is "0" or "1")
                        {
                            value = value == "0" ? "false" : "true";
                        }
                        else if (value == "null")
                        {
                            value = mi.ReturnType == typeof(bool) ? "false"
                                : mi.ReturnType.IsValueType ? "0"
                                : mi.ReturnType == typeof(string) ? "\"\"" : "null";
                        }
                        Emit($"return {value};");
                    }
                }
                else if (name == "throw")
                    Emit($"throw {Pop(stack)};");
                else if (name == "ckfinite")
                    stack.Add(Pop(stack));
                else if (name is not ("nop" or "endfinally" or "endfault"
                    or "volatile." or "constrained." or "readonly." or "tail." or "unaligned." or "no."))
                    Emit($"/* unsupported IL: {name} */");
            }

            if (closeBlockCounts.Remove(instructions.Length, out int trailingCloseCount))
            {
                for (int i = 0; i < trailingCloseCount; i++)
                {
                    blockDepth--;
                    Emit("}");
                }
            }

            return [.. code];
        }

    }
}
