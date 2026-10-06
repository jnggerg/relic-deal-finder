using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using RelicDealFinder.Data;
using RelicDealFinder.Models.Market;
using RelicDealFinder.Enums.Refresh;
using RelicDealFinder.Models.Refresh;

namespace RelicDealFinder.Services.Refresh;

/*  Builds the new data in a separate staging database (same migrations, so same schema),
 *  then copies it over the live tables in a single transaction at the end.
 *  Until that commit, readers keep seeing the previous snapshot; if anything fails,
 *  the staging file is simply deleted and the live tables are untouched.
 */
public sealed class RefreshPipeline(
    MarketService marketService,
    StatisticsService statsService,
    WfcdService wfcdService,
    AppDbContext db,
    IConfiguration configuration
) : IRefreshPipeline
{
    private static readonly RefreshStepDefinition[] Steps =
    [
        new("Staging database", "Create an empty copy of the schema", "Preparing staging database", "Staging", 1),
        new("Item catalogue", "All items from warframe.market, filtered to prime parts and relics", "Fetching item catalogue", "Catalogue", 3),
        new("Part prices", "7-day VWAP for every prime part", "Pricing Prime parts", "7-day VWAP lookups", 400),
        new("Save prime parts", "Write priced parts to staging", "Saving prime parts", "Parts saved", 1),
        new("Relic drop tables", "WFCD drop data, rarity normalised from intact drop chance", "Fetching relic drop tables", "Relics", 3),
        new("Match rewards", "WFCD reward names → market slugs", "Matching relic rewards", "Relics matched", 1),
        new("Expected values", "Intact and Radiant EV per relic", "Calculating expected values", "Relics valued", 1),
        new("Save relics", "Write relics to staging", "Saving relics", "Relics saved", 1),
        new("Publish", "Swap staging into the live tables in one transaction", "Publishing new snapshot", "Tables", 1),
    ];

    public async Task<RefreshTotals> RunAsync(IRefreshReporter reporter, CancellationToken ct)
    {
        reporter.Begin(Steps);

        var (stagingPath, stagingConnectionString) = StagingDatabase();
        DeleteStagingFile(stagingPath); // leftover from a crashed run

        try
        {
            await using var staging = new AppDbContext(
                new DbContextOptionsBuilder<AppDbContext>()
                    .UseSqlite(stagingConnectionString, x => x.MaxBatchSize(100))
                    .Options
            );

            // 1. Staging database
            reporter.StartStep(0, 0);
            await staging.Database.MigrateAsync(ct);
            reporter.Log(LogEntryLevel.Info, $"staging database ready at {Path.GetFileName(stagingPath)}");
            reporter.CompleteStep();

            // 2. Item catalogue
            reporter.StartStep(1, 0);
            var items =
                await marketService.GetAllMarketItems(ct)
                ?? throw new InvalidOperationException("warframe.market returned no items");

            List<PrimePart> primeParts =
            [
                .. items
                    .Where(i => i.Tags.Contains("prime") && !i.Tags.Contains("set"))
                    .Select(i => new PrimePart
                    {
                        Id = i.Id,
                        Slug = i.Slug,
                        Tags = i.Tags,
                        Vaulted = i.Vaulted,
                        GameRef = i.GameRef,
                    }),
            ];
            
            // drop requiem relics, as we only care about prime part rewards
            List<MarketItem> marketRelics =
            [
                .. items.Where(i => i.Tags.Contains("relic") && !i.Tags.Contains("requiem")),
            ];

            reporter.Log(LogEntryLevel.Info, "GET /v2/items 200");
            reporter.Log(
                LogEntryLevel.Info,
                $"catalogue: {N(items.Count)} items · {N(primeParts.Count)} prime parts · {N(marketRelics.Count)} relics"
            );
            reporter.CompleteStep();

            // 3. Part prices
            reporter.StartStep(2, primeParts.Count);
            await statsService.AddPriceToItems(
                primeParts,
                part =>
                {
                    if (part.Price is > 0)
                        reporter.Log(LogEntryLevel.Info, $"{part.Slug} {part.Price.Value.ToString("0.#")}p");
                    else
                        reporter.Log(LogEntryLevel.Skip, $"skip {part.Slug} (no stats or no 7-day trades)");
                    reporter.Advance();
                },
                ct
            );
            reporter.CompleteStep();

            // 4. Save prime parts
            reporter.StartStep(3, primeParts.Count);
            staging.PrimeParts.AddRange(primeParts);
            await staging.SaveChangesAsync(ct);
            reporter.Log(LogEntryLevel.Info, $"saved {N(primeParts.Count)} prime parts to staging");
            reporter.CompleteStep();

            // 5. Relic drop tables
            reporter.StartStep(4, 0);
            var wfcdRelics =
                await wfcdService.GetAllWfcdRelics(ct)
                ?? throw new InvalidOperationException("WFCD returned no relics");
            reporter.Log(LogEntryLevel.Info, $"WFCD: {N(wfcdRelics.Count)} intact relics, rarity normalised");
            reporter.CompleteStep();

            // 6. Match rewards
            reporter.StartStep(5, wfcdRelics.Count);
            var relics =
                wfcdService.MatchMarketIdsToRelicRewards(wfcdRelics, marketRelics, primeParts)
                ?? throw new InvalidOperationException("No prime parts to match relic rewards against");
            var unmatched = wfcdRelics.Count - relics.Count;
            reporter.Log(
                unmatched > 0 ? LogEntryLevel.Warning : LogEntryLevel.Info,
                $"matched {N(relics.Count)} relics to market slugs" + (unmatched > 0 ? $" · {N(unmatched)} without a market listing" : "")
            );
            reporter.CompleteStep();

            // 7. Expected values
            reporter.StartStep(6, relics.Count);
            statsService.ComputeAllRelicValues(relics, primeParts);
            reporter.Log(LogEntryLevel.Info, $"EV computed for {N(relics.Count)} relics");
            reporter.CompleteStep();

            // 8. Save relics
            reporter.StartStep(7, relics.Count);
            staging.Relics.AddRange(relics);
            await staging.SaveChangesAsync(ct);
            reporter.Log(LogEntryLevel.Info, $"saved {N(relics.Count)} relics to staging");
            reporter.CompleteStep();

            // 9. Publish
            reporter.StartStep(8, 0);
            await PublishAsync(stagingPath, ct);
            reporter.Log(LogEntryLevel.Info, "live tables replaced");
            reporter.CompleteStep();

            return new RefreshTotals(primeParts.Count, relics.Count);
        }
        finally
        {
            DeleteStagingFile(stagingPath);
        }
    }

    /*  ATTACH can't run inside a transaction, so: attach, then replace both tables in one
     *  transaction, then detach. The connection has to stay open across all of it.
     */
    private async Task PublishAsync(string stagingPath, CancellationToken ct)
    {
        // we use this instead of hardcoding table names, in case of future changes
        var tables = new[] { typeof(PrimePart), typeof(MarketRelic) }
            .Select(t => db.Model.FindEntityType(t)!.GetTableName()!)
            .ToArray();

        await db.Database.OpenConnectionAsync(ct);
        try
        {
            await db.Database.ExecuteSqlRawAsync("ATTACH DATABASE {0} AS staging", [stagingPath], ct);
            try
            {
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                foreach (var table in tables)
                {
                    // both databases are built by the same migrations, so column order matches
                    var delete = $"DELETE FROM main.\"{table}\"";
                    var copy = $"INSERT INTO main.\"{table}\" SELECT * FROM staging.\"{table}\"";
                    await db.Database.ExecuteSqlRawAsync(delete, ct);
                    await db.Database.ExecuteSqlRawAsync(copy, ct);
                }
                await transaction.CommitAsync(ct);
            }
            finally
            {
                await db.Database.ExecuteSqlRawAsync("DETACH DATABASE staging", CancellationToken.None);
            }
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    // app.db -> app.staging.db next to it. Pooling off so the file isn't held open after the run.
    private (string Path, string ConnectionString) StagingDatabase()
    {
        var live = new SqliteConnectionStringBuilder(
            configuration.GetConnectionString("Default")
                ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured")
        );
        var livePath = Path.GetFullPath(live.DataSource);
        var stagingPath = Path.Combine(
            Path.GetDirectoryName(livePath)!,
            $"{Path.GetFileNameWithoutExtension(livePath)}.staging{Path.GetExtension(livePath)}"
        );

        var staging = new SqliteConnectionStringBuilder { DataSource = stagingPath, Pooling = false };
        return (stagingPath, staging.ConnectionString);
    }

    private static void DeleteStagingFile(string path)
    {
        foreach (var file in new[] { path, $"{path}-journal", $"{path}-wal", $"{path}-shm" })
        {
            if (File.Exists(file))
                File.Delete(file);
        }
    }

    private static string N(int n) => n.ToString("N0");
}
