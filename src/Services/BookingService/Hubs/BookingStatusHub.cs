using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BookingService.Hubs;

/// <summary>
/// Authenticated SignalR hub. A client may join only its own booking group
/// (or an Admin may join any group). Without this check, any authenticated
/// user could subscribe to another user's booking status updates by guessing
/// a booking GUID.
/// </summary>
[Authorize]
public class BookingStatusHub(BookingDbContext db) : Hub
{
    public async Task JoinBookingGroup(string bookingId)
    {
        if (!Guid.TryParse(bookingId, out var id))
            throw new HubException("Invalid booking id.");

        var booking = await db.Bookings
            .AsNoTracking()
            .Where(x => x.Id == id)
            .Select(x => new { x.UserId })
            .SingleOrDefaultAsync(Context.ConnectionAborted);

        if (booking is null)
            throw new HubException("Booking not found.");

        if (!Guid.TryParse(
                Context.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? Context.User?.FindFirstValue("sub"),
                out var userId))
            throw new HubException("Unauthorized.");

        if (booking.UserId != userId && !(Context.User?.IsInRole("Admin") ?? false))
            throw new HubException("You are not allowed to subscribe to this booking.");

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(id.ToString()), Context.ConnectionAborted);
    }

    public Task LeaveBookingGroup(string bookingId) =>
        Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(bookingId), Context.ConnectionAborted);

    public static string GroupName(string bookingId) => $"booking:{bookingId}";
}

public interface IBookingNotifier
{
    Task NotifyStatusChanged(Guid bookingId, string status, CancellationToken ct = default);
}

public class BookingNotifier(IHubContext<BookingStatusHub> hub) : IBookingNotifier
{
    public Task NotifyStatusChanged(Guid bookingId, string status, CancellationToken ct = default) =>
        hub.Clients.Group(BookingStatusHub.GroupName(bookingId.ToString()))
           .SendAsync("BookingStatusChanged", new { bookingId, status }, ct);
}
