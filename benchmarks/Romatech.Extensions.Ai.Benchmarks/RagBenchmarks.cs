using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Logging.Abstractions;
using Romatech.Extensions.Ai.Rag.Embeddings;
using Romatech.Extensions.Ai.Rag.Indexing;
using Romatech.Extensions.Ai.Shared.Models;

namespace Romatech.Extensions.Ai.Benchmarks;

[MemoryDiagnoser]
[SimpleJob(warmupCount: 3, iterationCount: 10)]
public class RagBenchmarks
{
    private SemanticIndexer _indexer = null!;
    private List<AiEndpointDescriptor> _endpoints = null!;

    [GlobalSetup]
    public void Setup()
    {
        var provider = new LocalEmbeddingProvider();
        _indexer = new SemanticIndexer(provider, NullLogger<SemanticIndexer>.Instance);

        _endpoints = Enumerable.Range(1, 100).Select(i => new AiEndpointDescriptor
        {
            ToolName = $"tool_{i}",
            HttpMethod = i % 2 == 0 ? "GET" : "POST",
            Route = $"/api/resource{i}",
            Description = $"Endpoint {i} that handles resource {i} operations",
            Category = $"Category{i % 5}",
            ExposureLevel = i % 3 == 0 ? AiExposureLevel.Executable : AiExposureLevel.ReadOnly
        }).ToList();
    }

    [Benchmark]
    public async Task IndexEndpoints()
    {
        var provider = new LocalEmbeddingProvider();
        var indexer = new SemanticIndexer(provider, NullLogger<SemanticIndexer>.Instance);
        await indexer.IndexAsync(_endpoints);
    }

    [Benchmark]
    public async Task SearchAfterIndex()
    {
        await _indexer.IndexAsync(_endpoints);
        await _indexer.SearchAsync("resource operations handling");
    }

    [Benchmark]
    public Task<float[]> GenerateEmbedding()
    {
        var provider = new LocalEmbeddingProvider();
        return provider.GenerateEmbeddingAsync("How do I create a payment transaction?");
    }
}
