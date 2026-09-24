using Microsoft.EntityFrameworkCore;
using SearchService.Models;

namespace SearchService;

public class SearchDbContext(DbContextOptions<SearchDbContext> options) : DbContext(options)
{
    public DbSet<Station> Stations => Set<Station>();
    public DbSet<Train> Trains => Set<Train>();
    public DbSet<TrainRoute> TrainRoutes => Set<TrainRoute>();
    public DbSet<Coach> Coaches => Set<Coach>();
    public DbSet<Seat> Seats => Set<Seat>();
    public DbSet<Schedule> Schedules => Set<Schedule>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Station>().HasIndex(s => s.Code).IsUnique();
        b.Entity<Train>().HasIndex(t => t.Number).IsUnique();

        b.Entity<TrainRoute>()
            .HasIndex(r => new { r.TrainId, r.SequenceNumber }).IsUnique();

        b.Entity<Coach>()
            .HasIndex(c => new { c.TrainId, c.Code }).IsUnique();

        b.Entity<Seat>()
            .HasIndex(s => new { s.CoachId, s.SeatNumber }).IsUnique();

        // The hot path for search: "trains from X to Y on date D".
        b.Entity<Schedule>()
            .HasIndex(s => new { s.TrainId, s.DepartureDate });
    }
}
