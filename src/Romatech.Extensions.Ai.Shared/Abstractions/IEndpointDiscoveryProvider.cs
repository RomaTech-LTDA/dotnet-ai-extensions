using Romatech.Extensions.Ai.Shared.Models;

namespace Romatech.Extensions.Ai.Shared.Abstractions;

/// <summary>
/// Contract for discovering API endpoints and converting them to AI-consumable descriptors.
/// </summary>
public interface IEndpointDiscoveryProvider
{
    /// <summary>
    /// Discovers all API endpoints and returns their AI descriptors.
    /// </summary>
    Task<IReadOnlyList<AiEndpointDescriptor>> DiscoverEndpointsAsync(CancellationToken cancellationToken = default);
}
