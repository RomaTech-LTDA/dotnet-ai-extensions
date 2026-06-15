namespace Romatech.Extensions.Ai.Metadata.Attributes;

/// <summary>
/// Groups an endpoint into a semantic category for AI organization.
/// Used for semantic grouping, tool categorization, and prompt enrichment.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AiCategoryAttribute : Attribute
{
    /// <summary>
    /// The category name.
    /// </summary>
    public string Category { get; }

    /// <summary>
    /// Creates a new AiCategory attribute.
    /// </summary>
    /// <param name="category">The category name for grouping.</param>
    public AiCategoryAttribute(string category)
    {
        Category = category ?? throw new ArgumentNullException(nameof(category));
    }
}
