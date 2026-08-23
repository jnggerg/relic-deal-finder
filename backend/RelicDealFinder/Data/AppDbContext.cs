using Microsoft.EntityFrameworkCore;
using RelicDealFinder.Models.Market;
using RelicDealFinder.Models.WFCD;

namespace RelicDealFinder.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<PrimePart> PrimeParts { get; set; }
    public DbSet<Relic> Relics { get; set; }
}
