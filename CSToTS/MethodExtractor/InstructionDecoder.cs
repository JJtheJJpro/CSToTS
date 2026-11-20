using System.Globalization;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;

namespace CSToTS;

internal static partial class MethodExtractor
{
    private static unsafe IlInstruction[] ReadInstructions(MethodBase method)
    {
        byte[]? il = method.GetMethodBody()?.GetILAsByteArray();
        if (il == null || il.Length == 0)
            return [];

        fixed (byte* pointer = il)
        {
            BlobReader reader = new(pointer, il.Length);
            List<IlInstruction> instructions = [];
            while (reader.RemainingBytes > 0)
            {
                int offset = reader.Offset;
                OpCode opCode = reader.ReadOpCode();
                object? operand = ReadOperand(ref reader, opCode);
                instructions.Add(new IlInstruction(offset, opCode, operand));
            }
            return [.. instructions];
        }
    }

    private static object? ReadOperand(ref BlobReader reader, OpCode opCode) =>
        opCode.OperandType switch
        {
            OperandType.ShortInlineI => reader.ReadSByte(),
            OperandType.InlineI => reader.ReadInt32(),
            OperandType.InlineI8 => reader.ReadInt64(),
            OperandType.ShortInlineR => reader.ReadSingle(),
            OperandType.InlineR => reader.ReadDouble(),
            OperandType.ShortInlineVar => reader.ReadByte(),
            OperandType.InlineVar => reader.ReadInt16(),
            OperandType.InlineString or OperandType.InlineField or OperandType.InlineMethod
                or OperandType.InlineTok or OperandType.InlineType or OperandType.InlineSig
                => reader.ReadInt32(),
            OperandType.ShortInlineBrTarget => reader.ReadSByte() + reader.Offset,
            OperandType.InlineBrTarget => reader.ReadInt32() + reader.Offset,
            OperandType.InlineSwitch => ReadSwitchTargets(ref reader),
            _ => null
        };

    private static int[] ReadSwitchTargets(ref BlobReader reader)
    {
        int count = reader.ReadInt32();
        int baseOffset = reader.Offset + count * 4;
        int[] targets = new int[count];
        for (int i = 0; i < count; i++)
            targets[i] = baseOffset + reader.ReadInt32();
        return targets;
    }

    private static string Literal(object? value) =>
        value switch
        {
            null => "null",
            string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\"",
            char character => ((int)character).ToString(CultureInfo.InvariantCulture),
            float number => float.IsFinite(number) ? number.ToString("R", CultureInfo.InvariantCulture) : "0",
            double number => double.IsFinite(number) ? number.ToString("R", CultureInfo.InvariantCulture) : "0",
            sbyte number => number.ToString(CultureInfo.InvariantCulture),
            byte number => number.ToString(CultureInfo.InvariantCulture),
            short number => number.ToString(CultureInfo.InvariantCulture),
            ushort number => number.ToString(CultureInfo.InvariantCulture),
            int number => number.ToString(CultureInfo.InvariantCulture),
            uint number => number.ToString(CultureInfo.InvariantCulture),
            long number => number.ToString(CultureInfo.InvariantCulture),
            ulong number => number.ToString(CultureInfo.InvariantCulture),
            bool boolean => boolean ? "true" : "false",
            _ => value.ToString() ?? "null"
        };

    private static string IntegerConstant(string opCodeName, object? operand) =>
        opCodeName switch
        {
            "ldc.i4.m1" => "-1",
            "ldc.i4.0" => "0",
            "ldc.i4.1" => "1",
            "ldc.i4.2" => "2",
            "ldc.i4.3" => "3",
            "ldc.i4.4" => "4",
            "ldc.i4.5" => "5",
            "ldc.i4.6" => "6",
            "ldc.i4.7" => "7",
            "ldc.i4.8" => "8",
            _ => Literal(operand)
        };

    private static MemberInfo? ResolveMember(MethodBase method, int token)
    {
        try
        {
            Type[] typeArguments = method.DeclaringType?.IsGenericType == true
                ? method.DeclaringType.GetGenericArguments()
                : [];
            Type[] methodArguments = method is MethodInfo methodInfo && methodInfo.IsGenericMethod
                ? methodInfo.GetGenericArguments()
                : [];
            return method.Module.ResolveMember(token, typeArguments, methodArguments);
        }
        catch
        {
            return null;
        }
    }

    private static Type? ResolveType(MethodBase method, int token)
    {
        try
        {
            Type[] typeArguments = method.DeclaringType?.IsGenericType == true
                ? method.DeclaringType.GetGenericArguments()
                : [];
            Type[] methodArguments = method is MethodInfo methodInfo && methodInfo.IsGenericMethod
                ? methodInfo.GetGenericArguments()
                : [];
            return method.Module.ResolveType(token, typeArguments, methodArguments);
        }
        catch
        {
            return null;
        }
    }
}
