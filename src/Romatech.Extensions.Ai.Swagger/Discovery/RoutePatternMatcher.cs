using Microsoft.AspNetCore.Routing.Patterns;

namespace Romatech.Extensions.Ai.Swagger.Discovery;

/// <summary>
/// Compares two route patterns structurally, ignoring constraints, defaults, optional and catch-all markers.
/// ApiExplorer's <c>RelativePath</c> drops those details, while the endpoint's pattern keeps them.
/// </summary>
internal static class RoutePatternMatcher
{
    public static bool AreEquivalent(RoutePattern endpointPattern, RoutePattern describedPattern)
    {
        var a = endpointPattern.PathSegments;
        var b = describedPattern.PathSegments;
        if (a.Count != b.Count)
            return false;

        for (var i = 0; i < a.Count; i++)
        {
            var pa = a[i].Parts;
            var pb = b[i].Parts;
            if (pa.Count != pb.Count)
                return false;

            for (var j = 0; j < pa.Count; j++)
            {
                if (!PartEquals(pa[j], pb[j]))
                    return false;
            }
        }

        return true;
    }

    private static bool PartEquals(RoutePatternPart x, RoutePatternPart y) => (x, y) switch
    {
        (RoutePatternLiteralPart lx, RoutePatternLiteralPart ly) => string.Equals(lx.Content, ly.Content, StringComparison.OrdinalIgnoreCase),
        (RoutePatternParameterPart px, RoutePatternParameterPart py) => string.Equals(px.Name, py.Name, StringComparison.OrdinalIgnoreCase),
        (RoutePatternSeparatorPart sx, RoutePatternSeparatorPart sy) => string.Equals(sx.Content, sy.Content, StringComparison.Ordinal),
        _ => false,
    };
}
