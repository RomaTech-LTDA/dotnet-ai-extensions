using System.Reflection;
using Romatech.Extensions.Ai.Metadata.Attributes;
using Romatech.Extensions.Ai.Shared.Models;

namespace Romatech.Extensions.Ai.Metadata.Resolution;

/// <summary>
/// Resolves AI metadata from method and class attributes.
/// </summary>
public static class MetadataResolver
{
    /// <summary>
    /// Determines the AI exposure level for a given method.
    /// </summary>
    public static AiExposureLevel ResolveExposureLevel(MethodInfo method)
    {
        if (method.GetCustomAttribute<AiHiddenAttribute>() is not null)
            return AiExposureLevel.Hidden;

        if (method.DeclaringType?.GetCustomAttribute<AiHiddenAttribute>() is not null)
            return AiExposureLevel.Hidden;

        if (method.GetCustomAttribute<AiToolAttribute>() is not null)
            return AiExposureLevel.Executable;

        if (method.DeclaringType?.GetCustomAttribute<AiToolAttribute>() is not null)
            return AiExposureLevel.Executable;

        return AiExposureLevel.ReadOnly;
    }

    /// <summary>
    /// Resolves the tool name for an endpoint.
    /// Priority: [AiTool("custom_name")] > OperationId > endpoint method name.
    /// </summary>
    public static string ResolveToolName(MethodInfo method, string? operationId = null)
    {
        var aiTool = method.GetCustomAttribute<AiToolAttribute>();
        if (aiTool?.Name is not null)
            return aiTool.Name;

        var classAiTool = method.DeclaringType?.GetCustomAttribute<AiToolAttribute>();
        if (classAiTool?.Name is not null)
            return classAiTool.Name;

        if (!string.IsNullOrWhiteSpace(operationId))
            return operationId;

        return ToSnakeCase(method.Name);
    }

    /// <summary>
    /// Resolves the description for an endpoint.
    /// </summary>
    public static string? ResolveDescription(MethodInfo method)
    {
        return method.GetCustomAttribute<AiDescriptionAttribute>()?.Description
            ?? method.DeclaringType?.GetCustomAttribute<AiDescriptionAttribute>()?.Description;
    }

    /// <summary>
    /// Resolves the category for an endpoint.
    /// </summary>
    public static string? ResolveCategory(MethodInfo method)
    {
        return method.GetCustomAttribute<AiCategoryAttribute>()?.Category
            ?? method.DeclaringType?.GetCustomAttribute<AiCategoryAttribute>()?.Category;
    }

    /// <summary>
    /// Resolves the required role for an endpoint.
    /// </summary>
    public static string? ResolveRole(MethodInfo method)
    {
        return method.GetCustomAttribute<AiRoleAttribute>()?.Role
            ?? method.DeclaringType?.GetCustomAttribute<AiRoleAttribute>()?.Role;
    }

    /// <summary>
    /// Resolves the rate limit for an endpoint.
    /// </summary>
    public static int? ResolveRateLimit(MethodInfo method)
    {
        return method.GetCustomAttribute<AiRateLimitAttribute>()?.RequestsPerMinute
            ?? method.DeclaringType?.GetCustomAttribute<AiRateLimitAttribute>()?.RequestsPerMinute;
    }

    /// <summary>
    /// Resolves the context priority for an endpoint.
    /// </summary>
    public static int ResolveContextPriority(MethodInfo method)
    {
        return method.GetCustomAttribute<AiContextPriorityAttribute>()?.Priority
            ?? method.DeclaringType?.GetCustomAttribute<AiContextPriorityAttribute>()?.Priority
            ?? 0;
    }

    private static string ToSnakeCase(string name)
    {
        if (string.IsNullOrEmpty(name))
            return name;

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                if (i > 0)
                    result.Append('_');
                result.Append(char.ToLowerInvariant(c));
            }
            else
            {
                result.Append(c);
            }
        }
        return result.ToString();
    }
}
