using Romatech.Extensions.Ai.Shared.Abstractions;

namespace Romatech.Extensions.Ai.Rag.Embeddings;

/// <summary>
/// A lightweight local embedding provider using bag-of-words TF-IDF approximation.
/// Suitable for small-to-medium APIs without external dependencies.
/// For production use with large APIs, swap in OpenAI or Ollama adapters.
/// </summary>
public sealed class LocalEmbeddingProvider : IEmbeddingProvider
{
    private const int DefaultDimensions = 256;

    /// <inheritdoc />
    public int Dimensions => DefaultDimensions;

    /// <inheritdoc />
    public Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default)
    {
        var embedding = GenerateHashEmbedding(text);
        return Task.FromResult(embedding);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default)
    {
        var results = texts.Select(GenerateHashEmbedding).ToList();
        return Task.FromResult<IReadOnlyList<float[]>>(results);
    }

    /// <summary>
    /// Generates a deterministic embedding using character n-gram hashing.
    /// This is a lightweight approximation suitable for semantic search over API docs.
    /// </summary>
    private float[] GenerateHashEmbedding(string text)
    {
        var embedding = new float[DefaultDimensions];
        var normalizedText = text.ToLowerInvariant();
        var words = normalizedText.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        foreach (var word in words)
        {
            // Use character trigrams for better semantic capture
            for (int i = 0; i <= word.Length - 3; i++)
            {
                var trigram = word.Substring(i, 3);
                var hash = GetStableHash(trigram);
                var index = Math.Abs(hash) % DefaultDimensions;
                embedding[index] += (hash > 0) ? 1.0f : -1.0f;
            }

            // Also hash whole words
            var wordHash = GetStableHash(word);
            var wordIndex = Math.Abs(wordHash) % DefaultDimensions;
            embedding[wordIndex] += 2.0f;
        }

        // L2 normalize
        var magnitude = MathF.Sqrt(embedding.Sum(x => x * x));
        if (magnitude > 0)
        {
            for (int i = 0; i < embedding.Length; i++)
                embedding[i] /= magnitude;
        }

        return embedding;
    }

    private static int GetStableHash(string input)
    {
        unchecked
        {
            int hash = 17;
            foreach (var c in input)
            {
                hash = hash * 31 + c;
            }
            return hash;
        }
    }
}
