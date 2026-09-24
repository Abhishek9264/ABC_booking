namespace Tatkal.Contracts;

// These are the wire contracts published to RabbitMQ. Keep them flat and
// serialization-friendly — no domain behavior lives here, only data.
// Every event carries CorrelationId so a trace can be reconstructed across
// service boundaries purely from the message log if tracing infra is down.

public record BookingCreated(Guid BookingId, Guid UserId, Guid ScheduleId, Guid CoachId, Guid SeatId, Guid CorrelationId, DateTime OccurredAtUtc);
public record BookingQueued(Guid BookingId, Guid CorrelationId, DateTime OccurredAtUtc);
public record SeatLockRequested(Guid BookingId, Guid ScheduleId, Guid CoachId, Guid SeatId, Guid CorrelationId, DateTime OccurredAtUtc);
public record SeatLocked(Guid BookingId, Guid SeatReservationId, DateTime ExpiresAtUtc, Guid CorrelationId, DateTime OccurredAtUtc);
public record SeatLockFailed(Guid BookingId, string Reason, Guid CorrelationId, DateTime OccurredAtUtc);
public record PaymentRequested(Guid BookingId, Guid UserId, decimal Amount, Guid CorrelationId, DateTime OccurredAtUtc);
public record PaymentSucceeded(Guid BookingId, Guid PaymentId, string ProviderTransactionId, Guid CorrelationId, DateTime OccurredAtUtc);
public record PaymentFailed(Guid BookingId, Guid PaymentId, string Reason, Guid CorrelationId, DateTime OccurredAtUtc);
public record SeatReleaseRequested(Guid BookingId, Guid SeatReservationId, string Reason, Guid CorrelationId, DateTime OccurredAtUtc);
public record SeatReleased(Guid BookingId, Guid SeatReservationId, Guid CorrelationId, DateTime OccurredAtUtc);
public record BookingConfirmed(Guid BookingId, Guid UserId, Guid CorrelationId, DateTime OccurredAtUtc);
public record BookingExpired(Guid BookingId, Guid CorrelationId, DateTime OccurredAtUtc);
public record BookingCancelled(Guid BookingId, string Reason, Guid CorrelationId, DateTime OccurredAtUtc);
public record NotifyUser(Guid UserId, string Channel, string TemplateKey, IDictionary<string, string> Data, Guid CorrelationId, DateTime OccurredAtUtc);
