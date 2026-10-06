using Microsoft.EntityFrameworkCore;
using RelicDealFinder.Models.Market;
using RelicDealFinder.Models.Refresh;

namespace RelicDealFinder.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<PrimePart> PrimeParts { get; set; }
    public DbSet<MarketRelic> Relics { get; set; }
    public DbSet<RefreshRun> RefreshRuns { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MarketRelic>().HasKey(r => r.Slug);
    }
}
