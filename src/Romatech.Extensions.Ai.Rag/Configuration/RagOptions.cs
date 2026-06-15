namespace Romatech.Extensions.Ai.Rag.Configuration;

/// <summary>
/// Configuration options for the RAG layer.
/// </summary>
public sealed class RagOptions
{
    /// <summary>
    /// Whether to include XML documentation in the semantic index. Default: true
    /// </summary>
    public bool IncludeXmlDocs { get; set; } = true;

    /// <summary>
    /// Maximum number of results to return from a search query. Default: 10
    /// </summary>
    public int MaxSearchResults { get; set; } = 10;

    /// <summary>
    /// Minimum similarity score (0.0 to 1.0) for results to be included. Default: 0.3
    /// </summary>
    public float MinimumSimilarity { get; set; } = 0.3f;

    /// <summary>
    /// Whether to automatically rebuild the index when endpoints change. Default: true
    /// </summary>
    public bool AutoRebuildIndex { get; set; } = true;
}
