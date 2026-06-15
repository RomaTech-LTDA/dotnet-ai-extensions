using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi.Models;

namespace Romatech.Extensions.Ai.Swagger.Schema;

/// <summary>
/// Converts OpenAPI schemas to MCP-compatible JSON Schema.
/// </summary>
public static class OpenApiSchemaConverter
{
    /// <summary>
    /// Converts an OpenAPI operation to a JSON Schema string for MCP tool input.
    /// </summary>
    public static string? ConvertToJsonSchema(OpenApiOperation operation)
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject(),
            ["required"] = new JsonArray()
        };

        var properties = schema["properties"]!.AsObject();
        var required = schema["required"]!.AsArray();

        // Add path and query parameters
        foreach (var param in operation.Parameters ?? Enumerable.Empty<OpenApiParameter>())
        {
            var paramSchema = ConvertSchemaType(param.Schema);
            if (param.Description is not null)
                paramSchema["description"] = param.Description;

            properties[param.Name] = paramSchema;

            if (param.Required)
                required.Add(param.Name);
        }

        // Add request body properties
        if (operation.RequestBody?.Content?.TryGetValue("application/json", out var mediaType) == true
            && mediaType.Schema is not null)
        {
            var bodySchema = ConvertOpenApiSchema(mediaType.Schema);
            if (bodySchema["properties"] is JsonObject bodyProps)
            {
                foreach (var (key, value) in bodyProps)
                {
                    properties[key] = value?.DeepClone() ?? new JsonObject();
                }
            }
            if (mediaType.Schema.Required is not null)
            {
                foreach (var req in mediaType.Schema.Required)
                {
                    required.Add(req);
                }
            }
        }

        if (properties.Count == 0)
            return null;

        return schema.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static JsonObject ConvertOpenApiSchema(OpenApiSchema openApiSchema)
    {
        var schema = new JsonObject
        {
            ["type"] = openApiSchema.Type ?? "object"
        };

        if (openApiSchema.Properties is not null)
        {
            var props = new JsonObject();
            foreach (var (name, propSchema) in openApiSchema.Properties)
            {
                props[name] = ConvertSchemaType(propSchema);
            }
            schema["properties"] = props;
        }

        return schema;
    }

    private static JsonObject ConvertSchemaType(OpenApiSchema? openApiSchema)
    {
        if (openApiSchema is null)
            return new JsonObject { ["type"] = "string" };

        var result = new JsonObject
        {
            ["type"] = openApiSchema.Type ?? "string"
        };

        if (openApiSchema.Format is not null)
            result["format"] = openApiSchema.Format;

        if (openApiSchema.Description is not null)
            result["description"] = openApiSchema.Description;

        if (openApiSchema.Enum?.Any() == true)
        {
            var enumArray = new JsonArray();
            foreach (var e in openApiSchema.Enum)
            {
                if (e is Microsoft.OpenApi.Any.OpenApiString str)
                    enumArray.Add(str.Value);
            }
            if (enumArray.Count > 0)
                result["enum"] = enumArray;
        }

        if (openApiSchema.Type == "array" && openApiSchema.Items is not null)
        {
            result["items"] = ConvertSchemaType(openApiSchema.Items);
        }

        return result;
    }
}
