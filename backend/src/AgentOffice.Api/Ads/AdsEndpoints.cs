using AgentOffice.Api.Data;
using AgentOffice.Api.MercadoLivre;
using Microsoft.EntityFrameworkCore;

namespace AgentOffice.Api.Ads;

public record AdCampaignView(
    long Id,
    string Name,
    string Status,
    string? Strategy,
    decimal Budget,
    decimal DailyBudget,
    double? RoasTarget,
    double? AcosTarget,
    decimal AvgDailyCost,      // custo total do período dividido pelos dias do período
    double? BudgetUsagePct,    // custo médio diário / orçamento (%)
    double? ImpressionShare,   // média dos dias com impressões (0 a 1)
    double? LostByBudget,
    double? LostByRank,
    AdMetrics Metrics);

public record AdDayView(
    DateOnly Date, AdMetrics Metrics, double? ImpressionShare, double? LostByBudget, double? LostByRank);

public record LinkedListing(string ItemId, string Title);

public record AdGroupView(
    long Id, string Type, string ExternalId, string Title, string Status, string? Thumbnail,
    string[] Tags, double? Sov, AdMetrics Metrics, List<LinkedListing> Listings);

// Um anúncio de campanha ligado a uma publicação. "SharedBy" = quantas publicações dividem este mesmo grupo
// (família ou catálogo); as métricas são do grupo inteiro.
public record LinkedAdGroup(
    long AdGroupId, string Type, string Status, long CampaignId, string CampaignName, string CampaignStatus,
    int SharedBy, DateOnly From, DateOnly To, AdMetrics Metrics);

public record ListingAdView(string ItemId, AdMetrics Metrics, bool Shared, List<LinkedAdGroup> AdGroups);

// Achado de maior prioridade da conta, já com o nome de onde ele ocorre.
public record TopFinding(long CampaignId, string CampaignName, long? AdGroupId, string? AdGroupTitle, AdFinding Finding);

// Rotas de Product Ads. Todas só locais; só a sincronização conversa com o Mercado Livre (somente leitura).
public static class AdsEndpoints
{
    public static void MapAdsEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/ads").AddEndpointFilter(MercadoLivreEndpoints.LocalOnly);

        // POST /ads/sync?days=30: lê campanhas, métricas diárias e anúncios no Mercado Livre e salva no banco local.
        group.MapPost("/sync", async (AdsSyncService sync, CancellationToken ct, int days = 30) =>
        {
            try
            {
                return Results.Ok(await sync.SyncAsync(days, ct));
            }
            catch (InvalidOperationException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status401Unauthorized);
            }
            catch (HttpRequestException ex)
            {
                return Results.Json(new { error = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        // GET /ads/campaigns?days=30: campanhas com as métricas somadas do período (lê só o banco local).
        group.MapGet("/campaigns", async (AgentOfficeDb db, CancellationToken ct, int days = 30) =>
        {
            var (from, to) = AdsSyncService.Window(days);
            var campaigns = await db.AdCampaigns.ToListAsync(ct);
            var daily = (await db.AdCampaignDaily.Where(d => d.Date >= from && d.Date <= to).ToListAsync(ct))
                .ToLookup(d => d.CampaignId);

            var views = campaigns
                .Select(c => BuildView(c, daily[c.CampaignId].ToList(), from, to))
                .OrderByDescending(v => v.Metrics.Cost)
                .ToList();

            return Results.Ok(new
            {
                from,
                to,
                lastSync = campaigns.Count == 0 ? (DateTimeOffset?)null : campaigns.Max(c => c.SyncedAt),
                totals = AdMetricsCalculator.Sum(views.Select(v => v.Metrics)),
                campaigns = views,
            });
        });

        // GET /ads/by-listing: para cada publicação do painel, os anúncios de campanha ligados a ela e as métricas
        // somadas (período da última sincronização de Ads). Lê só o banco local.
        group.MapGet("/by-listing", async (AgentOfficeDb db, CancellationToken ct) =>
        {
            var keys = await LoadKeysAsync(db, ct);
            var groups = await db.AdGroupMetrics.ToListAsync(ct);
            var campaigns = await db.AdCampaigns.ToDictionaryAsync(c => c.CampaignId, ct);

            // Quantas publicações dividem cada grupo (ex.: as duas variantes de uma família).
            var sharedBy = groups.ToDictionary(g => g.AdGroupId, g => keys.Count(k => AdLinker.Matches(g, k)));

            var result = keys
                .Select(k =>
                {
                    var linked = groups
                        .Where(g => AdLinker.Matches(g, k))
                        .Select(g =>
                        {
                            var campaign = campaigns.GetValueOrDefault(g.CampaignId);
                            return new LinkedAdGroup(
                                g.AdGroupId, g.AdGroupType, g.Status, g.CampaignId,
                                campaign?.Name ?? "", campaign?.Status ?? "", sharedBy[g.AdGroupId],
                                g.PeriodFrom, g.PeriodTo, AdMetricsCalculator.From(g));
                        })
                        .OrderByDescending(g => g.Metrics.Cost)
                        .ToList();

                    return new ListingAdView(
                        k.ItemId,
                        AdMetricsCalculator.Sum(linked.Select(g => g.Metrics)),
                        linked.Any(g => g.SharedBy > 1),
                        linked);
                })
                .Where(v => v.AdGroups.Count > 0)
                .ToList();

            return Results.Ok(result);
        });

        // GET /ads/settings e PUT /ads/settings: parâmetros do diagnóstico (faixa de custo, volumes mínimos, limites).
        group.MapGet("/settings", async (AdsSettingsService settings, CancellationToken ct) =>
            Results.Ok(await settings.GetAsync(ct)));

        group.MapPut("/settings", async (AdsPlaybookSettings body, AdsSettingsService settings, CancellationToken ct) =>
        {
            var error = body.Validate();
            if (error is not null) return Results.BadRequest(new { error });

            await settings.SaveAsync(body, ct);
            return Results.Ok(body);
        });

        // GET /ads/diagnosis?days=30: diagnóstico por regras de cada campanha e anúncio de campanha, com evidências.
        // Lê só o banco local e não chama a IA (gratuito).
        group.MapGet("/diagnosis", async (AdsDiagnosisService diagnosisService, CancellationToken ct, int days = 30) =>
        {
            var context = await diagnosisService.BuildAsync(days, ct);
            var analysis = context.Analysis;
            var (from, to, settings) = (context.From, context.To, context.Settings);

            static int Rank(string severity) => severity switch
            {
                Severity.Critical => 0,
                Severity.Attention => 1,
                Severity.Opportunity => 2,
                _ => 3,
            };

            var top = analysis.Campaigns
                .SelectMany(c => c.Diagnosis.Findings
                    .Select(f => new TopFinding(c.CampaignId, c.Name, null, null, f))
                    .Concat(c.Groups.SelectMany(g => g.Diagnosis.Findings
                        .Select(f => new TopFinding(c.CampaignId, c.Name, g.AdGroupId, g.Title, f)))))
                .Where(t => t.Finding.Severity != Severity.Info)
                .OrderBy(t => Rank(t.Finding.Severity))
                .ThenByDescending(t => t.Finding.ImpactBrl ?? 0)
                .Take(12)
                .ToList();

            return Results.Ok(new
            {
                from,
                to,
                settings,
                analysis.MedianCvr,
                analysis.CvrSampleGroups,
                campaigns = analysis.Campaigns,
                top,
            });
        });

        // GET /ads/campaigns/{id}?days=30: uma campanha com a série diária e os anúncios dela.
        group.MapGet("/campaigns/{id:long}", async (long id, AgentOfficeDb db, CancellationToken ct, int days = 30) =>
        {
            var campaign = await db.AdCampaigns.FindAsync([id], ct);
            if (campaign is null) return Results.NotFound(new { error = "Campanha não encontrada. Rode /ads/sync primeiro." });

            var (from, to) = AdsSyncService.Window(days);
            var rows = (await db.AdCampaignDaily.Where(d => d.CampaignId == id && d.Date >= from && d.Date <= to).ToListAsync(ct))
                .OrderBy(d => d.Date)
                .ToList();

            var groupEntities = await db.AdGroupMetrics.Where(g => g.CampaignId == id).ToListAsync(ct);
            var keys = await LoadKeysAsync(db, ct);
            var titles = await db.Listings.ToDictionaryAsync(l => l.ItemId, l => l.Title, ct);
            var adGroups = groupEntities
                .Select(g => new AdGroupView(
                    g.AdGroupId, g.AdGroupType, g.ExternalId, g.Title, g.Status, g.Thumbnail,
                    g.Tags.Split(',', StringSplitOptions.RemoveEmptyEntries), g.Sov, AdMetricsCalculator.From(g),
                    keys.Where(k => AdLinker.Matches(g, k)).Select(k => new LinkedListing(k.ItemId, titles.GetValueOrDefault(k.ItemId, k.ItemId))).ToList()))
                .OrderByDescending(g => g.Metrics.Cost)
                .ToList();

            return Results.Ok(new
            {
                from,
                to,
                campaign = BuildView(campaign, rows, from, to),
                daily = rows.Select(d => new AdDayView(
                    d.Date, AdMetricsCalculator.From(d), d.ImpressionShare, d.LostImpressionShareByBudget, d.LostImpressionShareByRank)),
                adGroups,
                changes = (await db.AdChanges.Where(c => c.CampaignId == id).ToListAsync(ct))
                    .OrderByDescending(c => c.DetectedAt)
                    .ToList(),
                // Os anúncios têm o período da última sincronização, que pode diferir do período pedido aqui.
                adGroupsPeriod = groupEntities.Count == 0 ? null : new { from = groupEntities[0].PeriodFrom, to = groupEntities[0].PeriodTo },
            });
        });
    }

    private static async Task<List<ListingKeys>> LoadKeysAsync(AgentOfficeDb db, CancellationToken ct) =>
        await db.ListingDetails
            .Select(d => new ListingKeys(d.ItemId, d.FamilyId, d.CatalogProductId))
            .ToListAsync(ct);

    internal static AdCampaignView BuildView(AdCampaign c, List<AdCampaignDaily> rows, DateOnly from, DateOnly to)
    {
        var metrics = AdMetricsCalculator.From(rows);
        var windowDays = to.DayNumber - from.DayNumber + 1;
        var avgDailyCost = Math.Round(metrics.Cost / windowDays, 2);

        // Médias só dos dias em que o anúncio apareceu (sem impressões não há o que medir).
        var withPrints = rows.Where(d => d.Prints > 0).ToList();
        double? Avg(Func<AdCampaignDaily, double?> selector)
        {
            var values = withPrints.Select(selector).OfType<double>().ToList();
            return values.Count == 0 ? null : Math.Round(values.Average(), 3);
        }

        return new AdCampaignView(
            c.CampaignId, c.Name, c.Status, c.Strategy, c.Budget, c.DailyBudget, c.RoasTarget, c.AcosTarget,
            avgDailyCost,
            c.Budget > 0 ? Math.Round((double)(avgDailyCost / c.Budget) * 100, 1) : null,
            Avg(d => d.ImpressionShare), Avg(d => d.LostImpressionShareByBudget), Avg(d => d.LostImpressionShareByRank),
            metrics);
    }
}
