using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Controllers;

public record BookingEventDto(long SequenceNumber, string EventType, string PayloadJson, DateTime OccurredAtUtc);

/// <summary>
/// Read side over the append-only BookingEvents stream. Access is restricted
/// to the booking owner or an Admin; the event stream may contain passenger,
/// payment and lifecycle details that should not be enumerable by other users.
/// </summary>
[ApiController]
[Route("api/bookings/{bookingId:guid}/events")]
[Authorize]
public class BookingEventsController(BookingDbContext db) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BookingEventDto>>> GetStream(Guid bookingId, CancellationToken ct)
    {
        var bookingUserId = await db.Bookings
            .AsNoTracking()
            .Where(x => x.Id == bookingId)
            .Select(x => (Guid?)x.UserId)
            .SingleOrDefaultAsync(ct);

        if (bookingUserId is null)
            return NotFound();

        if (!Guid.TryParse(
                User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub"),
                out var userId))
            return Unauthorized();

        if (bookingUserId.Value != userId && !User.IsInRole("Admin"))
            return Forbid();

        var events = await db.BookingEvents
            .AsNoTracking()
            .Where(e => e.BookingId == bookingId)
            .OrderBy(e => e.SequenceNumber)
            .Select(e => new BookingEventDto(e.SequenceNumber, e.EventType, e.PayloadJson, e.OccurredAtUtc))
            .ToListAsync(ct);

        return Ok(events);
    }
}
