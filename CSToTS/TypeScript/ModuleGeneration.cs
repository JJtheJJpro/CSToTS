using System.Reflection;
using System.Text;
using CSToTS.Abstractions;

namespace CSToTS;

public static partial class TypeScript
{
    public static void GetFullTypeScriptCode(Type type)
    {
        GetFullTypeScriptCode(type, new TypeScriptOutputOptions());
    }

    public static void GetFullTypeScriptCode(Type type, TypeScriptOutputOptions options)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(options);
        if (!Enum.IsDefined(typeof(TypeScriptOutputLayout), options.FileLayout))
        {
            throw new ArgumentOutOfRangeException(nameof(options.FileLayout), options.FileLayout, "Unknown TypeScript output layout.");
        }

        HashSet<Type> mainSet = [];
        HashSet<Type> visited = [];
        WalkingTypesFinder(type, mainSet, visited);

        foreach (Type discoveredType in mainSet)
        {
            string outputPath = GetTypeOutputPath(discoveredType, options.FileLayout);
            string outputDirectory = Path.GetDirectoryName(outputPath)!;
            Directory.CreateDirectory(outputDirectory);
            string imports = GetTypeScriptImports(discoveredType, mainSet, options);
            string code = GetTypeScriptCode(discoveredType, options.EmitNamespaces, 0);
            File.WriteAllText(outputPath, $"{imports}{code}{Environment.NewLine}");

            string legacyGenericPath = Path.Combine(outputDirectory, $"{discoveredType.Name}.ts");
            if (!string.Equals(legacyGenericPath, outputPath, StringComparison.Ordinal)
                && File.Exists(legacyGenericPath))
                File.Delete(legacyGenericPath);
        }

        RemoveCompilerImplementationDetailOutputs();
    }

    private static void RemoveCompilerImplementationDetailOutputs()
    {
        const string modulesDirectory = "modules";
        if (!Directory.Exists(modulesDirectory))
            return;

        foreach (string filePath in Directory.EnumerateFiles(modulesDirectory, "*.ts", SearchOption.AllDirectories))
        {
            string fileName = Path.GetFileName(filePath);
            if (fileName.StartsWith("__StaticArrayInitTypeSize=", StringComparison.Ordinal)
                || fileName == "<PrivateImplementationDetails>.ts")
                File.Delete(filePath);
        }
    }

    private static string GetTypeOutputPath(Type type, TypeScriptOutputLayout layout)
    {
        string outputDirectory = layout == TypeScriptOutputLayout.NamespaceDirectories && type.Namespace != null
            ? Path.Combine("modules", type.Namespace.Replace('.', Path.DirectorySeparatorChar))
            : "modules";
        string fileName = FixInvalidName(type.Name).Replace('`', '_');
        return Path.Combine(outputDirectory, $"{fileName}.ts");
    }

    private static string GetTypeScriptImports(Type type, HashSet<Type> allTypes, TypeScriptOutputOptions options)
    {
        HashSet<Type> references = [];
        HashSet<Type> declaredInFile = [];
        CollectTypeReferences(type, references, declaredInFile);

        StringBuilder imports = new();
        if (UsesConsoleReadLine(type))
        {
            imports.AppendLine("import { readSync } from \"node:fs\";");
            imports.AppendLine("import { TextDecoder } from \"node:util\";");
        }

        int dependencyIndex = 0;
        foreach (Type dependency in references
            .Where(candidate => allTypes.Contains(candidate) && !declaredInFile.Contains(candidate))
            .OrderBy(candidate => candidate.Namespace, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.Name, StringComparer.Ordinal))
        {
            string importPath = Path.GetRelativePath(
                Path.GetDirectoryName(GetTypeOutputPath(type, options.FileLayout))!,
                GetTypeOutputPath(dependency, options.FileLayout));
            importPath = Path.ChangeExtension(importPath, null)!.Replace('\\', '/');
            if (!importPath.StartsWith('.'))
                importPath = $"./{importPath}";

            string name = GetConvertedName(dependency, true);
            if (!options.EmitNamespaces)
            {
                imports.AppendLine($"import {{ {name} }} from \"{importPath}\";");
                continue;
            }

            string moduleAlias = $"__dependency{dependencyIndex++}";
            imports.AppendLine($"import * as {moduleAlias} from \"{importPath}\";");
            string qualifiedName = moduleAlias;
            if (dependency.Namespace != null)
                qualifiedName += "." + string.Join(".", dependency.Namespace.Split('.').Select(FixInvalidName));
            qualifiedName += $".{name}";

            string genericParameters = dependency.IsGenericTypeDefinition
                ? $"<{string.Join(", ", dependency.GetGenericArguments().Select(argument => FixInvalidName(argument.Name)))}>"
                : "";
            imports.AppendLine($"type {name}{genericParameters} = {qualifiedName}{genericParameters};");
            if (dependency.IsClass || dependency.IsValueType)
                imports.AppendLine($"const {name} = {qualifiedName};");
        }

        if (imports.Length > 0)
            imports.AppendLine();
        return imports.ToString();
    }

    private static bool UsesConsoleReadLine(Type type)
    {
        TypeInfo typeInfo = type.GetTypeInfo();
        return typeInfo.DeclaredConstructors
                .Cast<MethodBase>()
                .Concat(typeInfo.DeclaredMethods)
                .Any(method => MethodExtractor.GetReferencedMethods(method).Any(SystemConsole.IsReadLine))
            || typeInfo.DeclaredNestedTypes.Any(nestedType => UsesConsoleReadLine(nestedType));
    }

    private static void CollectTypeReferences(Type type, HashSet<Type> references, HashSet<Type> declaredInFile)
    {
        if (!declaredInFile.Add(type))
            return;

        if (HasTypeScriptBaseClass(type))
            AddTypeReference(type.BaseType!, references);
        foreach (Type interfaceType in type.GetInterfaces())
            AddTypeReference(interfaceType, references);
        foreach (FieldInfo field in type.GetTypeInfo().DeclaredFields)
            AddTypeReference(GetFieldType(field), references);
        foreach (PropertyInfo property in type.GetTypeInfo().DeclaredProperties)
            AddTypeReference(property.PropertyType, references);
        foreach (ConstructorInfo constructor in type.GetTypeInfo().DeclaredConstructors)
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
                AddTypeReference(parameter.ParameterType, references);
            foreach (Type referencedType in MethodExtractor.GetReferencedTypes(constructor))
                AddTypeReference(referencedType, references);
        }
        foreach (MethodInfo method in type.GetTypeInfo().DeclaredMethods)
        {
            AddTypeReference(method.ReturnType, references);
            foreach (ParameterInfo parameter in method.GetParameters())
                AddTypeReference(parameter.ParameterType, references);
            foreach (Type referencedType in MethodExtractor.GetReferencedTypes(method))
                AddTypeReference(referencedType, references);
        }
        foreach (TypeInfo nestedType in type.GetTypeInfo().DeclaredNestedTypes)
            CollectTypeReferences(nestedType, references, declaredInFile);
    }

    private static void AddTypeReference(Type type, HashSet<Type> references)
    {
        if (type.IsByRef || type.IsArray || type.IsPointer)
        {
            AddTypeReference(type.GetElementType()!, references);
            return;
        }

        if (type.IsGenericType)
        {
            foreach (Type genericArgument in type.GetGenericArguments())
                AddTypeReference(genericArgument, references);

            Type genericDefinition = type.GetGenericTypeDefinition();
            if (genericDefinition == typeof(Nullable<>) || GenericTypeNames.ContainsKey(genericDefinition))
                return;
            type = genericDefinition;
        }

        if (type.IsGenericParameter || type == typeof(Enum) || type == typeof(ValueType) || IsBuiltInTypeScript(type))
            return;
        references.Add(type);
    }
}
