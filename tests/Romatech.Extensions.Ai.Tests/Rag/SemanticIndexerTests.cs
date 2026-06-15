using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Romatech.Extensions.Ai.Rag.Embeddings;
using Romatech.Extensions.Ai.Rag.Indexing;
using Romatech.Extensions.Ai.Shared.Models;
using Xunit;

namespace Romatech.Extensions.Ai.Tests.Rag;

public class SemanticIndexerTests
{
    private readonly SemanticIndexer _indexer;

    public SemanticIndexerTests()
    {
        var embeddingProvider = new LocalEmbeddingProvider();
        _indexer = new SemanticIndexer(embeddingProvider, NullLogger<SemanticIndexer>.Instance);
    }

    [Fact]
    public async Task IndexAsync_WithEndpoints_SetsIsIndexed()
    {
        var endpoints = new List<AiEndpointDescriptor>
        {
            new()
            {
                ToolName = "get_orders",
                HttpMethod = "GET",
                Route = "/api/orders",
                Description = "Gets all orders",
                ExposureLevel = AiExposureLevel.ReadOnly
            }
        };

        await _indexer.IndexAsync(endpoints);

        _indexer.IsIndexed.Should().BeTrue();
    }

    [Fact]
    public async Task IndexAsync_ExcludesHiddenEndpoints()
    {
        var endpoints = new List<AiEndpointDescriptor>
        {
            new()
            {
                ToolName = "hidden_endpoint",
                HttpMethod = "GET",
                Route = "/api/secret",
                ExposureLevel = AiExposureLevel.Hidden
            },
            new()
            {
                ToolName = "visible_endpoint",
                HttpMethod = "GET",
                Route = "/api/visible",
                Description = "Visible endpoint",
                ExposureLevel = AiExposureLevel.ReadOnly
            }
        };

        await _indexer.IndexAsync(endpoints);

        var results = await _indexer.SearchAsync("visible", minimumSimilarity: 0.0f);
        results.Should().NotContain(r => r.Document.ToolName == "hidden_endpoint");
    }

    [Fact]
    public async Task SearchAsync_ReturnsRelevantResults()
    {
        var endpoints = new List<AiEndpointDescriptor>
        {
            new()
            {
                ToolName = "create_payment",
                HttpMethod = "POST",
                Route = "/api/payments",
                Description = "Creates a new payment transaction",
                Category = "Payments",
                ExposureLevel = AiExposureLevel.Executable
            },
            new()
            {
                ToolName = "get_users",
                HttpMethod = "GET",
                Route = "/api/users",
                Description = "Lists all registered users",
                Category = "Users",
                ExposureLevel = AiExposureLevel.ReadOnly
            }
        };

        await _indexer.IndexAsync(endpoints);

        var results = await _indexer.SearchAsync("payment transaction", minimumSimilarity: 0.0f);
        results.Should().NotBeEmpty();
        results.First().Document.ToolName.Should().Be("create_payment");
    }

    [Fact]
    public async Task SearchAsync_WhenNotIndexed_ReturnsEmpty()
    {
        var results = await _indexer.SearchAsync("anything");
        results.Should().BeEmpty();
    }
}
