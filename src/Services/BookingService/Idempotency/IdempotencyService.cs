using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace BookingService.Idempotency;

public class IdempotencyService(BookingDbContext db) : IIdempotencyService
{
    public async Task<IdempotencyCheck> CheckAsync(
        string idempotencyKey,
        Guid userId,
        string requestBodyJson,
        CancellationToken ct = default)
    {
        var hash = Hash(requestBodyJson);
        var existing = await db.IdempotencyRecords.FindAsync([idempotencyKey], ct);

        if (existing is null)
        {
            // Do not SaveChanges here. The caller must persist the idempotency
            // record in the SAME transaction as the booking/outbox rows.
            db.IdempotencyRecords.Add(new IdempotencyRecord
            {
                IdempotencyKey = idempotencyKey,
                UserId = userId,
                RequestHash = hash,
                Status = "Pending",
                ExpiresAtUtc = DateTime.UtcNow.AddHours(24)
            });

            return new IdempotencyCheck(IdempotencyCheckResult.NewRequest, null);
        }

        // An idempotency key belongs to the user that created it. Reusing
        // another user's key must never expose that user's booking.
        if (existing.UserId != userId)
            return new IdempotencyCheck(IdempotencyCheckResult.ConflictDifferentBody, null);

        if (existing.RequestHash != hash)
            return new IdempotencyCheck(IdempotencyCheckResult.ConflictDifferentBody, null);

        return new IdempotencyCheck(IdempotencyCheckResult.DuplicateSameBody, existing.BookingId);
    }

    public async Task CompleteAsync(string idempotencyKey, Guid bookingId, CancellationToken ct = default)
    {
        var record = await db.IdempotencyRecords.FindAsync([idempotencyKey], ct);
        if (record is null)
            throw new InvalidOperationException($"Idempotency record '{idempotencyKey}' was not found.");

        record.BookingId = bookingId;
        record.Status = "Completed";
        // The caller owns the transaction and SaveChanges call.
    }

    private static string Hash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
