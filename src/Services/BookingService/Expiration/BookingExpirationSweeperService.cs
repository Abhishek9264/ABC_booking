using BookingService.Domain;
using BookingService.Outbox;
using BookingService.Events;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Tatkal.Contracts;

namespace BookingService.Expiration;

/// <summary>
/// Spec section 6 / 17: seat locks and payment-pending bookings expire on
/// a persisted `ExpiresAtUtc`, never on an in-memory `Task.Delay` — so a
/// service restart mid-lock doesn't leave a seat stuck LOCKED forever.
/// This sweeper is the recovery mechanism: on every tick (and therefore
/// also immediately after any restart, once the first tick runs) it finds
/// everything whose lock/lifetime has expired and releases it, regardless
/// of which instance originally created it.
/// </summary>
public class BookingExpirationSweeperService(IServiceScopeFactory scopeFactory, ILogger<BookingExpirationSweeperService> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Expiration sweep failed; will retry next tick");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken ct)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BookingDbContext>();

        var now = DateTime.UtcNow;
        var expiredLocks = await db.SeatReservations
            .Where(r => r.Status == SeatReservationStatus.Locked && r.ExpiresAtUtc <= now)
            .Take(200)
            .ToListAsync(ct);

        if (expiredLocks.Count == 0) return;

        foreach (var reservation in expiredLocks)
        {
            reservation.Status = SeatReservationStatus.Expired;

            var booking = await db.Bookings.FindAsync([reservation.BookingId], ct);
            if (booking is not null && (booking.Status == BookingStatus.SeatLocked || booking.Status == BookingStatus.PaymentPending))
            {
                booking.MoveTo(BookingStatus.Expired);

                var correlationId = Guid.NewGuid();
                OutboxWriter.Enqueue(db, new BookingExpired(booking.Id, correlationId, now), correlationId);
                OutboxWriter.Enqueue(db, new SeatReleased(booking.Id, reservation.Id, correlationId, now), correlationId);

                BookingEventWriter.Add(db, booking, nameof(BookingExpired), new { booking.Id, reservation.SeatId });
            }
        }

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Expiration sweep released {Count} expired seat locks", expiredLocks.Count);
    }
}
