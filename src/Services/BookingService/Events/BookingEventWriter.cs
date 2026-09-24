using System.Text.Json;
using BookingService.Domain;

namespace BookingService.Events;

public static class BookingEventWriter
{
    public static void Add(BookingDbContext db, Booking booking, string eventType, object? payload = null)
    {
        db.BookingEvents.Add(new BookingEvent
        {
            BookingId = booking.Id,
            EventType = eventType,
            PayloadJson = JsonSerializer.Serialize(payload ?? new { }),
            OccurredAtUtc = DateTime.UtcNow
        });
    }
}
