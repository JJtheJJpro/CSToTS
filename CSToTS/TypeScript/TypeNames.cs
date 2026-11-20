using System.Reflection;

namespace CSToTS;

public static partial class TypeScript
{
    internal static string GetConvertedName(Type type, bool @namespace = false)
    {
        if (@namespace)
        {
            string name = IsBuiltInTypeScript(type) ? PrimitiveTypes[type] : FixInvalidName(type.Name);
            return name.Contains('`') ? name[..name.IndexOf('`')] : name;
        }

        string template = "%";
        while (type.IsByRef || type.IsArray || type.IsPointer)
        {
            if (type.IsByRef)
            {
                type = type.GetElementType()!;
            }
            else if (type.IsArray)
            {
                template = template.Replace("%", "%[]");
                type = type.GetElementType()!;
            }
            else
            {
                template = template.Replace("%", "{ ptr: % }");
                type = type.GetElementType()!;
            }
        }

        if (Nullable.GetUnderlyingType(type) is Type nullableType)
            return template.Replace("%", $"({GetConvertedName(nullableType)} | null)");

        string typeName;
        if (type.IsGenericType && type.Name.Split('`').Length > 1)
        {
            TypeInfo typeInfo = type.GetTypeInfo();
            Type[] genericArguments = typeInfo.GenericTypeArguments.Length != 0
                ? typeInfo.GenericTypeArguments
                : typeInfo.GenericTypeParameters;
            Type genericDefinition = typeInfo.IsGenericTypeDefinition ? type : type.GetGenericTypeDefinition();
            if (GenericTypeNames.TryGetValue(genericDefinition, out string? mappedName))
            {
                string[] convertedArguments = [.. genericArguments.Select(argument => GetConvertedName(argument))];
                typeName = mappedName == "[]"
                    ? $"{convertedArguments[0]}[]"
                    : $"{mappedName}<{string.Join(", ", convertedArguments)}>";
            }
            else
            {
                string genericTypeName = typeInfo.Name.Split('`')[0] + "<";
                foreach (TypeInfo argumentInfo in genericArguments.Select(argument => argument.GetTypeInfo()))
                    genericTypeName += GetConvertedName(argumentInfo) + ", ";
                typeName = genericTypeName[..^2] + ">";
            }
        }
        else
        {
            typeName = IsBuiltInTypeScript(type) ? PrimitiveTypes[type] : FixInvalidName(type.Name);
        }

        return template.Replace("%", typeName);
    }

    internal static string GetLocalVariableTypeName(Type type)
    {
        string convertedName = GetConvertedName(type);
        Type genericDefinition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        bool isMappedSystemType = IsBuiltInTypeScript(type)
            || Nullable.GetUnderlyingType(type) != null
            || GenericTypeNames.ContainsKey(genericDefinition)
            || type.IsEnum;
        if (type.Assembly == typeof(object).Assembly && !isMappedSystemType)
            return $"any /* {GetConvertedName(type)} */";
        return convertedName;
    }
}
