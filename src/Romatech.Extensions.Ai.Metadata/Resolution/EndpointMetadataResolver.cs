using Romatech.Extensions.Ai.Metadata.Attributes;
using Romatech.Extensions.Ai.Shared.Models;

namespace Romatech.Extensions.Ai.Metadata.Resolution;

/// <summary>
/// Resolves AI metadata from an endpoint's metadata collection.
/// Works for MVC actions (controller attributes come first, action attributes last) and for
/// Minimal APIs (<c>.AiTool()</c>, <c>.AiHidden()</c>, ... add the same attributes as metadata).
/// When the same attribute appears more than once, the most specific (last) one wins,
/// except <see cref="AiHiddenAttribute"/> (anywhere hides the endpoint) and
/// <see cref="AiRoleAttribute"/> (every occurrence is a requirement).
/// </summary>
public static class EndpointMetadataResolver
{
    /// <summary>Determines the exposure level. Hidden always wins.</summary>
    public static AiExposureLevel ResolveExposureLevel(IReadOnlyList<object> metadata)
    {
        if (metadata is null) throw new ArgumentNullException(nameof(metadata));

        if (metadata.OfType<AiHiddenAttribute>().Any())
            return AiExposureLevel.Hidden;

        return metadata.OfType<AiToolAttribute>().Any()
            ? AiExposureLevel.Executable
            : AiExposureLevel.ReadOnly;
    }

    /// <summary>The explicit tool name from the most specific <c>[AiTool("name")]</c>, if any.</summary>
    public static string? ResolveToolName(IReadOnlyList<object> metadata)
        => Last<AiToolAttribute>(metadata, a => a.Name);

    /// <summary>The most specific <c>[AiDescription]</c>.</summary>
    public static string? ResolveDescription(IReadOnlyList<object> metadata)
        => Last<AiDescriptionAttribute>(metadata, a => a.Description);

    /// <summary>The most specific <c>[AiCategory]</c>.</summary>
    public static string? ResolveCategory(IReadOnlyList<object> metadata)
        => Last<AiCategoryAttribute>(metadata, a => a.Category);

    /// <summary>The most specific <c>[AiRateLimit]</c>.</summary>
    public static int? ResolveRateLimit(IReadOnlyList<object> metadata)
    {
        if (metadata is null) throw new ArgumentNullException(nameof(metadata));
        for (var i = metadata.Count - 1; i >= 0; i--)
        {
            if (metadata[i] is AiRateLimitAttribute a) return a.RequestsPerMinute;
        }
        return null;
    }

    /// <summary>The most specific <c>[AiContextPriority]</c>, or 0.</summary>
    public static int ResolveContextPriority(IReadOnlyList<object> metadata)
    {
        if (metadata is null) throw new ArgumentNullException(nameof(metadata));
        for (var i = metadata.Count - 1; i >= 0; i--)
        {
            if (metadata[i] is AiContextPriorityAttribute a) return a.Priority;
        }
        return 0;
    }

    /// <summary>
    /// Every <c>[AiRole]</c> requirement, distinct, in declaration order.
    /// All entries must be satisfied; an entry may hold comma-separated alternatives.
    /// </summary>
    public static IReadOnlyList<string> ResolveRoles(IReadOnlyList<object> metadata)
    {
        if (metadata is null) throw new ArgumentNullException(nameof(metadata));
        return metadata.OfType<AiRoleAttribute>()
            .Select(a => a.Role.Trim())
            .Where(r => r.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static string? Last<T>(IReadOnlyList<object> metadata, Func<T, string?> selector) where T : class
    {
        if (metadata is null) throw new ArgumentNullException(nameof(metadata));
        for (var i = metadata.Count - 1; i >= 0; i--)
        {
            if (metadata[i] is T a)
            {
                var value = selector(a);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }
        }
        return null;
    }
}
