using Microsoft.EntityFrameworkCore;
using RelicDealFinder.Models.Market;

namespace RelicDealFinder.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<PrimePart> PrimeParts { get; set; }
    public DbSet<MarketRelic> Relics { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MarketRelic>().HasKey(r => r.Slug);
    }
}
