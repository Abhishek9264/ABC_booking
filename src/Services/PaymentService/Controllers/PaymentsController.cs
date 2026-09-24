using MassTransit;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PaymentService.Domain;
using Tatkal.Contracts;

namespace PaymentService.Controllers;

public record PaymentCallbackRequest(string ProviderTransactionId, string Outcome); // "SUCCESS" | "FAILED"

[ApiController]
[Route("api/payments")]
public class PaymentsController(PaymentDbContext db, IPublishEndpoint publishEndpoint) : ControllerBase
{
    [HttpGet("{bookingId:guid}")]
    public async Task<ActionResult<Payment>> GetByBooking(Guid bookingId, CancellationToken ct)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.BookingId == bookingId, ct);
        return payment is null ? NotFound() : Ok(payment);
    }

    /// <summary>
    /// Simulates a payment-provider webhook. The callback is idempotent:
    /// once a payment has reached a terminal state, repeating the same
    /// callback is a no-op. The ProviderTransactionId is unique in Postgres.
    /// </summary>
    [HttpPost("callback")]
    public async Task<IActionResult> Callback(
        [FromBody] PaymentCallbackRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.ProviderTransactionId))
            return BadRequest("ProviderTransactionId is required.");

        var outcome = request.Outcome.Trim().ToUpperInvariant();
        if (outcome is not ("SUCCESS" or "FAILED"))
            return BadRequest("Outcome must be SUCCESS or FAILED.");

        var payment = await db.Payments
            .SingleOrDefaultAsync(
                p => p.ProviderTransactionId == request.ProviderTransactionId,
                ct);

        if (payment is null)
            return NotFound("Payment transaction was not found.");

        if (payment.Status is PaymentStatus.Success or PaymentStatus.Failed or PaymentStatus.Timeout)
            return Ok(new { message = "Duplicate callback ignored — payment is already terminal." });

        var correlationId = Guid.NewGuid();

        if (outcome == "SUCCESS")
        {
            payment.Status = PaymentStatus.Success;
            payment.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await publishEndpoint.Publish(
                new PaymentSucceeded(
                    payment.BookingId,
                    payment.Id,
                    payment.ProviderTransactionId,
                    correlationId,
                    DateTime.UtcNow),
                ct);
        }
        else
        {
            payment.Status = PaymentStatus.Failed;
            payment.UpdatedAtUtc = DateTime.UtcNow;
            await db.SaveChangesAsync(ct);

            await publishEndpoint.Publish(
                new PaymentFailed(
                    payment.BookingId,
                    payment.Id,
                    "Provider callback reported failure",
                    correlationId,
                    DateTime.UtcNow),
                ct);
        }

        return Ok(new { message = "Payment callback processed." });
    }
}
