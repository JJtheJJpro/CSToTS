using System.Reflection;
using CSToTS;
using Xunit;

namespace CSToTS.Tests;

public class ConversionTests
{
    [Fact]
    public void Space_UsesConfiguredIndentationAndSupportsLevels()
    {
        int originalSpaceCount = TypeScript.SpaceCount;

        try
        {
            TypeScript.SpaceCount = 2;

            Assert.Equal("      ", TypeScript.Space(1, 2));
            Assert.Equal(string.Empty, TypeScript.Space(0, 0));
        }

        finally
        {
            TypeScript.SpaceCount = originalSpaceCount;
        }
    }

    [Fact]
    public void MethodCode_ConvertsParametersReturnTypesAndArrays()
    {
        MethodInfo method = typeof(MethodFixture).GetMethod(nameof(MethodFixture.Accept))!;

        string code = TypeScript.GetTypeScriptCode(method, @interface: false);

        Assert.Contains("public Accept(values: number[][]): string[] {", code);
        Assert.DoesNotContain("throw new Error(\"not yet implemented\");", code);
    }

    [Fact]
    public void MethodCode_FormatsGenericTypesAndLowersSpanImplicitConversion()
    {
        MethodInfo spanMethod = typeof(SpanConversionFixture).GetMethod(
            "ConvertSpan",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        string spanCode = TypeScript.GetTypeScriptCode(spanMethod, @interface: false);

        Assert.Contains("new String(value)", spanCode);
        Assert.DoesNotContain("op_Implicit", spanCode);
        Assert.DoesNotContain('`', spanCode);

        MethodInfo listMethod = typeof(SpanConversionFixture).GetMethod(
            "CreateList",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        string listCode = TypeScript.GetTypeScriptCode(listMethod, @interface: false);

        Assert.Contains("[]", listCode);
        Assert.DoesNotContain('`', listCode);
    }

    [Fact]
    public void MethodCode_ConvertsListOperationsToNativeArrayOperations()
    {
        MethodInfo createMethod = typeof(ListConversionFixture).GetMethod(
            "Create",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        string createCode = TypeScript.GetTypeScriptCode(createMethod, @interface: false);

        Assert.Contains("= [];", createCode);
        Assert.Contains(".push(item)", createCode);
        Assert.DoesNotContain("ToArray", createCode);
        Assert.Contains("[...loc0]", createCode);

        MethodInfo countMethod = typeof(ListConversionFixture).GetMethod(
            "GetCount",
            BindingFlags.Static | BindingFlags.NonPublic)!;
        string countCode = TypeScript.GetTypeScriptCode(countMethod, @interface: false);

        Assert.Contains("values.length", countCode);
        Assert.DoesNotContain("values.Count", countCode);
    }

    [Fact]
    public void MethodCode_ConvertsNullConditionalVoidCallsToOptionalChaining()
    {
        MethodInfo method = typeof(NullConditionalCallFixture).GetMethod(
            "DisposeIfPresent",
            BindingFlags.Static | BindingFlags.NonPublic)!;

        string code = TypeScript.GetTypeScriptCode(method, @interface: false);

        Assert.True(code.Contains("resource?.Dispose();", StringComparison.Ordinal), code);
        Assert.DoesNotContain("missing value", code);
    }

    [Fact]
    public void MethodCode_RepresentsRefAndOutParameters()
    {
        MethodInfo method = typeof(MethodFixture).GetMethod(nameof(MethodFixture.Update))!;

        string code = TypeScript.GetTypeScriptCode(method, @interface: false);

        Assert.Contains("public static Update(ref: { value: number }, out: { text: string }) {", code);
    }

    [Fact]
    public void MethodCode_EmitsParamsArrayAsTypeScriptRestParameter()
    {
        MethodInfo method = typeof(MethodFixture).GetMethod(nameof(MethodFixture.Collect))!;

        string code = TypeScript.GetTypeScriptCode(method, @interface: false);

        Assert.Contains("Collect(...values: number[])", code);
    }

    [Fact]
    public void MethodCode_DoesNotEmitUnmatchedCloseBraceForFinally()
    {
        MethodInfo method = typeof(MethodFixture).GetMethod(nameof(MethodFixture.RunWithFinally))!;

        string code = TypeScript.GetTypeScriptCode(method, @interface: false);

        Assert.DoesNotContain($"{Environment.NewLine}    }}{Environment.NewLine}", code);
    }

    [Fact]
    public void MethodCode_ConvertsFieldAddressesConstantsAndConditionalBranches()
    {
        string fieldAddressCode = TypeScript.GetTypeScriptCode(
            typeof(InstructionFixture).GetMethod(nameof(InstructionFixture.ReadVolatile))!,
            @interface: false);
        string constantCode = TypeScript.GetTypeScriptCode(
            typeof(InstructionFixture).GetMethod(nameof(InstructionFixture.SmallConstant))!,
            @interface: false);
        string branchCode = TypeScript.GetTypeScriptCode(
            typeof(InstructionFixture).GetMethod(nameof(InstructionFixture.NonNegative))!,
            @interface: false);

        Assert.Contains("Volatile.Read(InstructionFixture._counter)", fieldAddressCode);
        Assert.DoesNotContain("unsupported IL: ldsflda", fieldAddressCode);
        Assert.Contains("return 5;", constantCode);
        Assert.DoesNotContain("return null;", constantCode);
        Assert.DoesNotContain("unsupported IL: bge", branchCode);
        Assert.DoesNotContain("unsupported IL: blt", branchCode);
    }

    [Fact]
    public void MethodCode_ConvertsIntegerConstantsToBooleanCallArguments()
    {
        MethodInfo method = typeof(Console).GetMethod(nameof(Console.ReadKey), Type.EmptyTypes)!;

        string code = TypeScript.GetTypeScriptCode(method, @interface: false);

        Assert.Contains("ConsolePal.ReadKey(false)", code);
    }

    [Fact]
    public void MethodCode_ResolvesCoreLibraryResourceStrings()
    {
        MethodInfo setter = typeof(AppDomain).GetProperty(nameof(AppDomain.MonitoringIsEnabled))!.SetMethod!;

        string code = TypeScript.GetTypeScriptCode(setter, @interface: false);

        Assert.Contains("new ArgumentException(\"Argument must be true.\")", code);
        Assert.DoesNotContain("SR.", code);
    }

    [Fact]
    public void MethodCode_MapsGarbageCollectionCallsToJavaScriptSemantics()
    {
        MethodInfo keepAlive = typeof(GarbageCollectionFixture).GetMethod(nameof(GarbageCollectionFixture.KeepAlive))!;
        MethodInfo allocate = typeof(GarbageCollectionFixture).GetMethod(nameof(GarbageCollectionFixture.AllocateChars))!;
        MethodInfo memoryUsage = typeof(GarbageCollectionFixture).GetMethod(nameof(GarbageCollectionFixture.GetAllocatedBytes))!;
        MethodInfo appDomainMemory = typeof(AppDomain).GetProperty(nameof(AppDomain.MonitoringSurvivedProcessMemorySize))!.GetMethod!;
        MethodInfo appDomainAllocated = typeof(AppDomain).GetProperty(nameof(AppDomain.MonitoringTotalAllocatedMemorySize))!.GetMethod!;

        string keepAliveCode = TypeScript.GetTypeScriptCode(keepAlive, @interface: false);
        string allocateCode = TypeScript.GetTypeScriptCode(allocate, @interface: false);
        string memoryUsageCode = TypeScript.GetTypeScriptCode(memoryUsage, @interface: false);
        string appDomainMemoryCode = TypeScript.GetTypeScriptCode(appDomainMemory, @interface: false);
        string appDomainAllocatedCode = TypeScript.GetTypeScriptCode(appDomainAllocated, @interface: false);

        Assert.DoesNotContain("GC.", keepAliveCode);
        Assert.DoesNotContain("GC.KeepAlive(", keepAliveCode);
        Assert.Contains("Array.from({ length: length }, () => \"\\0\")", allocateCode);
        Assert.Contains("System.GC.GetTotalAllocatedBytes has no portable JavaScript equivalent.", memoryUsageCode);
        Assert.DoesNotContain("GC.GetTotalAllocatedBytes(", memoryUsageCode);
        Assert.Contains("System.GC.GetGCMemoryInfo has no portable JavaScript equivalent.", appDomainMemoryCode);
        Assert.DoesNotContain("GC.GetGCMemoryInfo(", appDomainMemoryCode);
        Assert.Contains("System.GC.GetTotalAllocatedBytes has no portable JavaScript equivalent.", appDomainAllocatedCode);
        Assert.DoesNotContain("GC.GetTotalAllocatedBytes(", appDomainAllocatedCode);
    }

    [Fact]
    public void MethodCode_ConvertsStringConcatToNativeTypeScriptConcatenation()
    {
        MethodInfo method = typeof(AppDomain).GetMethod(nameof(object.ToString), Type.EmptyTypes)!;
        MethodInfo concat = typeof(StringConcatFixture).GetMethod(nameof(StringConcatFixture.Concat))!;
        MethodInfo concatArray = typeof(StringConcatFixture).GetMethod(nameof(StringConcatFixture.ConcatArray))!;

        string code = TypeScript.GetTypeScriptCode(method, @interface: false);
        string concatCode = TypeScript.GetTypeScriptCode(concat, @interface: false);
        string concatArrayCode = TypeScript.GetTypeScriptCode(concatArray, @interface: false);

        Assert.Contains("return", code);
        Assert.Contains("this.FriendlyName", code);
        Assert.Contains("There are no context policies.", code);
        Assert.DoesNotContain("String.Concat", code);
        Assert.Contains("\"True\"", concatCode);
        Assert.DoesNotContain("String.Concat", concatCode);
        Assert.Contains("Array.from(values == null ? [] : values", concatArrayCode);
        Assert.DoesNotContain("String.Concat", concatArrayCode);
    }

    [Fact]
    public void MethodCode_EmitsLocalFunctionsInsideTheirContainingMethod()
    {
        string code = TypeScript.GetTypeScriptClass(typeof(LocalFunctionFixture), 0);

        Assert.Contains("function Increment(value: any) {", code);
        Assert.Contains("Increment(value)", code);
        Assert.DoesNotContain("__g__", code);
    }

    [Fact]
    public void MethodCode_TypesLocalsAndConvertsInterpolatedConsoleWrites()
    {
        MethodInfo method = typeof(InterpolatedConsoleFixture).GetMethod(nameof(InterpolatedConsoleFixture.Print))!;

        string code = TypeScript.GetTypeScriptCode(method, @interface: false);

        Assert.Contains("let loc0: number;", code);
        Assert.DoesNotContain("let loc1:", code);
        Assert.Contains("console.log(`", code);
        Assert.Contains("console.error(`I ate your ${String(loc0 ?? \"\")}`)", code);
        Assert.DoesNotContain("AppendFormatted", code);
        Assert.DoesNotContain("AppendLiteral", code);
        Assert.DoesNotContain("ToStringAndClear", code);
    }

    [Fact]
    public void MethodCode_ConvertsComplexInterpolatedStringsToNativeTemplates()
    {
        MethodInfo method = typeof(ComplexInterpolationFixture).GetMethod(nameof(ComplexInterpolationFixture.Format))!;

        string code = TypeScript.GetTypeScriptCode(method, @interface: false);

        Assert.Contains("return `Report [${String(marker ?? \"\")}] ${String(name ?? \"\")}", code);
        Assert.Contains("String(count ?? \"\").padStart(6)", code);
        Assert.Contains("String(ratio ?? \"\").padEnd(-(-8))", code);
        Assert.Contains("${(enabled ? \"True\" : \"False\")}", code);
        Assert.Contains("${String(optional ?? \"\")}", code);
        Assert.Contains("braces={ok}; template=\\`; literal=\\${name}`", code);
        Assert.DoesNotContain("DefaultInterpolatedStringHandler", code);
        Assert.DoesNotContain("AppendFormatted", code);
        Assert.DoesNotContain("ToStringAndClear", code);
    }

    [Fact]
    public void MethodCode_ConvertsBooleanLocalConstantsToBooleanLiterals()
    {
        MethodInfo method = typeof(BooleanLocalFixture).GetMethod(nameof(BooleanLocalFixture.ReturnTrue))!;

        string code = TypeScript.GetTypeScriptCode(method, @interface: false);

        Assert.Contains("let loc0: boolean;", code);
        Assert.Contains("let loc1: boolean;", code);
        Assert.Contains("loc0 = true;", code);
        Assert.Contains("loc1 = loc0;", code);
        Assert.Contains("return loc1;", code);
        Assert.DoesNotContain("loc0 = 1;", code);
    }

    [Fact]
    public void MethodCode_EmitsStructuredIfElseAndWhileBlocks()
    {
        MethodInfo choose = typeof(ControlFlowFixture).GetMethod(nameof(ControlFlowFixture.Choose))!;
        MethodInfo sum = typeof(ControlFlowFixture).GetMethod(nameof(ControlFlowFixture.Sum))!;
        MethodInfo toInt = typeof(ControlFlowFixture).GetMethod(nameof(ControlFlowFixture.ToInt))!;

        string chooseCode = TypeScript.GetTypeScriptCode(choose, @interface: false);
        string sumCode = TypeScript.GetTypeScriptCode(sum, @interface: false);
        string toIntCode = TypeScript.GetTypeScriptCode(toInt, @interface: false);

        Assert.Contains("if (loc1) {", chooseCode);
        Assert.Contains("} else {", chooseCode);
        Assert.Contains("return loc2;", chooseCode);
        Assert.Contains("while ((loc1 < count)) {", sumCode);
        Assert.Contains("return (condition ? 1 : 0);", toIntCode);
        Assert.DoesNotContain("branch IL_", chooseCode);
        Assert.DoesNotContain("branch IL_", sumCode);
        Assert.DoesNotContain("branch IL_", toIntCode);
    }

    [Fact]
    public void ClassCode_DeduplicatesEquivalentOverloadSignatures()
    {
        string code = TypeScript.GetTypeScriptClass(typeof(OverloadFormattingFixture), 0);

        Assert.Equal(1, CountOccurrences(code, "public Log(value: number): void;"));
        Assert.DoesNotContain("Method implementation exists", code);
    }

    [Fact]
    public void ClassCode_DispatchesOverloadsByArgumentCountAndType()
    {
        string code = TypeScript.GetTypeScriptClass(typeof(OverloadDispatchFixture), 0);

        Assert.Contains("public Select(): string;", code);
        Assert.Contains("public Select(enabled?: boolean): string;", code);
        Assert.Contains("public Select(value: number): string;", code);
        Assert.Contains("public Select(value: string): string;", code);
        Assert.Contains("if (__overloadArguments$.length === 0)", code);
        Assert.Contains("__overloadArguments$[0] === undefined || typeof __overloadArguments$[0] === \"boolean\"", code);
        Assert.Contains("typeof __overloadArguments$[0] === \"number\"", code);
        Assert.Contains("typeof __overloadArguments$[0] === \"string\"", code);
        Assert.Contains("return \"optional\";", code);
        Assert.Contains("return \"integer\";", code);
        Assert.Contains("return \"text\";", code);
        Assert.Contains("return \"empty\";", code);
        Assert.Contains("throw new TypeError(\"No overload of Select matches the provided arguments.\");", code);
        Assert.True(
            code.IndexOf("if (__overloadArguments$.length === 0)", StringComparison.Ordinal)
                < code.IndexOf("if (__overloadArguments$.length >= 0", StringComparison.Ordinal));
    }

    [Fact]
    public void ClassCode_DispatchesConstructorOverloadsByArgumentCountAndType()
    {
        string code = TypeScript.GetTypeScriptClass(typeof(ConstructorOverloadFixture), 0);

        Assert.Contains("public constructor();", code);
        Assert.Contains("public constructor(value: number);", code);
        Assert.Contains("public constructor(value: string);", code);
        Assert.Contains("constructor(...__overloadArguments$: any[])", code);
        Assert.Contains("if (__overloadArguments$.length === 0)", code);
        Assert.Contains("typeof __overloadArguments$[0] === \"number\"", code);
        Assert.Contains("typeof __overloadArguments$[0] === \"string\"", code);
        Assert.Contains("throw new TypeError(\"No matching overload for constructor.\");", code);
    }

    [Fact]
    public void ClassCode_DispatchesIndexerAndOperatorOverloadsAndEmitsEventAccessors()
    {
        string indexerCode = TypeScript.GetTypeScriptClass(typeof(OverloadedIndexerFixture), 0);
        Assert.Contains("public get_Item(index: number): string;", indexerCode);
        Assert.Contains("public get_Item(key: string): string;", indexerCode);
        Assert.Contains("typeof __overloadArguments$[0] === \"number\"", indexerCode);
        Assert.Contains("typeof __overloadArguments$[0] === \"string\"", indexerCode);
        Assert.DoesNotContain("get Item():", indexerCode);

        string operatorCode = TypeScript.GetTypeScriptClass(typeof(OperatorOverloadFixture), 0);
        Assert.Contains("public static op_Addition(left: OperatorOverloadFixture, right: number): OperatorOverloadFixture;", operatorCode);
        Assert.Contains("public static op_Addition(left: OperatorOverloadFixture, right: string): OperatorOverloadFixture;", operatorCode);
        Assert.Contains("typeof __overloadArguments$[1] === \"number\"", operatorCode);
        Assert.Contains("typeof __overloadArguments$[1] === \"string\"", operatorCode);

        string eventCode = TypeScript.GetTypeScriptClass(typeof(EventAccessorFixture), 0);
        Assert.Contains("public add_Changed(", eventCode);
        Assert.Contains("public remove_Changed(", eventCode);
    }

    [Fact]
    public void InterfaceMethodCode_ProducesSignatureWithoutImplementation()
    {
        MethodInfo method = typeof(IContract).GetMethod(nameof(IContract.Convert))!;

        string code = TypeScript.GetTypeScriptCode(method, @interface: true);

        Assert.Equal($"Convert(amount: number): string;{Environment.NewLine}", code);
    }

    [Fact]
    public void FieldCode_HandlesVisibilityStaticReadonlyAndConstants()
    {
        Type fixture = typeof(FieldFixture);

        Assert.Equal("public Count!: number;", TypeScript.GetTypeScriptCode(fixture.GetField(nameof(FieldFixture.Count))!));
        Assert.Equal("private _name!: string;", TypeScript.GetTypeScriptCode(fixture.GetField("_name", BindingFlags.Instance | BindingFlags.NonPublic)!));
        Assert.Equal("public static readonly Label: string;", TypeScript.GetTypeScriptCode(fixture.GetField(nameof(FieldFixture.Label))!));
        Assert.Equal("public static readonly DefaultCount: number;", TypeScript.GetTypeScriptCode(fixture.GetField(nameof(FieldFixture.DefaultCount))!));
        Assert.Equal("public readonly ReadonlyCount!: number;", TypeScript.GetTypeScriptCode(fixture.GetField(nameof(FieldFixture.ReadonlyCount))!));
    }

    [Fact]
    public void PropertyCode_EmitsGetterAndSetterBodies()
    {
        PropertyInfo property = typeof(PropertyFixture).GetProperty(nameof(PropertyFixture.Value))!;

        string code = TypeScript.GetTypeScriptCode(property);

        Assert.Contains("public get Value(): number {", code);
        Assert.Contains("public set Value(value: number) {", code);
        Assert.Contains("return ", code);
        Assert.Contains(".Value = ", code);
    }

    [Fact]
    public void InterfaceCode_EmitsPropertyAccessorsAndMethodSignature()
    {
        string code = TypeScript.GetTypeScriptInterface(typeof(IContract), 0);

        Assert.StartsWith("export interface IContract {", code);
        Assert.Contains("get Name(): string;", code);
        Assert.Contains("set Name(value: string);", code);
        Assert.Contains("Convert(amount: number): string;", code);
        Assert.EndsWith("}", code.TrimEnd());
    }

    [Fact]
    public void InterfaceCode_UsesExtendsForInheritedInterfaces()
    {
        string code = TypeScript.GetTypeScriptCode(typeof(IExtendedContract), @namespace: false, r: 0);

        Assert.Contains("export interface IExtendedContract extends IContract", code);
        Assert.DoesNotContain("implements IContract", code);
    }

    [Fact]
    public void MethodCode_MapsNullableAndCommonGenericTypes()
    {
        MethodInfo method = typeof(MappedTypesFixture).GetMethod(nameof(MappedTypesFixture.Map))!;

        string code = TypeScript.GetTypeScriptCode(method, @interface: false);

        Assert.Contains("value: (number | null)", code);
        Assert.Contains("values: number[]", code);
        Assert.Contains("lookup: Map<string, number>", code);
        Assert.Contains("result: Promise<string>", code);
    }

    [Fact]
    public void FlagsEnumCode_FormatsNegativeValuesAsNegativeHex()
    {
        string code = TypeScript.GetTypeScriptEnum(typeof(SignedAccess), 0);

        Assert.Contains("All = -0x1,", code);
    }

    [Fact]
    public void ConstructorCode_UsesVisibilityAndConvertedParameterTypes()
    {
        ConstructorInfo constructor = typeof(ConstructorFixture).GetConstructor([typeof(int)])!;

        string code = TypeScript.GetTypeScriptCode(constructor);

        Assert.Contains("public constructor(initialCount: number) {", code);
        Assert.DoesNotContain("throw new Error(\"not yet implemented\");", code);
    }

    [Fact]
    public void StaticConstructorCode_UsesInitializationWrapper()
    {
        ConstructorInfo constructor = typeof(StaticConstructorFixture)
            .GetConstructor(BindingFlags.Static | BindingFlags.NonPublic, binder: null, Type.EmptyTypes, modifiers: null)!;

        string code = TypeScript.GetTypeScriptCode(constructor);

        Assert.Contains("private static staticctor_initialize = (() => {", code);
        Assert.DoesNotContain("private constructor()", code);
    }

    [Fact]
    public void ClassCode_EmitsClassMembersAndIndentation()
    {
        string code = TypeScript.GetTypeScriptClass(typeof(ClassFixture), 1);

        Assert.StartsWith("    export class ClassFixture {", code);
        Assert.Contains("    public Count!: number;", code);
        Assert.Contains("    public get Value(): number {", code);
        Assert.Contains("    public Read(): number {", code);
        Assert.EndsWith("}", code.TrimEnd());
    }

    [Fact]
    public void EnumCode_FormatsOrdinaryAndFlagsValues()
    {
        string ordinary = TypeScript.GetTypeScriptEnum(typeof(Status), 0);
        string flags = TypeScript.GetTypeScriptEnum(typeof(Access), 0);

        Assert.Contains("export enum Status {", ordinary);
        Assert.Contains("Ready = 0,", ordinary);
        Assert.Contains("Complete = 1,", ordinary);
        Assert.Contains("Read = 0x1,", flags);
        Assert.Contains("Write = 0x2,", flags);
    }

    [Fact]
    public void TypeCode_ClosesEnumAndIncludesMembers()
    {
        string code = TypeScript.GetTypeScriptCode(typeof(Status), @namespace: false, r: 0);

        Assert.Contains("export enum Status {", code);
        Assert.Contains("Ready = 0,", code);
        Assert.EndsWith("}", code.TrimEnd());
    }

    [Fact]
    public void TypeCode_CanWrapDeclarationsInTheirNamespace()
    {
        string code = TypeScript.GetTypeScriptCode(typeof(Status), @namespace: true, r: 0);

        Assert.StartsWith("export namespace CSToTS {", code);
        Assert.Contains("    export namespace Tests {", code);
        Assert.Contains("        export enum Status {", code);
        Assert.EndsWith("}", code.TrimEnd());
    }

    [Fact]
    public void GetFullTypeScriptCode_WritesDiscoveredTypeToModules()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "CSToTS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            Environment.CurrentDirectory = temporaryDirectory;

            TypeScript.GetFullTypeScriptCode(typeof(GenerationRoot));
            TypeScript.GetFullTypeScriptCode(typeof(GenerationRoot));

            string generatedFile = Path.Combine(temporaryDirectory, "modules", "CSToTS", "Tests", "GenerationRoot.ts");
            string code = File.ReadAllText(generatedFile);
            Assert.Contains("export class GenerationRoot", code);
            Assert.Contains("public Value!: number;", code);
            Assert.DoesNotContain("export namespace CSToTS", code);
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void GetFullTypeScriptCode_UsesValidFileNamesForGenericTypes()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "CSToTS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            Environment.CurrentDirectory = temporaryDirectory;
            string typeDirectory = Path.Combine(temporaryDirectory, "modules", "CSToTS", "Tests");
            Directory.CreateDirectory(typeDirectory);
            string staleFile = Path.Combine(typeDirectory, "GenericArityFixture`1.ts");
            File.WriteAllText(staleFile, "stale generated file");

            TypeScript.GetFullTypeScriptCode(typeof(GenericArityFixture<>));

            string generatedFile = Path.Combine(typeDirectory, "GenericArityFixture_1.ts");
            Assert.True(File.Exists(generatedFile));
            Assert.False(File.Exists(staleFile));
            Assert.Contains("export class GenericArityFixture<T>", File.ReadAllText(generatedFile));
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void GetFullTypeScriptCode_DiscoversTypesReferencedOnlyByMethodBodies()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "CSToTS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            Environment.CurrentDirectory = temporaryDirectory;

            TypeScript.GetFullTypeScriptCode(typeof(MethodBodyTypesRoot));

            string referencedTypePath = Path.Combine(
                temporaryDirectory,
                "modules",
                "CSToTS",
                "Tests",
                "MethodBodyOnlyFixture.ts");
            Assert.True(File.Exists(referencedTypePath));
            Assert.Contains("export class MethodBodyOnlyFixture", File.ReadAllText(referencedTypePath));
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void GetFullTypeScriptCode_MapsConsoleReadLineAndWriteLineToNode()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "CSToTS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            Environment.CurrentDirectory = temporaryDirectory;

            TypeScript.GetFullTypeScriptCode(typeof(ConsoleMethodRoot));

            string rootPath = Path.Combine(
                temporaryDirectory,
                "modules",
                "CSToTS",
                "Tests",
                "ConsoleMethodRoot.ts");
            string rootCode = File.ReadAllText(rootPath);

            Assert.Contains("import { readSync } from \"node:fs\";", rootCode);
            Assert.Contains("import { TextDecoder } from \"node:util\";", rootCode);
            Assert.Contains("readSync(0, one, 0, 1, null)", rootCode);
            Assert.Contains("ReadLine(): string | null", rootCode);
            Assert.Contains("WriteLine(value: string | null)", rootCode);
            Assert.Contains("console.log(value)", rootCode);
            Assert.False(File.Exists(Path.Combine(temporaryDirectory, "modules", "System", "Console.ts")));
            Assert.False(File.Exists(Path.Combine(temporaryDirectory, "modules", "System", "ConsolePal.ts")));
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void GetFullTypeScriptCode_ExcludesCompilerImplementationDetails()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "CSToTS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            Environment.CurrentDirectory = temporaryDirectory;
            string modulesDirectory = Path.Combine(temporaryDirectory, "modules");
            Directory.CreateDirectory(modulesDirectory);
            string staleArtifactPath = Path.Combine(modulesDirectory, "__StaticArrayInitTypeSize=12.ts");
            File.WriteAllText(staleArtifactPath, "stale compiler artifact");

            TypeScript.GetFullTypeScriptCode(typeof(Console));

            string[] generatedFiles = Directory.GetFiles(modulesDirectory, "*.ts", SearchOption.AllDirectories);
            Assert.Contains(generatedFiles, path => Path.GetFileName(path) == "Console.ts");
            Assert.False(File.Exists(staleArtifactPath));
            Assert.DoesNotContain(generatedFiles, path =>
                Path.GetFileName(path).StartsWith("__StaticArrayInitTypeSize=", StringComparison.Ordinal)
                || Path.GetFileName(path).Contains("PrivateImplementationDetails", StringComparison.Ordinal)
                || Path.GetFileName(path).StartsWith("<", StringComparison.Ordinal));
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void GetFullTypeScriptCode_IncludesWhitelistedSystemTypesReferencedByMethodBodies()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "CSToTS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            Environment.CurrentDirectory = temporaryDirectory;

            TypeScript.GetFullTypeScriptCode(typeof(WhitelistedSystemTypesRoot));

            Assert.True(File.Exists(Path.Combine(temporaryDirectory, "modules", "System", "Text", "Encoding.ts")));
            Assert.True(File.Exists(Path.Combine(temporaryDirectory, "modules", "System", "Threading", "Volatile.ts")));
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void GetFullTypeScriptCode_SupportsBothFileLayoutsAndNamespaceSettings()
    {
        foreach (TypeScriptOutputLayout layout in Enum.GetValues<TypeScriptOutputLayout>())
        {
            AssertOutputOptions(layout, emitNamespaces: false);
            AssertOutputOptions(layout, emitNamespaces: true);
        }
    }

    [Fact]
    public void GetFullTypeScriptCode_ConvertsFixedBuffersWithoutEmittingCompilerGeneratedTypes()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "CSToTS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            Environment.CurrentDirectory = temporaryDirectory;

            TypeScript.GetFullTypeScriptCode(typeof(FixedBufferFixture));

            string modulesDirectory = Path.Combine(temporaryDirectory, "modules", "CSToTS", "Tests");
            string[] generatedFiles = Directory.GetFiles(modulesDirectory, "*.ts", SearchOption.AllDirectories);
            string code = File.ReadAllText(Path.Combine(modulesDirectory, "FixedBufferFixture.ts"));

            Assert.Single(generatedFiles);
            Assert.Contains("public Values!: number[];", code);
            Assert.DoesNotContain("e__FixedBuffer", code);
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public void GetFullTypeScriptCode_WritesSameNamedTypesFromDifferentNamespaces()
    {
        string previousDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "CSToTS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            Environment.CurrentDirectory = temporaryDirectory;

            TypeScript.GetFullTypeScriptCode(typeof(DuplicateTypesRoot));

            Assert.True(File.Exists(Path.Combine(temporaryDirectory, "modules", "CSToTS", "Tests", "FixtureOne", "Twin.ts")));
            Assert.True(File.Exists(Path.Combine(temporaryDirectory, "modules", "CSToTS", "Tests", "FixtureTwo", "Twin.ts")));
            Assert.True(File.Exists(Path.Combine(temporaryDirectory, "modules", "CSToTS", "Tests", "ReferencedFixture.ts")));
            Assert.True(File.Exists(Path.Combine(temporaryDirectory, "modules", "CSToTS", "Tests", "RecursiveFixture.ts")));
            Assert.True(File.Exists(Path.Combine(temporaryDirectory, "modules", "CSToTS", "Tests", "Status.ts")));
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;

        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static void AssertOutputOptions(TypeScriptOutputLayout layout, bool emitNamespaces)
    {
        string previousDirectory = Environment.CurrentDirectory;
        string temporaryDirectory = Path.Combine(Path.GetTempPath(), "CSToTS.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);

        try
        {
            Environment.CurrentDirectory = temporaryDirectory;
            TypeScript.GetFullTypeScriptCode(typeof(GenerationRoot), new TypeScriptOutputOptions
            {
                FileLayout = layout,
                EmitNamespaces = emitNamespaces
            });

            string generatedFile = layout == TypeScriptOutputLayout.NamespaceDirectories
                ? Path.Combine(temporaryDirectory, "modules", "CSToTS", "Tests", "GenerationRoot.ts")
                : Path.Combine(temporaryDirectory, "modules", "GenerationRoot.ts");
            string otherLayoutFile = layout == TypeScriptOutputLayout.NamespaceDirectories
                ? Path.Combine(temporaryDirectory, "modules", "GenerationRoot.ts")
                : Path.Combine(temporaryDirectory, "modules", "CSToTS", "Tests", "GenerationRoot.ts");

            Assert.True(File.Exists(generatedFile));
            Assert.False(File.Exists(otherLayoutFile));

            string code = File.ReadAllText(generatedFile);
            Assert.Contains("export class GenerationRoot", code);
            Assert.Equal(emitNamespaces, code.Contains("export namespace CSToTS {", StringComparison.Ordinal));
            Assert.Equal(emitNamespaces, code.Contains("export namespace Tests {", StringComparison.Ordinal));
        }
        finally
        {
            Environment.CurrentDirectory = previousDirectory;
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private class MethodFixture
    {
        public string[] Accept(List<int[]> values) => [];

        public void Collect(params int[] values)
        {
        }

        public int RunWithFinally(int value)
        {
            try
            {
                return value;
            }
            finally
            {
                Touch();
            }
        }

        private static void Touch()
        {
        }

        public static void Update(ref int value, out string text)
        {
            value = 0;
            text = string.Empty;
        }
    }

    private class SpanConversionFixture
    {
        private static string ConvertSpan(Span<char> value) => new((ReadOnlySpan<char>)value);

        private static List<int> CreateList() => new();
    }

    private class ListConversionFixture
    {
        private static int[] Create(int item)
        {
            List<int> values = new();
            values.Add(item);
            return values.ToArray();
        }

        private static int GetCount(List<int> values) => values.Count;
    }

    private class NullConditionalCallFixture
    {
        private static void DisposeIfPresent(IDisposable? resource) => resource?.Dispose();
    }

    private class GenericArityFixture<T>
    {
        public T? Value { get; set; }
    }

    private class InstructionFixture
    {
        private static int _counter = 0;

        public static int ReadVolatile() => System.Threading.Volatile.Read(ref _counter);

        public static int SmallConstant() => 5;

        public static int NonNegative(int value)
        {
            if (value < 0)
                return 0;
            return value;
        }

    }

    private static class GarbageCollectionFixture
    {
        public static void KeepAlive(object value) => GC.KeepAlive(value);

        public static char[] AllocateChars(int length) => GC.AllocateUninitializedArray<char>(length);

        public static long GetAllocatedBytes() => GC.GetTotalAllocatedBytes(false);
    }

    private class StringConcatFixture
    {
        public string Concat(string? name, bool enabled) => string.Concat("Name:", name, "\r\n", enabled);

        public string ConcatArray(string?[] values) => string.Concat(values);
    }

    private class OverloadFormattingFixture
    {
        public void Log(int value)
        {
        }

        public void Log(long value)
        {
        }
    }

    private class OverloadDispatchFixture
    {
        public string Select(int value) => "integer";

        public string Select(string value) => "text";

        public string Select() => "empty";

        public string Select(bool enabled = true) => "optional";
    }

    private class ConstructorOverloadFixture
    {
        public ConstructorOverloadFixture()
        {
        }

        public ConstructorOverloadFixture(int value)
        {
        }

        public ConstructorOverloadFixture(string value)
        {
        }
    }

    private class OverloadedIndexerFixture
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

    private class OperatorOverloadFixture
    {
        public static OperatorOverloadFixture operator +(OperatorOverloadFixture left, int right) => left;

        public static OperatorOverloadFixture operator +(OperatorOverloadFixture left, string right) => left;
    }

    private class EventAccessorFixture
    {
        public event Action? Changed
        {
            add { }
            remove { }
        }
    }

    private interface IContract
    {
        string Name { get; set; }

        string Convert(int amount);
    }

    private interface IExtendedContract : IContract
    {
        bool IsValid();
    }

    private class MappedTypesFixture
    {
        public void Map(int? value, List<int> values, Dictionary<string, int> lookup, Task<string> result)
        {
        }
    }

    private class FieldFixture
    {
        public int Count = 0;
        private string _name = string.Empty;
        public static readonly string Label = "label";
        public const int DefaultCount = 1;
        public readonly int ReadonlyCount = 0;
    }

    private class PropertyFixture
    {
        public int Value { get; set; }
    }

    private class ConstructorFixture
    {
        public ConstructorFixture(int initialCount)
        {
        }
    }

    private static class StaticConstructorFixture
    {
        static StaticConstructorFixture()
        {
        }
    }

    private class ClassFixture
    {
        public int Count = 0;
        public int Value { get; set; }

        public int Read() => Value;
    }

    private enum Status
    {
        Ready,
        Complete
    }

    [Flags]
    private enum Access
    {
        Read = 1,
        Write = 2
    }

    [Flags]
    private enum SignedAccess : long
    {
        All = -1
    }

    private class GenerationRoot
    {
        public int Value = 0;
    }

    private class MethodBodyTypesRoot
    {
        public int CreateAndRead() => new MethodBodyOnlyFixture().Value;
    }

    private class MethodBodyOnlyFixture
    {
        public int Value { get; set; }
    }

    private class ConsoleMethodRoot
    {
        public string? ReadLine() => Console.ReadLine();

        public void WriteLine(string? value) => Console.WriteLine(value);
    }

    private static class InterpolatedConsoleFixture
    {
        public static void Print()
        {
            int value = 1;
            Console.WriteLine($"{value}");
            Console.Error.WriteLine($"I ate your {value}");
        }
    }

    private static class ComplexInterpolationFixture
    {
        public static string Format(string name, int count, double ratio, bool enabled, char marker, int? optional) =>
            $"Report [{marker}] {name}: count={count,6}; ratio={ratio,-8}; enabled={enabled}; optional={optional}; braces={{ok}}; template=`; literal=${{name}}";
    }

    private static class BooleanLocalFixture
    {
        public static bool ReturnTrue()
        {
            bool enabled = true;
            return enabled;
        }
    }

    private static class ControlFlowFixture
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

    private class LocalFunctionFixture
    {
        public int Calculate(int value)
        {
            static int Increment(int value) => value + 1;
            return Increment(value);
        }
    }

    private class WhitelistedSystemTypesRoot
    {
        public int Use(ref int value) =>
            System.Text.Encoding.UTF8.GetByteCount(string.Empty) + System.Threading.Volatile.Read(ref value);
    }

    private unsafe struct FixedBufferFixture
    {
        public fixed int Values[4];
    }

    private class DuplicateTypesRoot
    {
        public FixtureOne.Twin First = null!;
        public FixtureTwo.Twin Second = null!;
        public List<ReferencedFixture> Values = [];
        public RecursiveFixture Recursive = null!;
        public Status State = Status.Ready;
    }

    private class ReferencedFixture
    {
        public int Value = 0;
    }

    private class RecursiveFixture
    {
        public RecursiveFixture? Next = null;
    }
}
