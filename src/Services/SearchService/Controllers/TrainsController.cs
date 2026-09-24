using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SearchService.Caching;
using SearchService.Models;

namespace SearchService.Controllers;

[ApiController]
[Route("api/trains")]
public class TrainsController(SearchDbContext db, ISearchCacheService cache, ILogger<TrainsController> logger) : ControllerBase
{
    // GET /api/trains/search?from=NDLS&to=BCT&date=2026-10-01
    [HttpGet("search")]
    public async Task<ActionResult<IReadOnlyList<ScheduleSearchResult>>> Search(
        [FromQuery] string from, [FromQuery] string to, [FromQuery] DateOnly date, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to))
            return BadRequest("from and to station codes are required.");

        var cacheKey = $"search:{from.ToUpperInvariant()}:{to.ToUpperInvariant()}:{date:yyyy-MM-dd}";

        // Availability changes far more often than schedule/train metadata,
        // so this cache entry intentionally has a SHORT TTL (the metadata
        // itself — trains, coaches — would deserve a much longer one, but
        // we keep a single entry here for simplicity in Phase 3).
        var result = await cache.GetOrCreateAsync(cacheKey, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(30),
            async () => await SearchFromDatabase(from, to, date, ct), ct);

        return Ok(result);
    }

    [HttpGet("{trainNumber}")]
    public async Task<ActionResult<Train>> GetByNumber(string trainNumber, CancellationToken ct)
    {
        var train = await db.Trains
            .Include(t => t.Coaches)
            .Include(t => t.Routes)
            .SingleOrDefaultAsync(t => t.Number == trainNumber, ct);

        return train is null ? NotFound() : Ok(train);
    }

    private async Task<List<ScheduleSearchResult>> SearchFromDatabase(string from, string to, DateOnly date, CancellationToken ct)
    {
        logger.LogInformation("Cache miss — querying Postgres for {From}->{To} on {Date}", from, to, date);

        // Simplified routing check: trains whose route includes both
        // stations with `from` appearing before `to`. A production version
        // would push this into SQL rather than materializing routes client
        // side; kept simple and readable for the portfolio scope.
        var fromCode = from.Trim().ToUpperInvariant();
        var toCode = to.Trim().ToUpperInvariant();

        var fromStationId = await db.Stations
            .Where(s => s.Code == fromCode)
            .Select(s => (Guid?)s.Id)
            .SingleOrDefaultAsync(ct);

        var toStationId = await db.Stations
            .Where(s => s.Code == toCode)
            .Select(s => (Guid?)s.Id)
            .SingleOrDefaultAsync(ct);

        if (fromStationId is null || toStationId is null)
            return new List<ScheduleSearchResult>();

        var schedules = await db.Schedules
            .Where(s =>
                s.DepartureDate == date &&
                s.Train.Routes.Any(r => r.StationId == fromStationId) &&
                s.Train.Routes.Any(r => r.StationId == toStationId))
            .Include(s => s.Train).ThenInclude(t => t.Routes)
            .Include(s => s.Train).ThenInclude(t => t.Coaches).ThenInclude(c => c.Seats)
            .ToListAsync(ct);

        var results = new List<ScheduleSearchResult>();
        foreach (var schedule in schedules)
        {
            var route = schedule.Train.Routes.OrderBy(r => r.SequenceNumber).ToList();
            var fromStop = route.FirstOrDefault(r => r.StationId == fromStationId.Value);
            var toStop = route.FirstOrDefault(r => r.StationId == toStationId.Value);

            // A train only serves this journey when the origin appears before
            // the destination in its ordered route.
            if (fromStop is null || toStop is null ||
                fromStop.SequenceNumber >= toStop.SequenceNumber)
                continue;

            var coachAvailability = schedule.Train.Coaches.Select(c => new CoachAvailability(
                c.Id,
                c.Code,
                c.Class.ToString(),
                c.Seats.Count,
                c.Seats.Count // Availability is a search-service read model; booking remains authoritative.
            )).ToList();

            results.Add(new ScheduleSearchResult(
                schedule.Id,
                schedule.Train.Number,
                schedule.Train.Name,
                schedule.DepartureDate,
                fromCode,
                toCode,
                coachAvailability));
        }

        return results;
    }
}
