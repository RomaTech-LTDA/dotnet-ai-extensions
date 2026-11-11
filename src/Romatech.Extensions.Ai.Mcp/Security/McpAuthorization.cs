using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Romatech.Extensions.Ai.Mcp.Security;

/// <summary>
/// Outcome of an authorization check.
/// </summary>
internal enum AuthorizationOutcome
{
    Allowed,
    Unauthenticated,
    Forbidden,
}

/// <summary>
/// Authorization helpers that reproduce what ASP.NET Core's <c>AuthorizationMiddleware</c> does,
/// so that an endpoint invoked in-process is protected exactly as if it were called over HTTP.
/// </summary>
internal static class McpAuthorization
{
    /// <summary>
    /// Authenticates the MCP caller with the default scheme when no earlier middleware did,
    /// so the result does not depend on where <c>UseMcp()</c> sits relative to <c>UseAuthentication()</c>.
    /// </summary>
    public static async Task EnsureAuthenticatedAsync(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated == true)
            return;

        var schemes = context.RequestServices.GetService<IAuthenticationSchemeProvider>();
        if (schemes is null)
            return;

        var defaultScheme = await schemes.GetDefaultAuthenticateSchemeAsync().ConfigureAwait(false);
        if (defaultScheme is null)
            return;

        var result = await context.AuthenticateAsync(defaultScheme.Name).ConfigureAwait(false);
        if (result.Succeeded && result.Principal is not null)
            context.User = result.Principal;
    }

    /// <summary>
    /// Authorizes the caller of the MCP endpoint itself.
    /// </summary>
    public static async Task<AuthorizationOutcome> AuthorizeMcpCallerAsync(HttpContext context, bool requireAuthentication, string? policyName)
    {
        if (!requireAuthentication && string.IsNullOrWhiteSpace(policyName))
            return AuthorizationOutcome.Allowed;

        await EnsureAuthenticatedAsync(context).ConfigureAwait(false);

        AuthorizationPolicy? policy = null;
        if (!string.IsNullOrWhiteSpace(policyName))
        {
            var provider = context.RequestServices.GetService<IAuthorizationPolicyProvider>();
            policy = provider is null ? null : await provider.GetPolicyAsync(policyName!).ConfigureAwait(false);
            if (policy is null)
                throw new InvalidOperationException(
                    $"MCP authorization policy '{policyName}' is not registered. Add it with services.AddAuthorization(o => o.AddPolicy(...)).");
        }

        if (requireAuthentication)
        {
            var builder = new AuthorizationPolicyBuilder().RequireAuthenticatedUser();
            if (policy is not null) builder.Combine(policy);
            policy = builder.Build();
        }

        return await EvaluateAsync(context, policy!, allowAnonymous: false).ConfigureAwait(false);
    }

    /// <summary>
    /// Authorizes a tool call against the target endpoint's own metadata: <c>[Authorize]</c>,
    /// <c>RequireAuthorization(...)</c>, <c>[AllowAnonymous]</c> and the application's fallback policy.
    /// </summary>
    public static async Task<AuthorizationOutcome> AuthorizeEndpointAsync(HttpContext context, Endpoint endpoint)
    {
        var metadata = endpoint.Metadata;
        var allowAnonymous = metadata.GetMetadata<IAllowAnonymous>() is not null;

        var policyProvider = context.RequestServices.GetService<IAuthorizationPolicyProvider>();
        var authorizeData = metadata.GetOrderedMetadata<IAuthorizeData>();
#if NET7_0_OR_GREATER
        var policies = metadata.GetOrderedMetadata<AuthorizationPolicy>();
        var hasAuthMetadata = authorizeData.Count > 0 || policies.Count > 0;
#else
        var hasAuthMetadata = authorizeData.Count > 0;
#endif
#if NET8_0_OR_GREATER
        var requirementData = metadata.GetOrderedMetadata<IAuthorizationRequirementData>();
        hasAuthMetadata |= requirementData.Count > 0;
#endif

        if (policyProvider is null)
        {
            // No authorization services: the host could not enforce this metadata either. Fail closed.
            return allowAnonymous || !hasAuthMetadata ? AuthorizationOutcome.Allowed : AuthorizationOutcome.Forbidden;
        }

#if NET7_0_OR_GREATER
        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authorizeData, policies).ConfigureAwait(false);
#else
        var policy = await AuthorizationPolicy.CombineAsync(policyProvider, authorizeData).ConfigureAwait(false);
#endif
#if NET8_0_OR_GREATER
        if (requirementData.Count > 0)
        {
            var builder = new AuthorizationPolicyBuilder();
            if (policy is not null) builder.Combine(policy);
            foreach (var data in requirementData)
                builder.AddRequirements(data.GetRequirements().ToArray());
            policy = builder.Build();
        }
#endif
        policy ??= await policyProvider.GetFallbackPolicyAsync().ConfigureAwait(false);

        // Same order as AuthorizationMiddleware: authenticate with the policy's schemes first (this sets the
        // identity the endpoint sees), and only then honour [AllowAnonymous].
        return policy is null
            ? AuthorizationOutcome.Allowed
            : await EvaluateAsync(context, policy, allowAnonymous).ConfigureAwait(false);
    }

    /// <summary>
    /// Checks <c>[AiRole]</c> requirements: every entry must match; an entry may list comma-separated alternatives.
    /// </summary>
    public static bool SatisfiesRoles(ClaimsPrincipal? user, IReadOnlyList<string> requirements)
    {
        if (requirements.Count == 0)
            return true;
        if (user?.Identity?.IsAuthenticated != true)
            return false;

        foreach (var requirement in requirements)
        {
            var alternatives = requirement.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (!alternatives.Any(user.IsInRole))
                return false;
        }
        return true;
    }

    private static async Task<AuthorizationOutcome> EvaluateAsync(HttpContext context, AuthorizationPolicy policy, bool allowAnonymous)
    {
        var evaluator = context.RequestServices.GetService<IPolicyEvaluator>();
        if (evaluator is null)
        {
            var authorizationService = context.RequestServices.GetService<IAuthorizationService>();
            if (authorizationService is null)
            {
                // No authorization services registered: only "authenticated user" can be checked; anything else fails closed.
                if (allowAnonymous)
                    return AuthorizationOutcome.Allowed;
                var authenticated = context.User?.Identity?.IsAuthenticated == true;
                if (!authenticated)
                    return AuthorizationOutcome.Unauthenticated;
                var onlyDenyAnonymous = policy.AuthenticationSchemes.Count == 0 && policy.Requirements.All(
                    r => r is Microsoft.AspNetCore.Authorization.Infrastructure.DenyAnonymousAuthorizationRequirement);
                return onlyDenyAnonymous ? AuthorizationOutcome.Allowed : AuthorizationOutcome.Forbidden;
            }
            evaluator = new PolicyEvaluator(authorizationService);
        }

        var authenticateResult = await evaluator.AuthenticateAsync(policy, context).ConfigureAwait(false);
        if (allowAnonymous)
            return AuthorizationOutcome.Allowed;

        var authorizeResult = await evaluator.AuthorizeAsync(policy, authenticateResult, context, context).ConfigureAwait(false);

        if (authorizeResult.Succeeded)
            return AuthorizationOutcome.Allowed;
        return authorizeResult.Challenged ? AuthorizationOutcome.Unauthenticated : AuthorizationOutcome.Forbidden;
    }
}
