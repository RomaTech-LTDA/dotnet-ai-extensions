namespace Romatech.Extensions.Ai.Swagger.Discovery;

/// <summary>
/// Supplies the executable tool bindings used by the MCP layer.
/// </summary>
public interface IAiToolBindingSource
{
    /// <summary>
    /// Returns the bindings of every endpoint marked with <c>[AiTool]</c> that can be executed in-process.
    /// </summary>
    Task<IReadOnlyList<AiToolBinding>> GetToolBindingsAsync(CancellationToken cancellationToken = default);
}
