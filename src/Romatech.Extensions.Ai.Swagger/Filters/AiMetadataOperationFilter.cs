using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Romatech.Extensions.Ai.Metadata.Attributes;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Reflection;

namespace Romatech.Extensions.Ai.Swagger.Filters;

/// <summary>
/// Swagger operation filter that injects AI metadata extensions into the OpenAPI document.
/// This allows the discovery provider to determine exposure levels from the Swagger JSON.
/// </summary>
public sealed class AiMetadataOperationFilter : IOperationFilter
{
    /// <inheritdoc />
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var method = context.MethodInfo;

        // Check for [AiHidden]
        if (method.GetCustomAttribute<AiHiddenAttribute>() is not null ||
            method.DeclaringType?.GetCustomAttribute<AiHiddenAttribute>() is not null)
        {
            operation.Extensions["x-ai-hidden"] = new OpenApiBoolean(true);
            return;
        }

        // Check for [AiTool]
        var aiTool = method.GetCustomAttribute<AiToolAttribute>()
                     ?? method.DeclaringType?.GetCustomAttribute<AiToolAttribute>();
        if (aiTool is not null)
        {
            operation.Extensions["x-ai-tool"] = new OpenApiBoolean(true);

            if (aiTool.Name is not null)
            {
                operation.Extensions["x-ai-tool-name"] = new OpenApiString(aiTool.Name);
            }
        }

        // Description
        var description = method.GetCustomAttribute<AiDescriptionAttribute>()
                          ?? method.DeclaringType?.GetCustomAttribute<AiDescriptionAttribute>();
        if (description is not null)
        {
            operation.Extensions["x-ai-description"] = new OpenApiString(description.Description);
        }

        // Category
        var category = method.GetCustomAttribute<AiCategoryAttribute>()
                       ?? method.DeclaringType?.GetCustomAttribute<AiCategoryAttribute>();
        if (category is not null)
        {
            operation.Extensions["x-ai-category"] = new OpenApiString(category.Category);
        }

        // Role
        var role = method.GetCustomAttribute<AiRoleAttribute>()
                   ?? method.DeclaringType?.GetCustomAttribute<AiRoleAttribute>();
        if (role is not null)
        {
            operation.Extensions["x-ai-role"] = new OpenApiString(role.Role);
        }

        // Rate limit
        var rateLimit = method.GetCustomAttribute<AiRateLimitAttribute>()
                        ?? method.DeclaringType?.GetCustomAttribute<AiRateLimitAttribute>();
        if (rateLimit is not null)
        {
            operation.Extensions["x-ai-rate-limit"] = new OpenApiInteger(rateLimit.RequestsPerMinute);
        }

        // Context priority
        var priority = method.GetCustomAttribute<AiContextPriorityAttribute>()
                       ?? method.DeclaringType?.GetCustomAttribute<AiContextPriorityAttribute>();
        if (priority is not null)
        {
            operation.Extensions["x-ai-context-priority"] = new OpenApiInteger(priority.Priority);
        }
    }
}
