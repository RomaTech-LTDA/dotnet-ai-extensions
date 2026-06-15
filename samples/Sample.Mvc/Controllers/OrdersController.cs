using Microsoft.AspNetCore.Mvc;
using Romatech.Extensions.Ai.Metadata.Attributes;

namespace Sample.Mvc.Controllers;

/// <summary>
/// Manages customer orders.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AiCategory("Orders")]
public class OrdersController : ControllerBase
{
    /// <summary>
    /// Lists all orders for the current user.
    /// </summary>
    [HttpGet]
    [AiDescription("Retrieves all orders for the authenticated user")]
    public IActionResult GetOrders()
    {
        return Ok(new[] { new { Id = 1, Status = "Pending" } });
    }

    /// <summary>
    /// Creates a new order.
    /// </summary>
    [HttpPost]
    [AiTool("create_order")]
    [AiDescription("Creates a new customer order")]
    [AiRateLimit(10)]
    public IActionResult CreateOrder([FromBody] CreateOrderRequest request)
    {
        return Ok(new { Id = 2, Status = "Created", request.ProductId, request.Quantity });
    }

    /// <summary>
    /// Internal endpoint not exposed to AI.
    /// </summary>
    [HttpDelete("{id}")]
    [AiHidden]
    public IActionResult DeleteOrder(int id)
    {
        return NoContent();
    }
}

public record CreateOrderRequest(string ProductId, int Quantity);
