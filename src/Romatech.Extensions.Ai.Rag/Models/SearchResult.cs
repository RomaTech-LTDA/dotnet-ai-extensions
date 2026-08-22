namespace Romatech.Extensions.Ai.Rag.Models;

/// <summary>
/// A single result from a RAG semantic search.
/// </summary>
public sealed class SearchResult
{
    /// <summary>
    /// The matched document.
    /// </summary>
    public SemanticDocument Document { get; init; } = default!;

    /// <summary>
    /// Cosine similarity score (0.0 to 1.0).
    /// </summary>
    public float Score { get; init; }
}
