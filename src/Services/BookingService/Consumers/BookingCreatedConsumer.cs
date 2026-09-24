using BookingService.Concurrency;
using BookingService.Domain;
using BookingService.Events;
using BookingService.Hubs;
using BookingService.Outbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Tatkal.Contracts;

namespace BookingService.Consumers;

/// <summary>
/// Consumes a queued booking and performs seat allocation. The booking row,
/// inbox record, seat reservation and resulting outbox message are committed
/// in one database transaction. This prevents the old failure mode where the
/// seat was committed first and the booking stayed stuck in Processing after
/// a worker crash.
/// </summary>
public class BookingCreatedConsumer(
    BookingDbContext db,
    ISeatLockStrategy seatLockStrategy,
    IBookingNotifier notifier,
    ILogger<BookingCreatedConsumer> logger) : IConsumer<BookingCreated>
{
    private static readonly TimeSpan SeatLockDuration = TimeSpan.FromMinutes(5);

    public async Task Consume(ConsumeContext<BookingCreated> context)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(context.CancellationToken);

        if (await InboxGuard.AlreadyProcessedAsync(
                db,
                GetMessageId(context),
                nameof(BookingCreatedConsumer),
                context.CancellationToken))
        {
            await transaction.RollbackAsync(context.CancellationToken);
            return;
        }

        var msg = context.Message;
        var booking = await db.Bookings.FindAsync([msg.BookingId], context.CancellationToken);

        if (booking is null)
        {
            logger.LogWarning("BookingCreated received for unknown booking {BookingId}", msg.BookingId);
            await db.SaveChangesAsync(context.CancellationToken);
            await transaction.CommitAsync(context.CancellationToken);
            return;
        }

        if (booking.Status != BookingStatus.Queued)
        {
            // A previously committed state transition means this is a
            // duplicate/redelivered event. The inbox row is committed with it.
            await db.SaveChangesAsync(context.CancellationToken);
            await transaction.CommitAsync(context.CancellationToken);
            return;
        }

        var correlationId = context.CorrelationId ?? Guid.NewGuid();
        booking.MoveTo(BookingStatus.Processing);
        BookingEventWriter.Add(db, booking, nameof(BookingStatus.Processing));

        var lockResult = await seatLockStrategy.TryLockSeatAsync(
            new SeatLockRequest(
                msg.ScheduleId,
                msg.CoachId,
                msg.SeatId,
                msg.BookingId,
                msg.UserId,
                SeatLockDuration),
            context.CancellationToken);

        if (lockResult.Outcome == SeatLockOutcome.Locked)
        {
            booking.SeatReservationId = lockResult.SeatReservationId;
            booking.MoveTo(BookingStatus.SeatLocked);
            BookingEventWriter.Add(db, booking, nameof(SeatLocked), new
            {
                booking.Id,
                lockResult.SeatReservationId,
                lockResult.ExpiresAtUtc
            });
            booking.MoveTo(BookingStatus.PaymentPending);
            BookingEventWriter.Add(db, booking, nameof(BookingStatus.PaymentPending));

            OutboxWriter.Enqueue(
                db,
                new PaymentRequested(
                    booking.Id,
                    booking.UserId,
                    booking.Amount,
                    correlationId,
                    DateTime.UtcNow),
                correlationId);
        }
        else if (lockResult.Outcome == SeatLockOutcome.SeatUnavailable)
        {
            booking.MoveTo(BookingStatus.Cancelled);

            BookingEventWriter.Add(db, booking, nameof(SeatLockFailed), new { lockResult.Reason });
            BookingEventWriter.Add(db, booking, nameof(BookingCancelled), new { Reason = lockResult.Reason ?? "Seat unavailable" });

            OutboxWriter.Enqueue(
                db,
                new BookingCancelled(
                    booking.Id,
                    lockResult.Reason ?? "Seat unavailable",
                    correlationId,
                    DateTime.UtcNow),
                correlationId);
        }
        else
        {
            // Transient failures must be retried by MassTransit. Do not turn a
            // Redis/DB outage into a permanent booking cancellation.
            throw new InvalidOperationException(
                $"Transient seat-lock failure for booking {booking.Id}: {lockResult.Reason}");
        }

        try
        {
            await db.SaveChangesAsync(context.CancellationToken);
            await transaction.CommitAsync(context.CancellationToken);
        }
        catch (DbUpdateException ex) when (IsSeatReservationUniqueViolation(ex))
        {
            await transaction.RollbackAsync(context.CancellationToken);
            db.ChangeTracker.Clear();

            // Another transaction won the seat. Record the cancellation in a
            // fresh transaction so this booking is not left in Processing.
            await CancelAfterSeatConflictAsync(msg.BookingId, correlationId, ex, context.CancellationToken);
            return;
        }

        await notifier.NotifyStatusChanged(
            booking.Id,
            booking.Status.ToString(),
            context.CancellationToken);
    }

    private async Task CancelAfterSeatConflictAsync(
        Guid bookingId,
        Guid correlationId,
        Exception exception,
        CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var booking = await db.Bookings.FindAsync([bookingId], ct);

        if (booking is null)
        {
            await transaction.RollbackAsync(ct);
            return;
        }

        if (booking.Status is BookingStatus.Queued or BookingStatus.Processing)
        {
            booking.MoveTo(BookingStatus.Cancelled);
            BookingEventWriter.Add(db, booking, nameof(SeatLockFailed), new
            {
                Reason = "Seat already reserved.",
                Conflict = true
            });
            BookingEventWriter.Add(db, booking, nameof(BookingCancelled), new { Reason = "Seat already reserved." });

            OutboxWriter.Enqueue(
                db,
                new BookingCancelled(
                    booking.Id,
                    "Seat already reserved.",
                    correlationId,
                    DateTime.UtcNow),
                correlationId);

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await notifier.NotifyStatusChanged(booking.Id, booking.Status.ToString(), ct);
            return;
        }

        logger.LogInformation(
            exception,
            "Seat conflict occurred for booking {BookingId}, but booking had already moved to {Status}",
            bookingId,
            booking.Status);
        await transaction.RollbackAsync(ct);
    }

    private static Guid GetMessageId(ConsumeContext<BookingCreated> context) =>
        context.MessageId ?? DeterministicMessageId(context.Message);

    private static Guid DeterministicMessageId(BookingCreated message) =>
        DeterministicGuid.Create(
            $"booking-created:{message.BookingId}:{message.CorrelationId}");

    private static bool IsSeatReservationUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "IX_SeatReservations_Active_Unique"
        };
}

internal static class DeterministicGuid
{
    public static Guid Create(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes[..16]);
    }
}
