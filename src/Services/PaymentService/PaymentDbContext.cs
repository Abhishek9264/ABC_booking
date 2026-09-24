using Microsoft.EntityFrameworkCore;
using PaymentService.Domain;

namespace PaymentService;

public class PaymentDbContext(DbContextOptions<PaymentDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Payment>(e =>
        {
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(16);
            e.Property(x => x.RowVersion).IsRowVersion();
            e.HasIndex(x => x.BookingId).IsUnique();

            // Nullable + unique: only rows that HAVE a provider transaction
            // id are deduplicated on it (Postgres unique indexes treat NULLs
            // as distinct, so multiple NULLs before the provider responds
            // are fine).
            e.HasIndex(x => x.ProviderTransactionId).IsUnique();
        });
    }
}
