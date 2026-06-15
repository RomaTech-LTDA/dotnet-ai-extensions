namespace Romatech.Extensions.Ai.Metadata.Attributes;

/// <summary>
/// Provides a human-readable description for AI consumers.
/// Used in MCP tool descriptions, semantic indexing, and RAG enrichment.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class | AttributeTargets.Parameter, AllowMultiple = false, Inherited = true)]
public sealed class AiDescriptionAttribute : Attribute
{
    /// <summary>
    /// The description text.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Creates a new AiDescription attribute.
    /// </summary>
    /// <param name="description">The AI-facing description.</param>
    public AiDescriptionAttribute(string description)
    {
        Description = description ?? throw new ArgumentNullException(nameof(description));
    }
}
