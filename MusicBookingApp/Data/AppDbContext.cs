using Microsoft.EntityFrameworkCore;
using MusicBookingApp.Models;

namespace MusicBookingApp.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Artist> Artists { get; set; }
    public DbSet<Event> Events { get; set; }
    public DbSet<Booking> Bookings { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Artist>()
            .HasIndex(a => a.Email)
            .IsUnique();
    }
}
