using System.Reflection;
using System.Runtime.CompilerServices;

namespace CSToTS;

public static partial class TypeScript
{
    private static bool IsBuiltInTypeScript(Type type) => PrimitiveTypes.ContainsKey(type);

    private static bool IsCompilerImplementationDetail(Type type)
    {
        for (Type? current = type; current != null; current = current.DeclaringType)
        {
            if (current.Name == "<PrivateImplementationDetails>"
                || current.Name.StartsWith("__StaticArrayInitTypeSize=", StringComparison.Ordinal)
                || current.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false))
                return true;
        }
        return false;
    }

    private static void WalkingTypesFinder(Type type, HashSet<Type> set, HashSet<Type> visited)
    {
        if (IsCompilerImplementationDetail(type))
            return;

        if (type.IsGenericType)
        {
            foreach (Type genericArgument in type.GetGenericArguments())
                WalkingTypesFinder(genericArgument, set, visited);
            type = type.GetGenericTypeDefinition();
        }

        if (type == typeof(Enum) || type == typeof(ValueType) || type.IsGenericParameter || IsBuiltInTypeScript(type)
            || type == typeof(Nullable<>) || GenericTypeNames.ContainsKey(type) || !visited.Add(type))
            return;
        if (MethodBodySystemTypeExceptions.Contains(type) || type.IsEnum)
        {
            set.Add(type);
            return;
        }

        if (type.IsArray || type.IsPointer || type.IsByRef)
        {
            WalkingTypesFinder(type.GetElementType()!, set, visited);
            return;
        }

        TypeInfo typeInfo = type.GetTypeInfo();
        if (type.BaseType != null)
            WalkingTypesFinder(type.BaseType, set, visited);
        foreach (Type interfaceType in typeInfo.ImplementedInterfaces)
            WalkingTypesFinder(interfaceType, set, visited);
        foreach (FieldInfo field in typeInfo.DeclaredFields)
            WalkingTypesFinder(GetFieldType(field), set, visited);
        foreach (PropertyInfo property in typeInfo.DeclaredProperties)
            WalkingTypesFinder(property.PropertyType, set, visited);

        foreach (ConstructorInfo constructor in typeInfo.DeclaredConstructors)
        {
            foreach (ParameterInfo parameter in constructor.GetParameters())
                WalkingTypesFinder(parameter.ParameterType, set, visited);
            WalkMethodBodyTypes(constructor, set, visited);
        }

        foreach (MethodInfo method in typeInfo.DeclaredMethods)
        {
            WalkingTypesFinder(method.ReturnType, set, visited);
            foreach (ParameterInfo parameter in method.GetParameters())
                WalkingTypesFinder(parameter.ParameterType, set, visited);
            WalkMethodBodyTypes(method, set, visited);
        }

        set.Add(type);
    }

    private static void WalkMethodBodyTypes(MethodBase method, HashSet<Type> set, HashSet<Type> visited)
    {
        foreach (Type referencedType in MethodExtractor.GetReferencedTypes(method))
            WalkReferencedType(referencedType, set, visited);
    }

    private static void WalkReferencedType(Type referencedType, HashSet<Type> set, HashSet<Type> visited)
    {
        if (IsCompilerImplementationDetail(referencedType))
            return;

        if (referencedType.IsByRef || referencedType.IsPointer || referencedType.IsArray)
        {
            WalkReferencedType(referencedType.GetElementType()!, set, visited);
            return;
        }

        Type type = referencedType;
        if (type.IsGenericType)
        {
            foreach (Type genericArgument in type.GetGenericArguments())
                WalkReferencedType(genericArgument, set, visited);
            type = type.GetGenericTypeDefinition();
        }

        if ((type.Namespace == "System" || type.Namespace?.StartsWith("System.", StringComparison.Ordinal) == true)
            && !MethodBodySystemTypeExceptions.Contains(type))
            return;

        WalkingTypesFinder(referencedType, set, visited);
    }
}
