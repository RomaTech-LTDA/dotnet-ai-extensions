namespace Romatech.Extensions.Ai.Metadata.Attributes;

/// <summary>
/// Specifies the required role for executing this endpoint via MCP.
/// Used for MCP authorization, AI governance, and execution permissions.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = true)]
public sealed class AiRoleAttribute : Attribute
{
    /// <summary>
    /// The required role name.
    /// </summary>
    public string Role { get; }

    /// <summary>
    /// Creates a new AiRole attribute.
    /// </summary>
    /// <param name="role">The role required for execution.</param>
    public AiRoleAttribute(string role)
    {
        Role = role ?? throw new ArgumentNullException(nameof(role));
    }
}
