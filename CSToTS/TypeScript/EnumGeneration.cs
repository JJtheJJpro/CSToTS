using System.Globalization;
using System.Reflection;
using System.Text;

namespace CSToTS;

public static partial class TypeScript
{
    public static string GetTypeScriptEnum(Type type, int r)
    {
        TypeInfo typeInfo = type.GetTypeInfo();
        StringBuilder builder = new();
        builder.AppendLine($"{Space(r, 0)}export enum {GetConvertedName(type)} {{");

        foreach ((string name, object value) in Enum.GetNames(type).Zip(Enum.GetValuesAsUnderlyingType(type).Cast<object>()))
        {
            string formattedValue = typeInfo.CustomAttributes.Any(attribute => attribute.AttributeType == typeof(FlagsAttribute))
                ? FormatEnumHexValue(type, value)
                : Convert.ToString(value, CultureInfo.InvariantCulture)!;
            builder.AppendLine($"{Space(r, 1)}{FixInvalidName(name)} = {formattedValue},");
        }

        builder.AppendLine($"{Space(r, 0)}}}");
        return builder.ToString();
    }

    private static string FormatEnumHexValue(Type enumType, object value)
    {
        Type underlyingType = Enum.GetUnderlyingType(enumType);
        bool isUnsigned = underlyingType == typeof(byte) || underlyingType == typeof(ushort)
            || underlyingType == typeof(uint) || underlyingType == typeof(ulong);

        if (isUnsigned)
            return $"0x{Convert.ToUInt64(value, CultureInfo.InvariantCulture):x}";

        long signedValue = Convert.ToInt64(value, CultureInfo.InvariantCulture);
        return signedValue < 0
            ? $"-0x{(signedValue == long.MinValue ? 0x8000000000000000UL : (ulong)-signedValue):x}"
            : $"0x{signedValue:x}";
    }
}
