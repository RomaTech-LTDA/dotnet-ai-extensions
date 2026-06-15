using System.Text;
using Microsoft.Extensions.Logging;
using Romatech.Extensions.Ai.Rag.Models;
using Romatech.Extensions.Ai.Shared.Abstractions;
using Romatech.Extensions.Ai.Shared.Models;

namespace Romatech.Extensions.Ai.Rag.Indexing;

/// <summary>
/// Builds and manages the semantic index from discovered endpoints.
/// </summary>
public sealed class SemanticIndexer
{
    private readonly IEmbeddingProvider _embeddingProvider;
    private readonly ILogger<SemanticIndexer> _logger;
    private readonly List<SemanticDocument> _documents = new();
    private readonly object _lock = new();
    private volatile bool _indexed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticIndexer"/> class.
    /// </summary>
    public SemanticIndexer(
        IEmbeddingProvider embeddingProvider,
        ILogger<SemanticIndexer> logger)
    {
        _embeddingProvider = embeddingProvider;
        _logger = logger;
    }

    /// <summary>
    /// Whether the index has been built.
    /// </summary>
    public bool IsIndexed => _indexed;

    /// <summary>
    /// Builds the semantic index from discovered endpoint descriptors.
    /// </summary>
    public async Task IndexAsync(IReadOnlyList<AiEndpointDescriptor> endpoints, CancellationToken cancellationToken = default)
    {
        var eligibleEndpoints = endpoints
            .Where(e => e.ExposureLevel != AiExposureLevel.Hidden)
            .ToList();

        _logger.LogInformation("Indexing {Count} endpoints for RAG", eligibleEndpoints.Count);

        var documents = eligibleEndpoints.Select(BuildDocument).ToList();
        var texts = documents.Select(d => d.Content).ToList();

        var embeddings = await _embeddingProvider.GenerateEmbeddingsAsync(texts, cancellationToken);

        for (int i = 0; i < documents.Count; i++)
        {
            documents[i].Embedding = embeddings[i];
        }

        lock (_lock)
        {
            _documents.Clear();
            _documents.AddRange(documents);
            _indexed = true;
        }

        _logger.LogInformation("RAG index built with {Count} documents", _documents.Count);
    }

    /// <summary>
    /// Searches the semantic index with a query and returns ranked results.
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(
        string query,
        int maxResults = 10,
        float minimumSimilarity = 0.3f,
        CancellationToken cancellationToken = default)
    {
        if (!_indexed)
            return Array.Empty<SearchResult>();

        var queryEmbedding = await _embeddingProvider.GenerateEmbeddingAsync(query, cancellationToken);

        List<SemanticDocument> snapshot;
        lock (_lock)
        {
            snapshot = _documents.ToList();
        }

        return snapshot
            .Where(d => d.Embedding is not null)
            .Select(d => new SearchResult
            {
                Document = d,
                Score = CosineSimilarity(queryEmbedding, d.Embedding!)
            })
            .Where(r => r.Score >= minimumSimilarity)
            .OrderByDescending(r => r.Score)
            .ThenByDescending(r => r.Document.Priority)
            .Take(maxResults)
            .ToList();
    }

    private static SemanticDocument BuildDocument(AiEndpointDescriptor endpoint)
    {
        var content = new StringBuilder();
        content.Append($"Endpoint: {endpoint.HttpMethod} {endpoint.Route}. ");

        if (endpoint.Description is not null)
            content.Append($"Description: {endpoint.Description}. ");

        if (endpoint.Category is not null)
            content.Append($"Category: {endpoint.Category}. ");

        if (endpoint.XmlDocSummary is not null)
            content.Append($"Documentation: {endpoint.XmlDocSummary}. ");

        content.Append($"Exposure: {endpoint.ExposureLevel}.");

        return new SemanticDocument
        {
            Id = $"{endpoint.HttpMethod}_{endpoint.Route}".Replace("/", "_"),
            Content = content.ToString(),
            ToolName = endpoint.ToolName,
            HttpMethod = endpoint.HttpMethod,
            Route = endpoint.Route,
            Category = endpoint.Category,
            Priority = endpoint.ContextPriority
        };
    }

    private static float CosineSimilarity(float[] a, float[] b)
    {
        if (a.Length != b.Length) return 0f;

        float dot = 0, magA = 0, magB = 0;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }

        var magnitude = MathF.Sqrt(magA) * MathF.Sqrt(magB);
        return magnitude == 0 ? 0f : dot / magnitude;
    }
}
