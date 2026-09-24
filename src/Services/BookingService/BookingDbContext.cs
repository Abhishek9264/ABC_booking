using BookingService.Domain;
using BookingService.Events;
using BookingService.Idempotency;
using BookingService.Outbox;
using Microsoft.EntityFrameworkCore;

namespace BookingService;

public class BookingDbContext(DbContextOptions<BookingDbContext> options) : DbContext(options)
{
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<BookingPassenger> BookingPassengers => Set<BookingPassenger>();
    public DbSet<SeatReservation> SeatReservations => Set<SeatReservation>();
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    public DbSet<BookingEvent> BookingEvents => Set<BookingEvent>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Booking>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(24);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.UserId);
        });

        b.Entity<SeatReservation>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.RowVersion).IsRowVersion();

            // THE correctness guarantee referenced throughout ADR-004: at
            // most one Active-equivalent (Locked or Confirmed) reservation
            // per (schedule, coach, seat), enforced by Postgres itself,
            // independent of whatever locking strategy runs in front of it.
            e.HasIndex(x => new { x.ScheduleId, x.CoachId, x.SeatId, x.Status })
             .IsUnique()
             .HasFilter("\"Status\" IN ('Locked','Confirmed')");

            e.HasIndex(x => x.ExpiresAtUtc); // for the expiration sweeper
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.HasIndex(x => x.ProcessedAtUtc);
            e.HasIndex(x => new { x.Status, x.CreatedAtUtc });
            e.HasIndex(x => x.ClaimedAtUtc);
        });

        b.Entity<InboxMessage>(e =>
        {
            e.HasKey(x => new { x.MessageId, x.ConsumerName });
            e.HasIndex(x => x.ProcessedAtUtc);
        });

        b.Entity<IdempotencyRecord>(e =>
        {
            e.HasKey(x => x.IdempotencyKey);
            e.HasIndex(x => x.ExpiresAtUtc);
        });

        b.Entity<BookingEvent>(e =>
        {
            e.HasKey(x => x.SequenceNumber);
            e.HasIndex(x => new { x.BookingId, x.SequenceNumber });
        });
    }
}
