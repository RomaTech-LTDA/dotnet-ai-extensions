namespace Romatech.Extensions.Ai.Metadata.Attributes;

/// <summary>
/// Specifies the priority for RAG ranking and semantic importance.
/// Higher values indicate greater importance in context selection.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class AiContextPriorityAttribute : Attribute
{
    /// <summary>
    /// The priority value (higher = more important).
    /// </summary>
    public int Priority { get; }

    /// <summary>
    /// Creates a new AiContextPriority attribute.
    /// </summary>
    /// <param name="priority">Priority value for RAG ranking.</param>
    public AiContextPriorityAttribute(int priority)
    {
        Priority = priority;
    }
}
