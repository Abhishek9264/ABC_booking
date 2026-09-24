using Microsoft.EntityFrameworkCore;
using UserService.Models;

namespace UserService;

public class UserDbContext(DbContextOptions<UserDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<PassengerProfile> Passengers => Set<PassengerProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Email).HasMaxLength(256).IsRequired();
            e.Property(u => u.Role).HasConversion<string>().HasMaxLength(16);
        });

        modelBuilder.Entity<PassengerProfile>(e =>
        {
            e.HasIndex(p => p.UserId);
        });
    }
}
