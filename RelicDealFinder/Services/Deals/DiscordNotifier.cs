using System.Net;
using Microsoft.Extensions.Options;
using RelicDealFinder.Models.Dashboard;

namespace RelicDealFinder.Services.Deals;

// Posts deals to a Discord channel webhook; does nothing while DiscordWebhookUrl is empty
public class DiscordNotifier(
    HttpClient client,
    IOptions<DealOptions> options,
    ILogger<DiscordNotifier> logger
)
{
    // one message per relic poll; at most 5 orders, so it stays under Discord's 2000 character limit
    public async Task SendDealsAsync(IReadOnlyList<Deal> deals, CancellationToken ct)
    {
        var url = options.Value.DiscordWebhookUrl;
        if (string.IsNullOrWhiteSpace(url) || deals.Count == 0)
            return;

        var payload = new { content = string.Join("\n\n", deals.Select(Format)) };

        try
        {
            using var response = await client.PostAsJsonAsync(url, payload, ct);

            // a burst of new deals (e.g. the first cycle) can outrun the webhook limit; retry once
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                await Task.Delay(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2), ct);
                using var retry = await client.PostAsJsonAsync(url, payload, ct);
                retry.EnsureSuccessStatusCode();
            }
            else
                response.EnsureSuccessStatusCode();
        }
        // a failed notification must not stop the scan; shortcut: the deal is not re-sent, add a queue if that matters
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "Discord notification failed: {Message}", ex.Message);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            logger.LogWarning(ex, "Discord notification timed out");
        }
    }

    private static string Format(Deal d) =>
        $"""
        **New deal found!**
        Relic: {d.RelicName} {d.Refinement}
        Ask: {d.Ask}p · EV: {d.Ev:0.0}p · Profit: +{d.Profit:0.0}p
        Message the seller: `{d.Whisper}`
        """;
}
