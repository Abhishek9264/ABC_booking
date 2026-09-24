using System.Security.Claims;
using System.Text.Json;
using BookingService.Domain;
using BookingService.Idempotency;
using BookingService.Outbox;
using BookingService.Events;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Microsoft.AspNetCore.Mvc;
using Tatkal.Contracts;

namespace BookingService.Controllers;

[ApiController]
[Route("api/bookings")]
[Authorize]
public class BookingsController(BookingDbContext db, IIdempotencyService idempotency, ILogger<BookingsController> logger) : ControllerBase
{
    /// <summary>
    /// Accepts a booking request and hands it to the queue — this endpoint
    /// does NOT lock the seat synchronously (spec section 8: don't let
    /// every request hit the database/seat-allocation logic directly).
    /// The actual seat lock happens in BookingCreatedConsumer; the client
    /// tracks progress via GET /api/bookings/{id} or the SignalR hub.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<BookingResponse>> Create(
        [FromBody] CreateBookingRequest request,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            return BadRequest("Idempotency-Key header is required.");
        if (idempotencyKey.Length > 128)
            return BadRequest("Idempotency-Key must be 128 characters or fewer.");

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var userId))
            return Unauthorized();

        if (request.ScheduleId == Guid.Empty || request.CoachId == Guid.Empty || request.SeatId == Guid.Empty)
            return BadRequest("ScheduleId, CoachId and SeatId are required.");
        if (request.Amount < 0)
            return BadRequest("Amount cannot be negative.");
        if (request.Passengers is null || request.Passengers.Count == 0 || request.Passengers.Count > 6)
            return BadRequest("Passengers must contain between 1 and 6 passengers.");
        if (request.Passengers.Any(p => string.IsNullOrWhiteSpace(p.FullName) || p.Age is < 0 or > 120 || string.IsNullOrWhiteSpace(p.Gender)))
            return BadRequest("Passenger details are invalid.");
        var bodyJson = JsonSerializer.Serialize(request);

        var check = await idempotency.CheckAsync(idempotencyKey, userId, bodyJson, ct);
        switch (check.Result)
        {
            case IdempotencyCheckResult.ConflictDifferentBody:
                return Conflict("Idempotency-Key is already associated with another request or user.");
            case IdempotencyCheckResult.DuplicateSameBody when check.ExistingBookingId is { } existingId:
            {
                var existing = await db.Bookings.FindAsync([existingId], ct);
                return Ok(new BookingResponse(
                    existingId,
                    existing?.Status.ToString() ?? "Unknown",
                    existing?.CreatedAtUtc ?? DateTime.UtcNow));
            }
            case IdempotencyCheckResult.DuplicateSameBody:
                // A previous request created the idempotency record but has
                // not completed the booking yet. Never create another booking.
                return Conflict("A booking request with this Idempotency-Key is already being processed.");
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        try
        {
            var booking = new Booking
            {
                UserId = userId,
                ScheduleId = request.ScheduleId,
                CoachId = request.CoachId,
                SeatId = request.SeatId,
                Amount = request.Amount,
                Status = BookingStatus.Initiated
            };

            booking.Passengers = request.Passengers.Select(p => new BookingPassenger
            {
                BookingId = booking.Id,
                FullName = p.FullName,
                Age = p.Age,
                Gender = p.Gender
            }).ToList();

            db.Bookings.Add(booking);
            booking.MoveTo(BookingStatus.Queued);

            BookingEventWriter.Add(db, booking, nameof(BookingCreated), request);
            BookingEventWriter.Add(db, booking, nameof(BookingQueued));

            var correlationId = Guid.NewGuid();
            OutboxWriter.Enqueue(
                db,
                new BookingCreated(
                    booking.Id,
                    userId,
                    request.ScheduleId,
                    request.CoachId,
                    request.SeatId,
                    correlationId,
                    DateTime.UtcNow),
                correlationId);

            await idempotency.CompleteAsync(idempotencyKey, booking.Id, ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            logger.LogInformation(
                "Booking {BookingId} accepted and queued for user {UserId}",
                booking.Id,
                userId);

            return Accepted(new BookingResponse(
                booking.Id,
                booking.Status.ToString(),
                booking.CreatedAtUtc));
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync(ct);

            // Another request won the race for this idempotency key.
            var existing = await db.IdempotencyRecords
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.IdempotencyKey == idempotencyKey, ct);

            if (existing?.UserId == userId &&
                existing.RequestHash == SHA256Hash(bodyJson) &&
                existing.BookingId is { } existingId)
            {
                var booking = await db.Bookings.FindAsync([existingId], ct);
                return Ok(new BookingResponse(
                    existingId,
                    booking?.Status.ToString() ?? "Unknown",
                    booking?.CreatedAtUtc ?? existing.CreatedAtUtc));
            }

            return Conflict("Idempotency-Key is already being processed.");
        }

        static bool IsUniqueViolation(DbUpdateException ex) =>
            ex.InnerException is PostgresException { SqlState: "23505" };

        static string SHA256Hash(string input)
        {
            var bytes = System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(input));
            return Convert.ToHexString(bytes);
        }
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BookingDetailResponse>> GetById(Guid id, CancellationToken ct)
    {
        var booking = await db.Bookings.FindAsync([id], ct);
        if (booking is null) return NotFound();

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var userId))
            return Unauthorized();
        if (booking.UserId != userId && !User.IsInRole("Admin")) return Forbid();

        return Ok(new BookingDetailResponse(booking.Id, booking.UserId, booking.Status.ToString(), booking.Amount, booking.CreatedAtUtc, booking.UpdatedAtUtc));
    }

    [HttpPost("{id:guid}/cancel")]
    public async Task<IActionResult> Cancel(Guid id, CancellationToken ct)
    {
        var booking = await db.Bookings.FindAsync([id], ct);
        if (booking is null) return NotFound();

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"), out var userId))
            return Unauthorized();
        if (booking.UserId != userId && !User.IsInRole("Admin")) return Forbid();

        if (!StateMachine.BookingStateMachine.CanTransition(booking.Status, BookingStatus.Cancelled))
            return Conflict($"Booking cannot be cancelled from status {booking.Status}.");

        booking.MoveTo(BookingStatus.Cancelled);

        var correlationId = Guid.NewGuid();
        if (booking.SeatReservationId is { } reservationId)
        {
            var reservation = await db.SeatReservations.FindAsync([reservationId], ct);
            if (reservation is not null && reservation.Status == SeatReservationStatus.Locked)
            {
                reservation.Status = SeatReservationStatus.Released;
                OutboxWriter.Enqueue(db, new SeatReleased(booking.Id, reservation.Id, correlationId, DateTime.UtcNow), correlationId);
            }
        }

        
        BookingEventWriter.Add(db, booking, nameof(BookingCancelled), new { Reason = "Cancelled by user" });
        OutboxWriter.Enqueue(db, new BookingCancelled(booking.Id, "Cancelled by user", correlationId, DateTime.UtcNow), correlationId);

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict("Booking changed while cancellation was being processed. Please retry.");
        }
        return NoContent();
    }
}
