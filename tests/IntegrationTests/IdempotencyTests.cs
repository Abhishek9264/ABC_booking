using BookingService;
using BookingService.Idempotency;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace IntegrationTests;

/// <summary>
/// Spec section 7 / failure scenario 8: the same Idempotency-Key sent
/// multiple times must not create duplicate bookings, and reusing a key
/// with a DIFFERENT request body must be rejected rather than silently
/// processed. Uses EF Core's InMemory provider for speed — the unique-key
/// behavior being tested lives in IdempotencyService's logic, not in a
/// Postgres-specific constraint (that part is covered by
/// SeatContentionTests instead).
/// </summary>
public class IdempotencyTests
{
    private static BookingDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<BookingDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task Same_key_same_body_returns_existing_booking_without_creating_a_new_record()
    {
        await using var db = CreateDb();
        var service = new IdempotencyService(db);
        var userId = Guid.NewGuid();
        var key = "idem-key-1";
        var body = """{"scheduleId":"11111111-1111-1111-1111-111111111111"}""";

        var first = await service.CheckAsync(key, userId, body);
        first.Result.Should().Be(IdempotencyCheckResult.NewRequest);

        var bookingId = Guid.NewGuid();
        await service.CompleteAsync(key, bookingId);

        var second = await service.CheckAsync(key, userId, body);
        second.Result.Should().Be(IdempotencyCheckResult.DuplicateSameBody);
        second.ExistingBookingId.Should().Be(bookingId);

        (await db.IdempotencyRecords.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Same_key_different_body_is_rejected_as_a_conflict()
    {
        await using var db = CreateDb();
        var service = new IdempotencyService(db);
        var userId = Guid.NewGuid();
        var key = "idem-key-2";

        await service.CheckAsync(key, userId, """{"scheduleId":"A"}""");

        var conflict = await service.CheckAsync(key, userId, """{"scheduleId":"B"}""");
        conflict.Result.Should().Be(IdempotencyCheckResult.ConflictDifferentBody);
    }
}
