namespace Romatech.Extensions.Ai.Metadata.Attributes;

/// <summary>
/// Marks an endpoint as an executable MCP tool.
/// When applied, the endpoint will appear in tools/list and be callable by LLMs.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AiToolAttribute : Attribute
{
    /// <summary>
    /// Optional custom tool name. If not provided, the name is inferred from OperationId or endpoint name.
    /// </summary>
    public string? Name { get; }

    /// <summary>
    /// Creates a new AiTool attribute with an auto-generated name.
    /// </summary>
    public AiToolAttribute()
    {
    }

    /// <summary>
    /// Creates a new AiTool attribute with a custom tool name.
    /// </summary>
    /// <param name="name">The MCP tool name to use.</param>
    public AiToolAttribute(string name)
    {
        Name = name;
    }
}
