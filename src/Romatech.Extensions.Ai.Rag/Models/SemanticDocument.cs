namespace Romatech.Extensions.Ai.Rag.Models;

/// <summary>
/// A semantic document generated from an API endpoint for RAG indexing.
/// </summary>
public sealed class SemanticDocument
{
    /// <summary>
    /// Unique identifier for this document.
    /// </summary>
    public string Id { get; init; } = default!;

    /// <summary>
    /// The text content of the document used for embedding.
    /// </summary>
    public string Content { get; init; } = default!;

    /// <summary>
    /// The source endpoint tool name.
    /// </summary>
    public string ToolName { get; init; } = default!;

    /// <summary>
    /// HTTP method of the source endpoint.
    /// </summary>
    public string? HttpMethod { get; init; }

    /// <summary>
    /// Route of the source endpoint.
    /// </summary>
    public string? Route { get; init; }

    /// <summary>
    /// Category for semantic grouping.
    /// </summary>
    public string? Category { get; init; }

    /// <summary>
    /// Priority for ranking results.
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// The computed embedding vector.
    /// </summary>
    public float[]? Embedding { get; set; }
}
