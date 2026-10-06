
namespace RelicDealFinder.Components;

public static class DisplayFormat
{
    // "8s", "4m 12s", "1h 5m"
    public static string Duration(TimeSpan d) =>
        d.TotalHours >= 1 ? $"{(int)d.TotalHours}h {d.Minutes}m"
        : d.TotalMinutes >= 1 ? $"{(int)d.TotalMinutes}m {d.Seconds}s"
        : $"{Math.Max(0, (int)d.TotalSeconds)}s";

    // "1,204
    public static string Number(int n) => n.ToString("N0");

    // "0 errors", "1 retry"
    public static string Count(int n, string singular, string plural) =>
        $"{Number(n)} {(n == 1 ? singular : plural)}";
}
