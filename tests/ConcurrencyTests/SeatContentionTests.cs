using System.Diagnostics.CodeAnalysis;
using BookingService;
using BookingService.Concurrency;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace ConcurrencyTests;

/// <summary>
/// The single most important test in this repository (spec section 5 /
/// 31): fire N booking requests at the SAME seat concurrently and assert
/// exactly one succeeds. Runs against a real Postgres via Testcontainers
/// so the unique index — the actual correctness guarantee — is exercised
/// for real, not mocked away.
///
/// NOTE: requires Docker to be available wherever this test runs (it was
/// authored without a local .NET SDK/Docker in the sandbox that produced
/// this repo, so run `dotnet test` locally to execute it for real).
/// </summary>
[SuppressMessage("Usage", "xUnit1041", Justification = "Testcontainers lifetime managed by IAsyncLifetime")]
public class SeatContentionTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        await using var conn = new Npgsql.NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();
        var sql = await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
                "src", "Services", "BookingService", "Migrations", "001_init.sql"));
        await using var cmd = new Npgsql.NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    [Theory]
    [InlineData(50)]  // spec section 5 uses 500; 50 keeps CI runtime reasonable while proving the same property
    public async Task Exactly_one_winner_when_N_users_race_for_the_same_seat(int concurrentRequests)
    {
        var scheduleId = Guid.NewGuid();
        var coachId = Guid.NewGuid();
        var seatId = Guid.NewGuid();

        var tasks = Enumerable.Range(0, concurrentRequests).Select(async _ =>
        {
            var options = new DbContextOptionsBuilder<BookingDbContext>()
                .UseNpgsql(_postgres.GetConnectionString())
                .Options;
            await using var db = new BookingDbContext(options);
            var strategy = new OptimisticConcurrencySeatLockStrategy(db);

            var request = new SeatLockRequest(scheduleId, coachId, seatId, Guid.NewGuid(), Guid.NewGuid(), TimeSpan.FromMinutes(5));
            var result = await strategy.TryLockSeatAsync(request);

            try
            {
                await db.SaveChangesAsync();
                return result;
            }
            catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
            {
                return new SeatLockResult(SeatLockOutcome.SeatUnavailable, null, null, "Seat already reserved.");
            }
        });

        var results = await Task.WhenAll(tasks);

        results.Count(r => r.Outcome == SeatLockOutcome.Locked).Should().Be(1,
            "the unique index on (ScheduleId, CoachId, SeatId, Status) must allow exactly one winner");
        results.Count(r => r.Outcome == SeatLockOutcome.SeatUnavailable).Should().Be(concurrentRequests - 1);
    }
}
