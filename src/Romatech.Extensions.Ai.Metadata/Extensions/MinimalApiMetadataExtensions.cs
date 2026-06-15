using Microsoft.AspNetCore.Builder;
using Romatech.Extensions.Ai.Metadata.Attributes;

namespace Romatech.Extensions.Ai.Metadata.Extensions;

/// <summary>
/// Extension methods for adding AI metadata to Minimal API endpoints.
/// </summary>
public static class MinimalApiMetadataExtensions
{
    /// <summary>
    /// Marks the endpoint as hidden from AI systems.
    /// </summary>
    public static RouteHandlerBuilder AiHidden(this RouteHandlerBuilder builder)
    {
        return builder.WithMetadata(new AiHiddenAttribute());
    }

    /// <summary>
    /// Marks the endpoint as an executable MCP tool.
    /// </summary>
    public static RouteHandlerBuilder AiTool(this RouteHandlerBuilder builder, string? name = null)
    {
        return builder.WithMetadata(name is null ? new AiToolAttribute() : new AiToolAttribute(name));
    }

    /// <summary>
    /// Sets the AI description for the endpoint.
    /// </summary>
    public static RouteHandlerBuilder AiDescription(this RouteHandlerBuilder builder, string description)
    {
        return builder.WithMetadata(new AiDescriptionAttribute(description));
    }

    /// <summary>
    /// Sets the AI category for the endpoint.
    /// </summary>
    public static RouteHandlerBuilder AiCategory(this RouteHandlerBuilder builder, string category)
    {
        return builder.WithMetadata(new AiCategoryAttribute(category));
    }

    /// <summary>
    /// Sets the required AI role for the endpoint.
    /// </summary>
    public static RouteHandlerBuilder AiRole(this RouteHandlerBuilder builder, string role)
    {
        return builder.WithMetadata(new AiRoleAttribute(role));
    }

    /// <summary>
    /// Sets the rate limit for the endpoint.
    /// </summary>
    public static RouteHandlerBuilder AiRateLimit(this RouteHandlerBuilder builder, int requestsPerMinute)
    {
        return builder.WithMetadata(new AiRateLimitAttribute(requestsPerMinute));
    }

    /// <summary>
    /// Sets the context priority for the endpoint.
    /// </summary>
    public static RouteHandlerBuilder AiContextPriority(this RouteHandlerBuilder builder, int priority)
    {
        return builder.WithMetadata(new AiContextPriorityAttribute(priority));
    }
}
