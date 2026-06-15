namespace Romatech.Extensions.Ai.Shared.Abstractions;

/// <summary>
/// Contract for generating text embeddings used in RAG semantic search.
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>
    /// Generates an embedding vector for the given text.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A float array representing the embedding vector.</returns>
    Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embedding vectors for multiple texts in batch.
    /// </summary>
    /// <param name="texts">The texts to embed.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of float arrays representing the embedding vectors.</returns>
    Task<IReadOnlyList<float[]>> GenerateEmbeddingsAsync(IEnumerable<string> texts, CancellationToken cancellationToken = default);

    /// <summary>
    /// The dimension of embedding vectors produced by this provider.
    /// </summary>
    int Dimensions { get; }
}
