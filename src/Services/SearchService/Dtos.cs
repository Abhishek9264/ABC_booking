namespace SearchService;

public record ScheduleSearchResult(
    Guid ScheduleId,
    string TrainNumber,
    string TrainName,
    DateOnly DepartureDate,
    string FromStationCode,
    string ToStationCode,
    IReadOnlyList<CoachAvailability> Coaches);

public record CoachAvailability(Guid CoachId, string Code, string Class, int TotalSeats, int AvailableSeats);
