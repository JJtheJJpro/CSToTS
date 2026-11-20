using System.Diagnostics;
using System.Reflection;
using System.Reflection.Emit;
using CSToTS;
using Xunit;

namespace CSToTS.TypeScriptCompilerTests;

public class TypeScriptCompilerTests
{
    [Fact]
    public async Task GeneratedTypesCompileWithTypeScript()
    {
        string projectDirectory = FindProjectDirectory();
        string compilerPath = Path.Combine(projectDirectory, "node_modules", "typescript", "bin", "tsc");
        Assert.True(File.Exists(compilerPath), $"TypeScript compiler not found at {compilerPath}. Run npm ci in this project.");

        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "CSToTS.TypeScript.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        string sourcePath = Path.Combine(temporaryDirectory, "generated.ts");

        try
        {
            string generatedCode = string.Join(
                Environment.NewLine,
                TypeScript.GetTypeScriptCode(typeof(ICompileContract), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(IExtendedCompileContract), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(IExplicitContract), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(IExplicitPropertyContract), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(CompileFixture), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(CompileFields), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(StringConcatCompileFixture), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(GarbageCollectionCompileFixture), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(ConstructorOverloadCompileFixture), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(IndexerOverloadCompileFixture), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(OperatorOverloadCompileFixture), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(ExplicitOnlyFixture), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(ExplicitPropertyFixture), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(StaticCompileFixture), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(AbstractCompileFixture), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(CompileStruct), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(NestedCompileFixture), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(LocalFunctionCompileFixture), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(InterpolatedConsoleCompileFixture), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(ComplexInterpolationCompileFixture), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(BooleanLocalCompileFixture), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(ControlFlowCompileFixture), @namespace: false, r: 0),
                TypeScript.GetTypeScriptCode(typeof(CompileStatus), @namespace: true, r: 0));
            Assert.DoesNotContain("BackingField", generatedCode);
            Assert.Contains("function Increment(value: any)", generatedCode);
            Assert.Contains("public Count!: number;", generatedCode);
            Assert.Contains("public readonly ReadonlyCount!: number;", generatedCode);
            await File.WriteAllTextAsync(sourcePath, generatedCode);

            (int exitCode, string output) = await RunCompilerAsync(projectDirectory, sourcePath);
            Assert.True(exitCode == 0, $"tsc rejected generated TypeScript:{Environment.NewLine}{output}{Environment.NewLine}{generatedCode}");
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GeneratedModulesImportBaseTypesAndCallSuper()
    {
        string projectDirectory = FindProjectDirectory();
        foreach (bool emitNamespaces in new[] { false, true })
        {
            string previousDirectory = Environment.CurrentDirectory;
            string temporaryDirectory = Path.Combine(Path.GetTempPath(), "CSToTS.TypeScript.Tests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temporaryDirectory);

            try
            {
                Environment.CurrentDirectory = temporaryDirectory;
                TypeScript.GetFullTypeScriptCode(typeof(GeneratedChild), new TypeScriptOutputOptions
                {
                    FileLayout = TypeScriptOutputLayout.NamespaceDirectories,
                    EmitNamespaces = emitNamespaces
                });

                string modulesDirectory = Path.Combine(temporaryDirectory, "modules");
                string basePath = Path.Combine(modulesDirectory, "CSToTS", "TypeScriptCompilerTests", "Fixtures", "GeneratedBase.ts");
                string derivedPath = Path.Combine(modulesDirectory, "CSToTS", "TypeScriptCompilerTests", "GeneratedChild.ts");
                string baseCode = await File.ReadAllTextAsync(basePath);
                string derivedCode = await File.ReadAllTextAsync(derivedPath);

                Assert.DoesNotContain("super();", baseCode);
                Assert.Contains("super();", derivedCode);
                Assert.Contains(
                    emitNamespaces
                        ? "import * as __dependency0 from \"./Fixtures/GeneratedBase\";"
                        : "import { GeneratedBase } from \"./Fixtures/GeneratedBase\";",
                    derivedCode);

                string[] generatedFiles = Directory.GetFiles(modulesDirectory, "*.ts", SearchOption.AllDirectories);
                (int exitCode, string output) = await RunCompilerAsync(projectDirectory, generatedFiles);
                Assert.True(exitCode == 0, $"tsc rejected generated modules:{Environment.NewLine}{output}{Environment.NewLine}{derivedCode}");
            }
            finally
            {
                Environment.CurrentDirectory = previousDirectory;
                Directory.Delete(temporaryDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task GeneratedFlatModulesWithNamespacesCompile()
    {
        string projectDirectory = FindProjectDirectory();
        string previousDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "CSToTS.TypeScript.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            Environment.CurrentDirectory = temporaryDirectory;
            TypeScript.GetFullTypeScriptCode(typeof(GeneratedChild), new TypeScriptOutputOptions
            {
                FileLayout = TypeScriptOutputLayout.Flat,
                EmitNamespaces = true
            });

            string modulesDirectory = Path.Combine(temporaryDirectory, "modules");
            string basePath = Path.Combine(modulesDirectory, "GeneratedBase.ts");
            string derivedPath = Path.Combine(modulesDirectory, "GeneratedChild.ts");
            string derivedCode = await File.ReadAllTextAsync(derivedPath);

            Assert.Contains("import * as __dependency0 from \"./GeneratedBase\";", derivedCode);
            Assert.Contains("__dependency0.CSToTS.TypeScriptCompilerTests.Fixtures.GeneratedBase", derivedCode);

            string[] generatedFiles = Directory.GetFiles(modulesDirectory, "*.ts", SearchOption.AllDirectories);
            Assert.Contains(basePath, generatedFiles);
            (int exitCode, string output) = await RunCompilerAsync(projectDirectory, generatedFiles);
            Assert.True(exitCode == 0, $"tsc rejected flat generated modules:{Environment.NewLine}{output}{Environment.NewLine}{derivedCode}");
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GeneratedConsoleAbstractionCompilesWithNodeTypes()
    {
        string projectDirectory = FindProjectDirectory();
        string previousDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "CSToTS.TypeScript.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            Environment.CurrentDirectory = temporaryDirectory;
            TypeScript.GetFullTypeScriptCode(typeof(ConsoleAbstractionFixture));

            string[] generatedFiles = Directory.GetFiles(
                Path.Combine(temporaryDirectory, "modules"),
                "*.ts",
                SearchOption.AllDirectories);
            Assert.Contains(generatedFiles, path => File.ReadAllText(path).Contains("readSync(0, one, 0, 1, null)"));

            (int exitCode, string output) = await RunCompilerAsync(projectDirectory, generatedFiles);
            Assert.True(exitCode == 0, $"tsc rejected generated Console TypeScript:{Environment.NewLine}{output}");
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GeneratedMethodsWithRepeatedOutParametersCompile()
    {
        string projectDirectory = FindProjectDirectory();
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "CSToTS.TypeScript.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        string sourcePath = Path.Combine(temporaryDirectory, "generated.ts");

        try
        {
            MethodInfo method = CreateMethodWithRepeatedOutParameters();
            string generatedCode = $"export class DuplicateOutParameters {{{Environment.NewLine}{TypeScript.GetTypeScriptCode(method, @interface: false)}}}";
            Assert.Contains("Repeated(out: { tokenType: number }, out2: { tokenValue: number })", generatedCode);
            await File.WriteAllTextAsync(sourcePath, generatedCode);

            (int exitCode, string output) = await RunCompilerAsync(projectDirectory, sourcePath);
            Assert.True(exitCode == 0, $"tsc rejected generated TypeScript:{Environment.NewLine}{output}{Environment.NewLine}{generatedCode}");
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static MethodInfo CreateMethodWithRepeatedOutParameters()
    {
        AssemblyBuilder assembly = AssemblyBuilder.DefineDynamicAssembly(
            new AssemblyName($"DuplicateOutParameters_{Guid.NewGuid():N}"),
            AssemblyBuilderAccess.Run);
        ModuleBuilder module = assembly.DefineDynamicModule("TestModule");
        TypeBuilder type = module.DefineType("DuplicateOutParametersFixture");
        MethodBuilder method = type.DefineMethod(
            "Repeated",
            MethodAttributes.Public | MethodAttributes.Static,
            typeof(void),
            [typeof(int).MakeByRefType(), typeof(int).MakeByRefType()]);
        method.DefineParameter(1, ParameterAttributes.Out, "tokenType");
        method.DefineParameter(2, ParameterAttributes.Out, "tokenValue");
        method.GetILGenerator().Emit(System.Reflection.Emit.OpCodes.Ret);

        return type.CreateType()!.GetMethod("Repeated")!;
    }

    private static async Task<(int ExitCode, string Output)> RunCompilerAsync(string projectDirectory, params string[] sourcePaths)
    {
        string compilerPath = Path.Combine(projectDirectory, "node_modules", "typescript", "bin", "tsc");
        using Process compiler = new()
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "node",
                WorkingDirectory = projectDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            }
        };
        compiler.StartInfo.ArgumentList.Add(compilerPath);
        compiler.StartInfo.ArgumentList.Add("--noEmit");
        compiler.StartInfo.ArgumentList.Add("--strict");
        compiler.StartInfo.ArgumentList.Add("--target");
        compiler.StartInfo.ArgumentList.Add("ES2020");
        compiler.StartInfo.ArgumentList.Add("--lib");
        compiler.StartInfo.ArgumentList.Add("ES2020");
        compiler.StartInfo.ArgumentList.Add("--moduleResolution");
        compiler.StartInfo.ArgumentList.Add("node");
        foreach (string sourcePath in sourcePaths)
        {
            compiler.StartInfo.ArgumentList.Add(sourcePath);
        }

        Assert.True(compiler.Start(), "Could not start Node.js to run the TypeScript compiler.");
        Task<string> standardOutput = compiler.StandardOutput.ReadToEndAsync();
        Task<string> standardError = compiler.StandardError.ReadToEndAsync();
        await compiler.WaitForExitAsync();

        return (compiler.ExitCode, (await standardOutput) + (await standardError));
    }

    private static string FindProjectDirectory()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "package.json")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the TypeScript compiler test project.");
    }

    private interface ICompileContract
    {
        string Name { get; set; }

        string Convert(int amount);
    }

    private interface IExtendedCompileContract : ICompileContract
    {
        bool IsValid();
    }

    private interface IExplicitContract
    {
        string Run(int value);
    }

    private interface IExplicitPropertyContract
    {
        int Value { get; set; }
    }

    private class CompileFixture : IExtendedCompileContract
    {
        public string Name { get; set; } = string.Empty;

        public CompileFixture()
        {
        }

        public CompileFixture(int initialValue)
        {
        }

        static CompileFixture()
        {
        }

        public string Convert(int amount) => amount.ToString();

        public bool IsValid() => true;

        public string Format(int value) => value.ToString();

        public string Format(string value) => value;

        public string Format() => string.Empty;

        public string Format(bool enabled = true) => enabled.ToString();

        public void Log(int value)
        {
        }

        public void Log(string value)
        {
        }

        public void Collect(params int[] values)
        {
        }

        public T Echo<T>(T value) => value;

        public Task<string> Map(int? value, List<int> values, Dictionary<string, int> lookup) =>
            Task.FromResult(value?.ToString() ?? string.Empty);
    }

    private class CompileFields
    {
        public int Count = 0;
        public readonly int ReadonlyCount = 0;
        public static readonly string Label = "label";
        public const int DefaultCount = 1;
    }

    private class GarbageCollectionCompileFixture
    {
        public static char[] AllocateChars(int length) => GC.AllocateUninitializedArray<char>(length);

        public static long GetAllocatedBytes() => GC.GetTotalAllocatedBytes(false);
    }

    private class StringConcatCompileFixture
    {
        public string Concat(string? name, bool enabled) => string.Concat("Name:", name, "\r\n", enabled);

        public string ConcatArray(string?[] values) => string.Concat(values);
    }

    private class ConstructorOverloadCompileFixture
    {
        public string Kind = string.Empty;

        public ConstructorOverloadCompileFixture()
        {
            Kind = "empty";
        }

        public ConstructorOverloadCompileFixture(int value)
        {
            Kind = "number";
        }

        public ConstructorOverloadCompileFixture(string value)
        {
            Kind = "string";
        }
    }

    private class IndexerOverloadCompileFixture
    {
        public string this[int index]
        {
            get => "number";
            set { }
        }

        public string this[string key]
        {
            get => "text";
            set { }
        }
    }

    private class OperatorOverloadCompileFixture
    {
        public static OperatorOverloadCompileFixture operator +(OperatorOverloadCompileFixture left, int right) => left;

        public static OperatorOverloadCompileFixture operator +(OperatorOverloadCompileFixture left, string right) => left;
    }

    private class ExplicitOnlyFixture : IExplicitContract
    {
        string IExplicitContract.Run(int value) => value.ToString();
    }

    private class ExplicitPropertyFixture : IExplicitPropertyContract
    {
        int IExplicitPropertyContract.Value { get; set; }
    }

    private static class StaticCompileFixture
    {
        static StaticCompileFixture()
        {
        }

        public static int Value => 1;
    }

    private abstract class AbstractCompileFixture
    {
        public abstract string Value { get; }

        public abstract int GetValue();

        public abstract string Convert(int value);

        public abstract string Convert(string value);
    }

    private struct CompileStruct
    {
        public int Value = 0;

        public CompileStruct()
        {
        }
    }

    private class NestedCompileFixture
    {
        public class Nested
        {
            public int Value = 0;
        }
    }

    private class LocalFunctionCompileFixture
    {
        public int Calculate(int value)
        {
            static int Increment(int value) => value + 1;
            return Increment(value);
        }
    }

    private class ConsoleAbstractionFixture
    {
        public string? ReadLine() => Console.ReadLine();

        public void WriteLine(string? value) => Console.WriteLine(value);
    }

    private static class InterpolatedConsoleCompileFixture
    {
        public static void Print(int value)
        {
            Console.WriteLine($"Value: {value}");
            Console.Error.WriteLine($"Error: {value}");
        }
    }

    private static class ComplexInterpolationCompileFixture
    {
        public static string Format(string name, int count, double ratio, bool enabled, char marker, int? optional) =>
            $"Report [{marker}] {name}: count={count,6}; ratio={ratio,-8}; enabled={enabled}; optional={optional}; braces={{ok}}; template=`; literal=${{name}}";
    }

    private static class BooleanLocalCompileFixture
    {
        public static bool ReturnTrue()
        {
            bool enabled = true;
            return enabled;
        }
    }

    private static class ControlFlowCompileFixture
    {
        public static int Choose(bool useFirst, int first, int second)
        {
            int result;
            if (useFirst)
                result = first;
            else
                result = second;
            return result;
        }

        public static int Sum(int count)
        {
            int sum = 0;
            int index = 0;
            while (index < count)
            {
                sum += index;
                index++;
            }
            return sum;
        }

        public static int ToInt(bool condition) => condition ? 1 : 0;
    }

    private enum CompileStatus
    {
        Ready,
        Complete
    }

    private class GeneratedChild : Fixtures.GeneratedBase
    {
        public GeneratedChild()
        {
        }

        public GeneratedChild(int initialValue)
        {
        }
    }
}
