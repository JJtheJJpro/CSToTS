# C# to TypeScript

This is a project that converts C# code to respective TypeScript code.

## Tests

The .NET unit tests run from the solution. A separate TypeScript compiler test suite validates generated output with `tsc` and requires Node.js plus local npm dependencies:

```powershell
npm ci --prefix CSToTS.TypeScript.Tests
dotnet test CSToTS.sln
npm test --prefix CSToTS.TypeScript.Tests
```

To run only the .NET suite:

```powershell
dotnet test CSToTS.Tests/CSToTS.Tests.csproj
```

To run only the TypeScript compiler suite after installing its dependencies:

```powershell
npm test --prefix CSToTS.TypeScript.Tests
```

`GetFullTypeScriptCode` accepts `TypeScriptOutputOptions` to control output paths and namespace wrappers. The defaults preserve the existing namespace-directory layout without namespace wrappers:

```csharp
TypeScript.GetFullTypeScriptCode(typeof(MyType), new TypeScriptOutputOptions
{
    FileLayout = TypeScriptOutputLayout.Flat,
    EmitNamespaces = true
});
```

## Node.js console mappings

Calls to `System.Console.WriteLine` are emitted as `console.log`, and `Console.Error.WriteLine` as `console.error`. Interpolated strings are emitted as native TypeScript template literals; literal escaping and alignment are handled without emitting .NET's interpolated-string handler. Culture-specific .NET format specifiers are not yet translated. Calls to `System.Console.ReadLine` use Node.js `fs.readSync` on standard input to preserve the synchronous C# behavior, so the generated code blocks until a line is available. Generated modules import `readSync` from `node:fs` and `TextDecoder` from `node:util`; compiling generated code requires Node.js type definitions.

`System.String.Concat` calls are translated to native TypeScript concatenation, including string-array/enumerable overloads.

CoreLib `System.SR` string properties used by converted methods are resolved through reflection and emitted as string literals using the converter's current .NET UI culture. JavaScript runtimes, including browsers and Node.js, manage memory automatically but do not expose portable equivalents for .NET GC metrics or explicit collection controls. Converted lifetime-only GC calls are omitted, array allocation preserves C# default element values, and unsupported GC queries throw an explicit error.

The IL converter reconstructs common ternary expressions, balanced `if`/`else` blocks, and `while`/`for` loop patterns. Other branch layouts, including switch statements and control flow with unsupported stack merges, are not fully reconstructed yet.