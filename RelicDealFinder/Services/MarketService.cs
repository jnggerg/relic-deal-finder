using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Polly;
using Polly.Registry;
using RelicDealFinder.Models.Market;
using RelicDealFinder.Models.Market.Order;

namespace RelicDealFinder.Services;

public class MarketService(
    HttpClient marketClient,
    ILogger<MarketService> logger,
    ResiliencePipelineProvider<string> pipelines
)
{
    private readonly ResiliencePipeline _rateLimiter = pipelines.GetPipeline("wfm");

    public async Task<List<MarketItem>?> GetAllMarketItems(CancellationToken ct = default)
    {
        logger.LogInformation("Fetching all market items...");
        var response = await marketClient.GetFromJsonAsync<MarketItemsResponse>("items", ct);
        return response?.Data;
    }

    // order types and relic subtypes come as lowercase strings
    private static readonly JsonSerializerOptions OrderJsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    /*
     *  Returns the top 5 sell orders from online users, cheapest first, or null if the request failed.
     *  Relic orders of every refinement are mixed into the same 5.
     */
    public async Task<List<MarketOrder>?> GetTopSellOrders(string slug, CancellationToken ct)
    {
        try
        {
            var orders = await _rateLimiter.ExecuteAsync(
                async token =>
                    await marketClient.GetFromJsonAsync<MarketTopOrderResponse>(
                        $"orders/item/{slug}/top",
                        OrderJsonOptions,
                        token
                    ),
                ct
            );

            return orders?.data.sell;
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(
                ex,
                "HTTP error fetching orders for {Slug}: {StatusCode} - {Message}",
                slug,
                ex.StatusCode,
                ex.Message
            );
            // there's no retry strategy in the wfm pipeline, so back off before the caller's next request
            if (ex.StatusCode == HttpStatusCode.TooManyRequests)
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex, "JSON parsing failed for {Slug}: {Message}", slug, ex.Message);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Request timed out fetching orders for {Slug}", slug);
        }

        return null;
    }
}
