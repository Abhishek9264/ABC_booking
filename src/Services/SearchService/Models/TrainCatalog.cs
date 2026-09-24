namespace SearchService.Models;

public class Station
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Code { get; set; } = default!;   // e.g. "NDLS"
    public string Name { get; set; } = default!;
    public string City { get; set; } = default!;
}

public class Train
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Number { get; set; } = default!; // e.g. "12951"
    public string Name { get; set; } = default!;
    public ICollection<TrainRoute> Routes { get; set; } = new List<TrainRoute>();
    public ICollection<Coach> Coaches { get; set; } = new List<Coach>();
}

/// <summary>One ordered stop on a train's route.</summary>
public class TrainRoute
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TrainId { get; set; }
    public Guid StationId { get; set; }
    public int SequenceNumber { get; set; }
    public TimeSpan ArrivalOffset { get; set; }   // offset from train's day-0 departure
    public TimeSpan DepartureOffset { get; set; }
}

public enum CoachClass { Sleeper, Ac3Tier, Ac2Tier, AcFirstClass, ChairCar }

public class Coach
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TrainId { get; set; }
    public string Code { get; set; } = default!; // e.g. "B1"
    public CoachClass Class { get; set; }
    public int SeatCount { get; set; }
    public ICollection<Seat> Seats { get; set; } = new List<Seat>();
}

public class Seat
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid CoachId { get; set; }
    public int SeatNumber { get; set; }
    public string SeatType { get; set; } = "Regular"; // Regular / LowerBerth / UpperBerth / SideLower...
}

/// <summary>A concrete run of a train on a specific calendar date.</summary>
public class Schedule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TrainId { get; set; }
    public DateOnly DepartureDate { get; set; }
    public bool TatkalWindowOpen { get; set; }
    public DateTime? TatkalOpensAtUtc { get; set; }
}
