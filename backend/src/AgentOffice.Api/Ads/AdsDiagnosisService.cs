using AgentOffice.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentOffice.Api.Ads;

// Tudo o que o diagnóstico e o agente precisam saber, montado a partir do banco local.
public record AdsContext(
    DateOnly From,
    DateOnly To,
    AdsPlaybookSettings Settings,
    List<CampaignInput> Inputs,
    AdsAnalysis Analysis,
    ILookup<long, AdChange> Changes);

// Monta o contexto (campanhas, dias, anúncios, publicações ligadas) e roda o diagnóstico por regras.
// Usado pela rota de diagnóstico e pelo agente de IA, para que os dois enxerguem exatamente os mesmos dados.
public class AdsDiagnosisService(AgentOfficeDb db, AdsSettingsService settingsService)
{
    public async Task<AdsContext> BuildAsync(int days, CancellationToken ct)
    {
        var settings = await settingsService.GetAsync(ct);
        var (from, to) = AdsSyncService.Window(days);

        var campaigns = await db.AdCampaigns.ToListAsync(ct);
        var daily = (await db.AdCampaignDaily.Where(d => d.Date >= from && d.Date <= to).ToListAsync(ct))
            .ToLookup(d => d.CampaignId);
        var groups = (await db.AdGroupMetrics.ToListAsync(ct)).ToLookup(g => g.CampaignId);
        var keys = await db.ListingDetails
            .Select(d => new ListingKeys(d.ItemId, d.FamilyId, d.CatalogProductId))
            .ToListAsync(ct);
        var sold = await db.Listings.ToDictionaryAsync(l => l.ItemId, l => l.SoldQuantity, ct);
        var changes = (await db.AdChanges.ToListAsync(ct)).ToLookup(c => c.CampaignId);

        var inputs = campaigns.Select(c => new CampaignInput(
            c,
            daily[c.CampaignId].ToList(),
            groups[c.CampaignId].Select(g =>
            {
                // Maturidade do produto = unidades vendidas somadas das publicações ligadas ao anúncio de campanha.
                var linked = keys.Where(k => AdLinker.Matches(g, k)).ToList();
                return new GroupInput(g, linked.Sum(k => sold.GetValueOrDefault(k.ItemId)), linked.Count);
            }).ToList())).ToList();

        var analysis = AdsAnalyzer.Analyze(inputs, settings, from, to);
        return new AdsContext(from, to, settings, inputs, analysis, changes);
    }
}
