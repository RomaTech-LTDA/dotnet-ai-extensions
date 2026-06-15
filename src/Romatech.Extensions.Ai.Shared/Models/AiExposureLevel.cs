namespace Romatech.Extensions.Ai.Shared.Models;

/// <summary>
/// Defines how an endpoint is exposed to AI consumers.
/// </summary>
public enum AiExposureLevel
{
    /// <summary>
    /// The endpoint is completely hidden from AI systems.
    /// </summary>
    Hidden = 0,

    /// <summary>
    /// The endpoint is available for RAG/documentation but not executable.
    /// </summary>
    ReadOnly = 1,

    /// <summary>
    /// The endpoint is a fully executable MCP tool.
    /// </summary>
    Executable = 2
}
