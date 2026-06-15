namespace Romatech.Extensions.Ai.Rag.Models;

/// <summary>
/// A semantic document generated from an API endpoint for RAG indexing.
/// </summary>
public sealed class SemanticDocument
{
    /// <summary>
    /// Unique identifier for this document.
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// The text content of the document used for embedding.
    /// </summary>
    public required string Content { get; init; }

    /// <summary>
    /// The source endpoint tool name.
    /// </summary>
    public required string ToolName { get; init; }

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
