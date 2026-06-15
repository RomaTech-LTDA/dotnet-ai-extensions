namespace Romatech.Extensions.Ai.Metadata.Attributes;

/// <summary>
/// Marks an endpoint as hidden from all AI systems.
/// The endpoint will be excluded from MCP, RAG, semantic documentation, and embeddings.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AiHiddenAttribute : Attribute
{
}
