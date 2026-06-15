using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Romatech.Extensions.Ai.Rag.Configuration;
using Romatech.Extensions.Ai.Rag.Indexing;
using Romatech.Extensions.Ai.Rag.Models;
using Romatech.Extensions.Ai.Shared.Abstractions;

namespace Romatech.Extensions.Ai.Rag.Services;

/// <summary>
/// Service that exposes RAG search capabilities and manages index lifecycle.
/// </summary>
public sealed class RagSearchService
{
    private readonly SemanticIndexer _indexer;
    private readonly IEndpointDiscoveryProvider _discoveryProvider;
    private readonly RagOptions _options;
    private readonly ILogger<RagSearchService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RagSearchService"/> class.
    /// </summary>
    public RagSearchService(
        SemanticIndexer indexer,
        IEndpointDiscoveryProvider discoveryProvider,
        IOptions<RagOptions> options,
        ILogger<RagSearchService> logger)
    {
        _indexer = indexer;
        _discoveryProvider = discoveryProvider;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the RAG index by discovering endpoints and building embeddings.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_indexer.IsIndexed) return;

        _logger.LogInformation("Initializing RAG index...");

        var endpoints = await _discoveryProvider.DiscoverEndpointsAsync(cancellationToken);
        await _indexer.IndexAsync(endpoints, cancellationToken);
    }

    /// <summary>
    /// Performs a semantic search across indexed API documentation.
    /// </summary>
    public async Task<IReadOnlyList<SearchResult>> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (!_indexer.IsIndexed)
        {
            await InitializeAsync(cancellationToken);
        }

        return await _indexer.SearchAsync(query, _options.MaxSearchResults, _options.MinimumSimilarity, cancellationToken);
    }
}
