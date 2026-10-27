using System.Text.Json;
using AgentOffice.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentOffice.Api.Ads;

public record AdsSyncResult(int Advertisers, int Campaigns, int DailyRows, int AdGroups, DateOnly From, DateOnly To);

// Sincronização de Product Ads: LÊ campanhas e métricas no Mercado Livre e grava uma cópia no banco local.
// Nada é escrito no Mercado Livre.
public class AdsSyncService(AdsClient ads, AgentOfficeDb db, ILogger<AdsSyncService> logger)
{
    public const int MaxDays = 90; // limite da API para métricas

    // Dados do dia corrente só ficam completos no dia seguinte, então o período termina em "ontem".
    public static (DateOnly From, DateOnly To) Window(int days)
    {
        var to = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);
        return (to.AddDays(-(Math.Clamp(days, 1, MaxDays) - 1)), to);
    }

    public async Task<AdsSyncResult> SyncAsync(int days, CancellationToken ct)
    {
        var (from, to) = Window(days);
        var now = DateTimeOffset.UtcNow;
        var advertisers = await ads.GetAdvertisersAsync(ct);

        var campaignCount = 0;
        var dailyCount = 0;
        var groupCount = 0;

        var campaigns = await db.AdCampaigns.ToDictionaryAsync(c => c.CampaignId, ct);
        var groups = await db.AdGroupMetrics.ToDictionaryAsync(g => g.AdGroupId, ct);
        var today = DateOnly.FromDateTime(DateTime.Now);
        var todaySnapshots = await db.AdCampaignSnapshots.Where(s => s.Date == today).ToDictionaryAsync(s => s.CampaignId, ct);

        foreach (var advertiser in advertisers)
        {
            foreach (var raw in await ads.GetCampaignsAsync(advertiser, ct))
            {
                var id = raw.GetProperty("id").GetInt64();

                var isNew = !campaigns.TryGetValue(id, out var campaign);
                if (isNew)
                {
                    campaign = new AdCampaign { CampaignId = id };
                    campaigns[id] = campaign;
                    db.AdCampaigns.Add(campaign);
                }

                // Estado anterior, para detectar o que mudou desde a última sincronização.
                var before = isNew ? null : CampaignState.From(campaign!);

                campaign!.AdvertiserId = advertiser.AdvertiserId;
                campaign.SiteId = advertiser.SiteId;
                campaign.Name = Str(raw, "name") ?? "";
                campaign.Status = Str(raw, "status") ?? "";
                campaign.Strategy = Str(raw, "strategy");
                campaign.Budget = Dec(raw, "budget");
                campaign.DailyBudget = Dec(raw, "daily_budget");
                campaign.AutomaticBudget = raw.TryGetProperty("automatic_budget", out var auto) && auto.ValueKind == JsonValueKind.True;
                campaign.RoasTarget = Num(raw, "roas_target");
                campaign.AcosTarget = Num(raw, "acos_target");
                campaign.CampaignCreatedAt = Date(raw, "date_created");
                campaign.CampaignUpdatedAt = Date(raw, "last_updated");
                campaign.SyncedAt = now;
                campaignCount++;

                if (before is not null) RecordChanges(id, before, CampaignState.From(campaign), now);
                UpsertSnapshot(campaign, today, todaySnapshots);

                dailyCount += await SyncDailyAsync(advertiser.SiteId, id, from, to, ct);
                groupCount += await SyncAdGroupsAsync(advertiser, id, from, to, now, groups, ct);
            }
        }

        await db.SaveChangesAsync(ct);
        return new AdsSyncResult(advertisers.Count, campaignCount, dailyCount, groupCount, from, to);
    }

    // Campos da configuração da campanha que acompanhamos (o que você pode alterar no Mercado Livre).
    private record CampaignState(string Status, string? Strategy, decimal Budget, double? RoasTarget, bool AutomaticBudget)
    {
        public static CampaignState From(AdCampaign c) => new(c.Status, c.Strategy, c.Budget, c.RoasTarget, c.AutomaticBudget);
    }

    // Compara o estado anterior com o atual e grava uma linha para cada campo que mudou.
    private void RecordChanges(long campaignId, CampaignState before, CampaignState after, DateTimeOffset now)
    {
        void Add(string field, string? oldValue, string? newValue)
        {
            if (oldValue == newValue) return;
            db.AdChanges.Add(new AdChange
            {
                CampaignId = campaignId,
                DetectedAt = now,
                Field = field,
                OldValue = oldValue ?? "",
                NewValue = newValue ?? "",
            });
        }

        var inv = System.Globalization.CultureInfo.InvariantCulture;
        Add("Status", before.Status, after.Status);
        Add("Strategy", before.Strategy, after.Strategy);
        Add("Budget", before.Budget.ToString("0.##", inv), after.Budget.ToString("0.##", inv));
        Add("RoasTarget", before.RoasTarget?.ToString("0.##", inv), after.RoasTarget?.ToString("0.##", inv));
        Add("AutomaticBudget", before.AutomaticBudget.ToString(), after.AutomaticBudget.ToString());
    }

    // Um retrato por campanha e dia (se sincronizar mais de uma vez no dia, o retrato é atualizado).
    private void UpsertSnapshot(AdCampaign c, DateOnly today, Dictionary<long, AdCampaignSnapshot> todaySnapshots)
    {
        if (!todaySnapshots.TryGetValue(c.CampaignId, out var snapshot))
        {
            snapshot = new AdCampaignSnapshot { CampaignId = c.CampaignId, Date = today };
            todaySnapshots[c.CampaignId] = snapshot;
            db.AdCampaignSnapshots.Add(snapshot);
        }

        snapshot.Status = c.Status;
        snapshot.Strategy = c.Strategy;
        snapshot.Budget = c.Budget;
        snapshot.RoasTarget = c.RoasTarget;
        snapshot.AutomaticBudget = c.AutomaticBudget;
    }

    // Atualiza (ou cria) uma linha por dia. Falha em uma campanha não derruba as demais.
    private async Task<int> SyncDailyAsync(string siteId, long campaignId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        List<JsonElement> rows;
        try
        {
            rows = await ads.GetCampaignDailyAsync(siteId, campaignId, from, to, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Métricas diárias da campanha {CampaignId} indisponíveis: {Message}", campaignId, ex.Message);
            return 0;
        }

        var existing = await db.AdCampaignDaily
            .Where(d => d.CampaignId == campaignId && d.Date >= from && d.Date <= to)
            .ToDictionaryAsync(d => d.Date, ct);

        var count = 0;
        foreach (var row in rows)
        {
            if (!DateOnly.TryParse(Str(row, "date"), out var date)) continue;

            if (!existing.TryGetValue(date, out var day))
            {
                day = new AdCampaignDaily { CampaignId = campaignId, Date = date };
                db.AdCampaignDaily.Add(day);
            }

            day.Clicks = (int)Num(row, "clicks");
            day.Prints = (int)Num(row, "prints");
            day.Cost = Dec(row, "cost");
            day.Revenue = Dec(row, "total_amount");
            day.DirectRevenue = Dec(row, "direct_amount");
            day.IndirectRevenue = Dec(row, "indirect_amount");
            day.Units = (int)Num(row, "units_quantity");
            day.AdvertisingSales = (int)Num(row, "advertising_items_quantity");
            day.OrganicUnits = (int)Num(row, "organic_units_quantity");
            day.OrganicRevenue = Dec(row, "organic_units_amount");
            day.ImpressionShare = NullableNum(row, "impression_share");
            day.LostImpressionShareByBudget = NullableNum(row, "lost_impression_share_by_budget");
            day.LostImpressionShareByRank = NullableNum(row, "lost_impression_share_by_ad_rank");
            day.AcosBenchmark = NullableNum(row, "acos_benchmark");
            count++;
        }

        return count;
    }

    private async Task<int> SyncAdGroupsAsync(
        AdvertiserInfo advertiser, long campaignId, DateOnly from, DateOnly to, DateTimeOffset now,
        Dictionary<long, AdGroupMetric> groups, CancellationToken ct)
    {
        List<JsonElement> rows;
        try
        {
            rows = await ads.GetAdGroupsAsync(advertiser, campaignId, from, to, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Anúncios da campanha {CampaignId} indisponíveis: {Message}", campaignId, ex.Message);
            return 0;
        }

        foreach (var row in rows)
        {
            var id = row.GetProperty("id").GetInt64();

            if (!groups.TryGetValue(id, out var group))
            {
                group = new AdGroupMetric { AdGroupId = id };
                groups[id] = group;
                db.AdGroupMetrics.Add(group);
            }

            var metrics = row.TryGetProperty("metrics", out var m) ? m : default;

            group.CampaignId = campaignId;
            group.AdGroupType = Str(row, "ad_group_type") ?? "";
            group.ExternalId = Str(row, "ad_group_external_id") ?? "";
            group.Title = Str(row, "title") ?? "";
            group.Status = Str(row, "status") ?? "";
            group.Thumbnail = Str(row, "thumbnail");
            group.DomainId = Str(row, "domain_id");
            group.CatalogListing = row.TryGetProperty("catalog_listing", out var cl) && cl.ValueKind == JsonValueKind.True;
            group.Tags = row.TryGetProperty("tags", out var tags) && tags.ValueKind == JsonValueKind.Array
                ? string.Join(",", tags.EnumerateArray().Select(t => t.GetString()))
                : "";
            group.PeriodFrom = from;
            group.PeriodTo = to;
            group.Clicks = (int)Num(metrics, "clicks");
            group.Prints = (int)Num(metrics, "prints");
            group.Cost = Dec(metrics, "cost");
            group.Revenue = Dec(metrics, "total_amount");
            group.Units = (int)Num(metrics, "units_quantity");
            group.AdvertisingSales = (int)Num(metrics, "advertising_items_quantity");
            group.OrganicUnits = (int)Num(metrics, "organic_units_quantity");
            group.OrganicRevenue = Dec(metrics, "organic_units_amount");
            group.Sov = NullableNum(metrics, "sov");
            group.SyncedAt = now;
        }

        return rows.Count;
    }

    // Helpers de leitura tolerantes: campo ausente ou null vira valor padrão em vez de exceção.
    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private static double? NullableNum(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDouble()
            : null;

    private static double Num(JsonElement e, string name) => NullableNum(e, name) ?? 0;

    private static decimal Dec(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            ? v.GetDecimal()
            : 0m;

    private static DateTimeOffset? Date(JsonElement e, string name) =>
        DateTimeOffset.TryParse(Str(e, name), out var d) ? d : null;
}
