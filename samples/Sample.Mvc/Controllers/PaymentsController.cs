using Microsoft.AspNetCore.Mvc;
using Romatech.Extensions.Ai.Metadata.Attributes;

namespace Sample.Mvc.Controllers;

/// <summary>
/// Manages payment operations.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AiCategory("Payments")]
public class PaymentsController : ControllerBase
{
    /// <summary>
    /// Creates a PIX payment.
    /// </summary>
    [HttpPost("pix")]
    [AiTool("create_pix_payment")]
    [AiDescription("Creates a PIX payment")]
    [AiRole("finance")]
    [AiRateLimit(5)]
    [AiContextPriority(100)]
    public IActionResult CreatePixPayment([FromBody] PixPaymentRequest request)
    {
        return Ok(new { TransactionId = Guid.NewGuid(), Status = "Processing" });
    }

    /// <summary>
    /// Gets payment status.
    /// </summary>
    [HttpGet("{id}")]
    [AiDescription("Retrieves the current status of a payment")]
    public IActionResult GetPaymentStatus(string id)
    {
        return Ok(new { Id = id, Status = "Completed" });
    }
}

public record PixPaymentRequest(decimal Amount, string PixKey, string Description);
