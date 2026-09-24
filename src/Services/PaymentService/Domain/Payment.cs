namespace PaymentService.Domain;

public enum PaymentStatus { Initiated, Processing, Success, Failed, Timeout, Cancelled }

public class Payment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid BookingId { get; set; }
    public Guid UserId { get; set; }
    public decimal Amount { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Initiated;

    /// <summary>
    /// The simulated provider's transaction id. Unique — this is what makes
    /// duplicate callback handling safe (spec section 12 / failure scenario
    /// 7): the second and third "PaymentSuccess" callback for the same
    /// ProviderTransactionId hit the unique index and no-op instead of
    /// creating a second success record.
    /// </summary>
    public string? ProviderTransactionId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
    public uint RowVersion { get; set; }
}
