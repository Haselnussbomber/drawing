using Dalamud.Interface;
using Lumina.Text.ReadOnly;
using System.Collections;
using System.Globalization;
using System.Reflection;

namespace Una.Drawing.NodeParser;

internal static class NodeAttributeParser
{
    private static readonly Dictionary<Type, Dictionary<string, PropertyInfo>> PropertyCache = [];

    internal static void ApplyProperty(dynamic obj, string elementName, string name, string value)
    {
        if (obj == null) throw new Exception("Object cannot be null");
        if (name == "") throw new Exception("Name cannot be empty");

        Type type = obj.GetType();
        name = name.ToLowerInvariant().Replace("-", "").Trim();

        if (false == PropertyCache.TryGetValue(type, out Dictionary<string, PropertyInfo>? properties)) {
            properties = CreateTypePropertyCache(type);
            PropertyCache.TryAdd(type, properties);
        }

        if (false == properties.TryGetValue(name, out PropertyInfo? property)) {
            throw new Exception($"Node type \"{obj.GetType().Name}\" has no public property named \"{name}\".");
        }

        Type propType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

        if (!(value.StartsWith('{') && value.EndsWith('}'))) {
            if (propType == typeof(FontAwesomeIcon)) {
                if (!Enum.TryParse<FontAwesomeIcon>(value, true, out var icon)) {
                    throw new Exception($"Invalid FontAwesomeIcon: {value}.");
                }

                property.SetValue(obj, icon);
                return;
            }

            if (propType == typeof(ReadOnlySeString)) {
                property.SetValue(obj, ReadOnlySeString.FromMacroString(value));
                return;
            }

            try {
                property.SetValue(obj, ConvertValue(propType, value));
            } catch (Exception e) {
                throw new Exception(
                    $"Failed to set property \"{name}\" of element \"{elementName}\". {e.InnerException?.Message ?? e.Message} @ {e.InnerException?.StackTrace}");
            }

            return;
        }

        value = value[1..^1].Trim();

        if (value == "") {
            throw new Exception(
                $"Empty expression in attribute \"{name}\" of element \"{elementName}\" in UDT \"{obj.UdtFilename}\".");
        }

        dynamic? parsedValue = ExpressionParser.ExpressionParser.Parse(value);

        try {
            InjectValue(property, propType, parsedValue, name, obj);
        } catch (Exception e) {
            throw new Exception(
                $"{e.GetType().Name} Failed to parse attribute \"{name}\" of element \"{elementName}\". {e.Message}"
            );
        }
    }

    internal static void Dispose()
    {
        PropertyCache.Clear();
    }

    private static void InjectValue(PropertyInfo property, Type propType, dynamic? parsedValue, string name, object obj)
    {
        object? valueToSet;

        if (parsedValue == null) {
            if (!propType.IsValueType || Nullable.GetUnderlyingType(propType) != null) {
                valueToSet = null;
            } else {
                throw new InvalidCastException($"Cannot assign null to non-nullable type \"{propType.Name}\".");
            }
        } else if (parsedValue is IEnumerable sourceEnumerable and not string &&
                   propType.IsGenericType && propType.GetGenericTypeDefinition() == typeof(List<>)) {
            Type targetElementType = propType.GetGenericArguments()[0];

            var targetList = (IList)Activator.CreateInstance(propType)!;
            if (targetList == null) {
                throw new Exception($"Could not create instance of list type {propType.Name}.");
            }

            int index = 0;
            foreach (var item in sourceEnumerable) {
                try {
                    object? convertedItem = item == null
                        ? null
                        : Convert.ChangeType(item, targetElementType);

                    targetList.Add(convertedItem);
                } catch (Exception ex) {
                    throw new InvalidCastException(
                        $"Failed to convert element at index {index} ('{item ?? "null"}') to type '{targetElementType.Name}' for attribute \"{name}\".", ex);
                }

                index++;
            }

            valueToSet = targetList;
        } else {
            valueToSet = Convert.ChangeType(parsedValue, propType);
        }

        property.SetValue(obj, valueToSet);
    }

    private static Dictionary<string, PropertyInfo> CreateTypePropertyCache(Type type)
    {
        Dictionary<string, PropertyInfo> properties = [];

        foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public)) {
            properties.Add(property.Name.ToLowerInvariant(), property);
        }

        return properties;
    }

    private static dynamic ConvertValue(Type type, string value)
    {
        if (type == typeof(string)) return value;

        if (type == typeof(bool)) {
            return value switch {
                "true" or "1"  => true,
                "false" or "0" => false,
                _              => throw new Exception($"Invalid boolean value: {value}.")
            };
        }

        if (type == typeof(uint)) return Convert.ToUInt32(value);
        if (type == typeof(int)) return Convert.ToInt32(value);
        if (type == typeof(float)) return Convert.ToSingle(value, CultureInfo.InvariantCulture);
        if (type == typeof(double)) return Convert.ToDouble(value, CultureInfo.InvariantCulture);
        if (type == typeof(long)) return Convert.ToInt64(value);
        if (type == typeof(ulong)) return Convert.ToUInt64(value);
        if (type == typeof(byte)) return Convert.ToByte(value);
        if (type == typeof(sbyte)) return Convert.ToSByte(value);
        if (type == typeof(short)) return Convert.ToInt16(value);

        return value;
    }
}