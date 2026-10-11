# Relic Deal Finder

A .NET web app that looks for underpriced Void Relics in Warframe. It works out what each relic is worth on average from live market data, then compares that with what players are selling it for.

<!-- TODO: add a screenshot or GIF of the UI here -->

# WIP


## Background

Warframe players trade items for an in-game currency called platinum. Each relic opens into one of six Prime parts: three common, two uncommon and one rare. The drop chances are fixed and public, and the parts have market prices, so you can calculate a relic's expected value and compare it to its asking price. If the expected value is well above the price, it's worth buying.

There are around 800 relics and hundreds of Prime parts, so doing this by hand isn't realistic. I built this app to do it automatically.

## Features

- **Dashboard.** Shows the relics with the highest Radiant expected value, the best current deals, headline stats, and when the data was last refreshed.
- **Top parts.** Every tradeable Prime part ranked by 7-day price, with trade volume, 7-day price change and a trend sparkline. You can search and sort the list, and clicking a part shows which relics drop it.
- **Database refresh with live progress.** One click rebuilds all prices and relic values. The refresh page shows each step's progress, an ETA, a streaming log, error and retry counters, and a live readout of the rate limiter. You can cancel a refresh at any point without losing the existing data.
- **Background processing.** A refresh keeps running if you close the tab or move to another page. Every open page updates as it goes.
- **Deal scanner** *(in progress)*. Polls the cheapest online sell orders for valuable relics and listens to warframe.market's new-order websocket. The plan is in [docs/deal-finder-design.md](docs/deal-finder-design.md). For now the deals panel shows placeholder data.

## How the value is calculated

1. **Pricing parts.** For every tradeable Prime part, the app pulls trade statistics from [warframe.market](https://warframe.market) and computes a 7-day volume-weighted average price (VWAP) over the last 7 *completed* UTC days:

   ```
   price = Σ(volume_d × avg_price_d) / Σ(volume_d)     over the last 7 days
   ```

   I went with VWAP over the lowest listing because a handful of troll or outdated listings can throw the lowest price off a lot.

2. **Mapping relics to rewards.** Relic contents come from the [Warframe Community Developers (WFCD)](https://github.com/WFCD/warframe-drop-data) drop data. Reward names are converted to warframe.market slugs, and untradeable rewards like Forma blueprints are skipped. Requiem relics are left out.

3. **Fixing rarities.** The same part can be rare in one relic and uncommon in another, and WFCD sometimes tags a part with the rarity it has in a different relic. To get around this, the app works out each reward's rarity from its actual drop chance in that specific relic.

4. **Expected value.** Calculated for both unrefined (Intact) and fully refined (Radiant) relics:

   ```
   EV = Σ price(part) × dropChance(rarity, refinement)
   ```

   | Rarity   | Intact  | Radiant |
   |----------|---------|---------|
   | Common   | 25.33 % | 16.67 % |
   | Uncommon | 11 %    | 20 %    |
   | Rare     | 2 %     | 10 %    |

   Drop chances are from the [Warframe wiki](https://warframe.fandom.com/wiki/Void_Relic/Math#Drop_Chances).

## Technical highlights

### Refresh pipeline with an atomic publish

A refresh is a nine-step pipeline: catalogue, part prices, drop tables, reward matching, expected values, and so on. It never writes to the live tables while it runs. Instead:

1. It creates a separate **staging SQLite database**, built from the same EF Core migrations so the schema matches exactly.
2. All new data is written to staging.
3. In the final step, staging is `ATTACH`ed to the live database and every table is replaced (`DELETE` + `INSERT … SELECT`) in **one transaction**.

The app keeps reading the previous snapshot until that commit lands, so users never see half-written data. If the refresh fails or is cancelled, the staging file is deleted and the live data is untouched. Staging files left behind by a crash are cleaned up at the start of the next run.

### Background worker and coordinator

The refresh runs in a `BackgroundService`, not in the Blazor circuit that started it, which is why it survives closed tabs and navigation. A singleton `RefreshCoordinator` connects the UI and the worker:

- Start requests go through a bounded `Channel` with capacity 1, so two refreshes can't run at once.
- Cancellation uses a per-run `CancellationTokenSource`, linked to the app's shutdown token. A user cancel and an app shutdown are handled differently, and both are recorded in the run history.
- State is an immutable record, and each change swaps in a new copy under a lock.
- **Throttled UI updates.** Pricing logs hundreds of lines a second. Pushing a re-render for each one would flood the SignalR connection. Changes only set an atomic dirty flag, and a 250 ms timer turns that flag into at most one notification per interval. Important events, like start, finish and cancel, skip the timer.
- A subscriber that throws doesn't stop the other subscribers from being notified.

### Rate limiting against warframe.market

warframe.market allows 3 requests per second. Every request to it goes through one shared Polly resilience pipeline backed by a **token bucket** (1 token, refilled every 350 ms, unbounded queue):

- With no burst capacity, requests are spaced evenly instead of firing three at the start of each window.
- 350 ms instead of 333 ms leaves room for timer jitter, so the app never sends 4 requests in a second.
- Hundreds of price lookups are started in parallel and the limiter queues them, so the code stays simple while the request rate stays within the limit.
- The limiter is registered as its own singleton so the UI can read its statistics and show requests per second and queue depth live.

Data is only fetched during a refresh and then stored in SQLite, so browsing the app doesn't send any API requests.

## Tech stack

- .NET 10 / ASP.NET Core, Blazor (Interactive Server)
- Entity Framework Core + SQLite, code-first migrations
- Polly 8 with `System.Threading.RateLimiting`
- Tailwind CSS v4 (custom theme, no component library), compiled as part of `dotnet build`

## Running it

Requirements: .NET 10 SDK, Node.js with npm (the Tailwind build runs `npm install` on the first build), and the `dotnet-ef` tool.

```bash
cd RelicDealFinder
dotnet ef database update   # creates app.db
dotnet run
```

Open the app and click **Refresh database**. The first refresh takes a few minutes because every Prime part is priced one request at a time under the rate limit.

## Status

- [x] Fetch and filter the warframe.market item catalogue (Prime parts and relics only)
- [x] 7-day VWAP pricing for every Prime part, with daily price history
- [x] Import relic drop tables from WFCD and match them to market slugs
- [x] Rarity normalization and handling of known data quirks (slug typos, Forma)
- [x] Expected value for Intact and Radiant relics
- [x] Global rate limiting for warframe.market
- [x] Background refresh with staging database, atomic publish, live progress and cancellation
- [x] Dashboard and Top parts pages
- [ ] Deal scanner (polling + websocket)
- [ ] Deals and Relics pages