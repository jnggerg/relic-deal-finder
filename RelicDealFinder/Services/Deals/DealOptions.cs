namespace RelicDealFinder.Services.Deals;

// bound from the "Deals" section of appsettings.json
public class DealOptions
{
    // only relics with at least this (squad) Radiant EV get polled
    public double RadEvThreshold { get; set; } = 20;
    public double MinProfit { get; set; } = 3;

    // usual market gap between an Intact and a Radiant relic, added to non-Radiant asks
    public int IntactToRadiantPlat { get; set; } = 2;
    public TimeSpan CycleGap { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan MaxDataAge { get; set; } = TimeSpan.FromDays(1);

    // keep scanning with no Deals page open, so notifications arrive while away
    public bool ScanWithoutViewers { get; set; } = true;

    // set in user-secrets / env (Deals__DiscordWebhookUrl), never in appsettings; empty disables notifications
    public string? DiscordWebhookUrl { get; set; }
    public int NotificationProfitThreshold { get; set; } = 15;
}
