using BookingService.Domain;
using BookingService.Events;
using BookingService.Hubs;
using BookingService.Outbox;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Tatkal.Contracts;

namespace BookingService.Consumers;

/// <summary>Happy path: payment succeeded -> confirm the booking and mark the seat Confirmed.</summary>
public class PaymentSucceededConsumer(
    BookingDbContext db,
    IBookingNotifier notifier,
    ILogger<PaymentSucceededConsumer> logger) : IConsumer<PaymentSucceeded>
{
    public async Task Consume(ConsumeContext<PaymentSucceeded> context)
    {
        var messageId = context.MessageId ?? DeterministicMessageId.Create(
            $"payment-succeeded:{context.Message.BookingId}:{context.Message.PaymentId}");

        if (await InboxGuard.AlreadyProcessedAsync(db, messageId, nameof(PaymentSucceededConsumer), context.CancellationToken))
            return;

        var msg = context.Message;
        var booking = await db.Bookings.FindAsync([msg.BookingId], context.CancellationToken);

        if (booking is null)
        {
            logger.LogWarning("PaymentSucceeded received for unknown booking {BookingId}", msg.BookingId);
            await db.SaveChangesAsync(context.CancellationToken);
            return;
        }

        if (booking.Status is BookingStatus.Confirmed or BookingStatus.Cancelled or BookingStatus.Expired or BookingStatus.PaymentFailed)
        {
            // Terminal state. Mark the message as consumed, but never move a
            // terminal booking backwards or confirm an expired/cancelled seat.
            await db.SaveChangesAsync(context.CancellationToken);
            return;
        }

        if (booking.Status != BookingStatus.PaymentPending)
        {
            // The payment event arrived before the booking reached the state
            // in which it can be applied. Do not persist the inbox record;
            // MassTransit will retry the message.
            throw new InvalidOperationException(
                $"PaymentSucceeded for booking {booking.Id} arrived while status was {booking.Status}.");
        }

        booking.PaymentId = msg.PaymentId;
        booking.MoveTo(BookingStatus.Confirmed);

        if (booking.SeatReservationId is { } reservationId)
        {
            var reservation = await db.SeatReservations.FindAsync([reservationId], context.CancellationToken);
            if (reservation is null || reservation.Status != SeatReservationStatus.Locked)
                throw new InvalidOperationException($"Active seat reservation was not found for booking {booking.Id}.");

            reservation.Status = SeatReservationStatus.Confirmed;
        }

        var correlationId = context.CorrelationId ?? msg.CorrelationId;

        BookingEventWriter.Add(db, booking, nameof(BookingConfirmed), new { msg.PaymentId, msg.ProviderTransactionId });

        OutboxWriter.Enqueue(
            db,
            new BookingConfirmed(booking.Id, booking.UserId, correlationId, DateTime.UtcNow),
            correlationId);
        OutboxWriter.Enqueue(
            db,
            new NotifyUser(
                booking.UserId,
                "email",
                "booking-confirmed",
                new Dictionary<string, string> { ["bookingId"] = booking.Id.ToString() },
                correlationId,
                DateTime.UtcNow),
            correlationId);

        try
        {
            await db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Another state transition (for example expiry) won the race.
            // Let MassTransit retry; the subsequent attempt will observe the
            // terminal state and safely no-op.
            throw;
        }

        await notifier.NotifyStatusChanged(booking.Id, booking.Status.ToString(), context.CancellationToken);
        logger.LogInformation("Booking {BookingId} confirmed", booking.Id);
    }
}

/// <summary>
/// Compensation path: payment failed -> release the seat and cancel the booking.
/// </summary>
public class PaymentFailedConsumer(
    BookingDbContext db,
    IBookingNotifier notifier,
    ILogger<PaymentFailedConsumer> logger) : IConsumer<PaymentFailed>
{
    public async Task Consume(ConsumeContext<PaymentFailed> context)
    {
        var messageId = context.MessageId ?? DeterministicMessageId.Create(
            $"payment-failed:{context.Message.BookingId}:{context.Message.PaymentId}");

        if (await InboxGuard.AlreadyProcessedAsync(db, messageId, nameof(PaymentFailedConsumer), context.CancellationToken))
            return;

        var msg = context.Message;
        var booking = await db.Bookings.FindAsync([msg.BookingId], context.CancellationToken);

        if (booking is null)
        {
            logger.LogWarning("PaymentFailed received for unknown booking {BookingId}", msg.BookingId);
            await db.SaveChangesAsync(context.CancellationToken);
            return;
        }

        if (booking.Status is BookingStatus.Cancelled or BookingStatus.Expired or BookingStatus.PaymentFailed or BookingStatus.Confirmed)
        {
            await db.SaveChangesAsync(context.CancellationToken);
            return;
        }

        if (booking.Status != BookingStatus.PaymentPending)
        {
            throw new InvalidOperationException(
                $"PaymentFailed for booking {booking.Id} arrived while status was {booking.Status}.");
        }

        booking.MoveTo(BookingStatus.PaymentFailed);
        booking.MoveTo(BookingStatus.Cancelled);

        var correlationId = context.CorrelationId ?? msg.CorrelationId;

        if (booking.SeatReservationId is { } reservationId)
        {
            var reservation = await db.SeatReservations.FindAsync([reservationId], context.CancellationToken);
            if (reservation is not null && reservation.Status == SeatReservationStatus.Locked)
            {
                reservation.Status = SeatReservationStatus.Released;
                OutboxWriter.Enqueue(
                    db,
                    new SeatReleased(booking.Id, reservation.Id, correlationId, DateTime.UtcNow),
                    correlationId);
            }
        }

        BookingEventWriter.Add(db, booking, nameof(PaymentFailed), new { msg.PaymentId, msg.Reason });
        BookingEventWriter.Add(db, booking, nameof(BookingCancelled), new { Reason = $"Payment failed: {msg.Reason}" });

        OutboxWriter.Enqueue(
            db,
            new BookingCancelled(
                booking.Id,
                $"Payment failed: {msg.Reason}",
                correlationId,
                DateTime.UtcNow),
            correlationId);

        try
        {
            await db.SaveChangesAsync(context.CancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw;
        }

        await notifier.NotifyStatusChanged(booking.Id, booking.Status.ToString(), context.CancellationToken);
        logger.LogInformation("Booking {BookingId} cancelled after payment failure: {Reason}", booking.Id, msg.Reason);
    }
}

internal static class DeterministicMessageId
{
    public static Guid Create(string value)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value));
        return new Guid(bytes[..16]);
    }
}
