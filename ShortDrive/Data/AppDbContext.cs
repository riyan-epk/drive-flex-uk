using Microsoft.EntityFrameworkCore;
using ShortDrive.Models;

namespace ShortDrive.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<Driver> Drivers => Set<Driver>();
    public DbSet<Quote> Quotes => Set<Quote>();
    public DbSet<Lead> Leads => Set<Lead>();
    public DbSet<PricingSettings> PricingSettings => Set<PricingSettings>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Vehicle>()
            .HasIndex(v => v.Registration);

        modelBuilder.Entity<Quote>()
            .Property(q => q.TotalPremium).HasPrecision(18, 2);

        modelBuilder.Entity<Quote>()
            .Property(q => q.BaseRate).HasPrecision(18, 2);

        modelBuilder.Entity<Quote>()
            .Property(q => q.DurationMultiplier).HasPrecision(18, 4);

        modelBuilder.Entity<Quote>()
            .Property(q => q.RiskMultiplier).HasPrecision(18, 4);
    }
}
