namespace BookingService;

public record PassengerDto(string FullName, int Age, string Gender);
public record CreateBookingRequest(Guid ScheduleId, Guid CoachId, Guid SeatId, decimal Amount, IReadOnlyList<PassengerDto> Passengers);
public record BookingResponse(Guid BookingId, string Status, DateTime CreatedAtUtc);
public record BookingDetailResponse(Guid BookingId, Guid UserId, string Status, decimal Amount, DateTime CreatedAtUtc, DateTime? UpdatedAtUtc);
