using System.Reflection;

namespace CSToTS.Abstractions;

internal static class SystemConsole
{
    private const string ReadLineExpression =
        "((): any => { const bytes: number[] = []; const one = new Uint8Array(1); const decode = () => { if (bytes[bytes.length - 1] === 13) bytes.pop(); return new TextDecoder().decode(new Uint8Array(bytes)); }; while (true) { const count = readSync(0, one, 0, 1, null); if (count === 0) return bytes.length > 0 ? decode() : null; if (one[0] === 10) return decode(); bytes.push(one[0]); } })()";

    internal static bool IsReadLine(MethodInfo method) =>
        method.DeclaringType == typeof(global::System.Console)
        && method.Name == nameof(global::System.Console.ReadLine);

    internal static bool TryConvertCall(
        MethodInfo method,
        IReadOnlyList<string> arguments,
        string? receiver,
        out string expression)
    {
        if (method.DeclaringType == typeof(global::System.Console))
        {
            if (method.Name == nameof(global::System.Console.ReadLine) && arguments.Count == 0)
            {
                expression = ReadLineExpression;
                return true;
            }

            if (method.IsSpecialName && method.Name is "get_Out" or "get_Error")
            {
                expression = method.Name == "get_Error" ? "console.error" : "console.log";
                return true;
            }

            if (method.Name == nameof(global::System.Console.WriteLine))
            {
                expression = $"console.log({string.Join(", ", arguments)})";
                return true;
            }
        }

        if (method.DeclaringType == typeof(global::System.IO.TextWriter)
            && method.Name == nameof(global::System.IO.TextWriter.WriteLine)
            && receiver is "console.log" or "console.error")
        {
            expression = $"{receiver}({string.Join(", ", arguments)})";
            return true;
        }

        expression = string.Empty;
        return false;
    }
}
