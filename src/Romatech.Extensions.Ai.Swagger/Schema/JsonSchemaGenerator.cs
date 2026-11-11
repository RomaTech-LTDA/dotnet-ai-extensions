using System.Collections;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Romatech.Extensions.Ai.Swagger.Schema;

/// <summary>
/// Produces a JSON Schema (draft 2020-12 subset understood by MCP clients) for a CLR type,
/// honouring the System.Text.Json options the endpoint actually uses (naming policy,
/// <see cref="JsonPropertyNameAttribute"/>, <see cref="JsonIgnoreAttribute"/>, enum converters).
/// The same output is produced on every target framework.
/// </summary>
public static class JsonSchemaGenerator
{
    private const int MaxDepth = 8;

    /// <summary>
    /// Generates the schema for <paramref name="type"/>.
    /// </summary>
    /// <param name="type">The CLR type.</param>
    /// <param name="options">The serializer options the endpoint uses.</param>
    /// <param name="enumsAsStrings">
    /// Force string enums (route/query/header values are bound by name). When false, string enums are used
    /// only if the options or the enum type declare a <see cref="JsonStringEnumConverter"/>.
    /// </param>
    public static JsonObject Generate(Type type, JsonSerializerOptions options, bool enumsAsStrings = false)
    {
        if (type is null) throw new ArgumentNullException(nameof(type));
        if (options is null) throw new ArgumentNullException(nameof(options));
        return Build(type, options, enumsAsStrings, new HashSet<Type>(), 0);
    }

    private static JsonObject Build(Type type, JsonSerializerOptions options, bool enumsAsStrings, HashSet<Type> visiting, int depth)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;

        if (type == typeof(string) || type == typeof(char))
            return new JsonObject { ["type"] = "string" };
        if (type == typeof(bool))
            return new JsonObject { ["type"] = "boolean" };
        if (type == typeof(byte) || type == typeof(sbyte) || type == typeof(short) || type == typeof(ushort)
            || type == typeof(int) || type == typeof(uint) || type == typeof(long) || type == typeof(ulong))
            return new JsonObject { ["type"] = "integer" };
        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            return new JsonObject { ["type"] = "number" };
        if (type == typeof(Guid))
            return new JsonObject { ["type"] = "string", ["format"] = "uuid" };
        if (type == typeof(DateTime) || type == typeof(DateTimeOffset))
            return new JsonObject { ["type"] = "string", ["format"] = "date-time" };
        if (type == typeof(DateOnly))
            return new JsonObject { ["type"] = "string", ["format"] = "date" };
        if (type == typeof(TimeOnly))
            return new JsonObject { ["type"] = "string", ["format"] = "time" };
        if (type == typeof(TimeSpan))
            return new JsonObject { ["type"] = "string", ["description"] = "Time span, e.g. \"01:30:00\"." };
        if (type == typeof(Uri))
            return new JsonObject { ["type"] = "string", ["format"] = "uri" };
        if (type == typeof(byte[]))
            return new JsonObject { ["type"] = "string", ["contentEncoding"] = "base64" };
        if (type.IsEnum)
            return BuildEnum(type, options, enumsAsStrings);
        if (type == typeof(object) || type == typeof(JsonElement) || type == typeof(JsonDocument)
            || typeof(JsonNode).IsAssignableFrom(type))
            return new JsonObject();

        if (depth >= MaxDepth || visiting.Contains(type))
            return new JsonObject { ["type"] = "object" };

        var dictionaryValueType = GetDictionaryValueType(type);
        if (dictionaryValueType is not null)
        {
            visiting.Add(type);
            var dict = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = Build(dictionaryValueType, options, enumsAsStrings, visiting, depth + 1)
            };
            visiting.Remove(type);
            return dict;
        }

        var elementType = GetEnumerableElementType(type);
        if (elementType is not null)
        {
            visiting.Add(type);
            var array = new JsonObject
            {
                ["type"] = "array",
                ["items"] = Build(elementType, options, enumsAsStrings, visiting, depth + 1)
            };
            visiting.Remove(type);
            return array;
        }

        visiting.Add(type);
        var result = BuildObject(type, options, enumsAsStrings, visiting, depth);
        visiting.Remove(type);
        return result;
    }

    private static JsonObject BuildObject(Type type, JsonSerializerOptions options, bool enumsAsStrings, HashSet<Type> visiting, int depth)
    {
        var properties = new JsonObject();
        var required = new JsonArray();
        var ctorParameters = GetPrimaryConstructorParameters(type);

        IEnumerable<MemberInfo> members = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && p.GetMethod is not null);
        if (options.IncludeFields)
            members = members.Concat(type.GetFields(BindingFlags.Public | BindingFlags.Instance));

        foreach (var member in members)
        {
            var ignore = member.GetCustomAttribute<JsonIgnoreAttribute>();
            if (ignore is not null && ignore.Condition == JsonIgnoreCondition.Always)
                continue;

            var memberType = member is PropertyInfo pi ? pi.PropertyType : ((FieldInfo)member).FieldType;
            var name = member.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                ?? options.PropertyNamingPolicy?.ConvertName(member.Name)
                ?? member.Name;

            if (properties.ContainsKey(name))
                continue;

            var schema = Build(memberType, options, enumsAsStrings, visiting, depth + 1);
            var description = member.GetCustomAttribute<DescriptionAttribute>()?.Description;
            if (!string.IsNullOrWhiteSpace(description))
                schema["description"] = description;

            properties[name] = schema;

            ctorParameters.TryGetValue(member.Name, out var ctorParameter);
            if (IsRequired(member) || (ctorParameter is not null && IsRequired(ctorParameter)))
                required.Add(name);
        }

        var result = new JsonObject { ["type"] = "object", ["properties"] = properties };
        if (required.Count > 0)
            result["required"] = required;
        return result;
    }

    private static JsonObject BuildEnum(Type enumType, JsonSerializerOptions options, bool enumsAsStrings)
    {
        var names = Enum.GetNames(enumType);
        if (enumsAsStrings || UsesStringEnumConverter(enumType, options))
        {
            var values = new JsonArray();
            foreach (var n in names) values.Add(n);
            return new JsonObject { ["type"] = "string", ["enum"] = values };
        }

        var numbers = new JsonArray();
        var legend = new List<string>();
        foreach (var n in names)
        {
            var value = Convert.ToInt64(Enum.Parse(enumType, n), System.Globalization.CultureInfo.InvariantCulture);
            numbers.Add(value);
            legend.Add($"{value}={n}");
        }
        return new JsonObject
        {
            ["type"] = "integer",
            ["enum"] = numbers,
            ["description"] = string.Join(", ", legend)
        };
    }

    private static bool UsesStringEnumConverter(Type enumType, JsonSerializerOptions options)
    {
        if (enumType.GetCustomAttribute<JsonConverterAttribute>()?.ConverterType is { } converterType
            && IsStringEnumConverter(converterType))
            return true;

        return options.Converters.Any(c => IsStringEnumConverter(c.GetType()) && c.CanConvert(enumType));
    }

    private static bool IsStringEnumConverter(Type converterType)
    {
        for (var t = converterType; t is not null; t = t.BaseType)
        {
            if (t == typeof(JsonStringEnumConverter)) return true;
            if (t.IsGenericType && t.GetGenericTypeDefinition().FullName == "System.Text.Json.Serialization.JsonStringEnumConverter`1")
                return true;
        }
        return false;
    }

    private static bool IsRequired(ICustomAttributeProvider provider)
    {
        foreach (var attribute in provider.GetCustomAttributes(inherit: true))
        {
            var fullName = attribute.GetType().FullName;
            if (fullName is "System.ComponentModel.DataAnnotations.RequiredAttribute"
                or "System.Text.Json.Serialization.JsonRequiredAttribute"
                or "System.Runtime.CompilerServices.RequiredMemberAttribute")
                return true;
        }
        return false;
    }

    private static Dictionary<string, ParameterInfo> GetPrimaryConstructorParameters(Type type)
    {
        // Records and immutable DTOs: attributes like [Required] on positional parameters live on the
        // constructor parameter, not on the generated property.
        var ctor = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .OrderByDescending(c => c.GetParameters().Length)
            .FirstOrDefault();
        var map = new Dictionary<string, ParameterInfo>(StringComparer.OrdinalIgnoreCase);
        if (ctor is null) return map;
        foreach (var p in ctor.GetParameters())
        {
            if (p.Name is not null && !map.ContainsKey(p.Name)) map[p.Name] = p;
        }
        return map;
    }

    private static Type? GetDictionaryValueType(Type type)
    {
        foreach (var candidate in new[] { type }.Concat(type.GetInterfaces()))
        {
            if (!candidate.IsGenericType) continue;
            var def = candidate.GetGenericTypeDefinition();
            if ((def == typeof(IDictionary<,>) || def == typeof(IReadOnlyDictionary<,>))
                && candidate.GetGenericArguments()[0] == typeof(string))
                return candidate.GetGenericArguments()[1];
        }
        return null;
    }

    private static Type? GetEnumerableElementType(Type type)
    {
        if (type.IsArray) return type.GetElementType();
        if (!typeof(IEnumerable).IsAssignableFrom(type)) return null;
        foreach (var candidate in new[] { type }.Concat(type.GetInterfaces()))
        {
            if (candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                return candidate.GetGenericArguments()[0];
        }
        return typeof(object);
    }
}
