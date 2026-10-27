using System.Text.Json;
using AgentOffice.Api.MercadoLivre;

namespace AgentOffice.Api.Ads;

public record AdvertiserInfo(long AdvertiserId, string SiteId, string Name);

// Cliente da API de Product Ads do Mercado Livre. Só LEITURA (GET), e passa pela trava de somente leitura.
// As APIs de publicidade exigem o cabeçalho "api-version" (1 para anunciantes, 2 para as demais).
public class AdsClient(MercadoLivreClient ml)
{
    // Métricas pedidas por campanha/dia. Taxas (CTR, CPC...) são recalculadas por nós a partir das somas.
    private const string DailyMetrics =
        "clicks,prints,cost,organic_units_quantity,organic_units_amount,organic_items_quantity," +
        "direct_items_quantity,indirect_items_quantity,advertising_items_quantity,direct_units_quantity," +
        "indirect_units_quantity,units_quantity,direct_amount,indirect_amount,total_amount," +
        "impression_share,lost_impression_share_by_budget,lost_impression_share_by_ad_rank,acos_benchmark";

    private const string AdGroupMetrics =
        "clicks,prints,cost,direct_amount,indirect_amount,total_amount,units_quantity," +
        "advertising_items_quantity,organic_units_quantity,organic_units_amount,sov";

    // Anunciantes de Product Ads da conta. Erro 404 "No permissions found" = produto não habilitado no perfil.
    public async Task<List<AdvertiserInfo>> GetAdvertisersAsync(CancellationToken ct)
    {
        var json = await ml.GetJsonAsync("/advertising/advertisers?product_id=PADS", "1", ct);

        return json.GetProperty("advertisers").EnumerateArray()
            .Select(a => new AdvertiserInfo(
                a.GetProperty("advertiser_id").GetInt64(),
                a.GetProperty("site_id").GetString() ?? "MLB",
                a.TryGetProperty("advertiser_name", out var name) ? name.GetString() ?? "" : ""))
            .ToList();
    }

    // Todas as campanhas do anunciante (sem métricas: elas vêm do histórico diário).
    public Task<List<JsonElement>> GetCampaignsAsync(AdvertiserInfo advertiser, CancellationToken ct) =>
        GetPagedAsync(
            offset => $"/advertising/{advertiser.SiteId}/advertisers/{advertiser.AdvertiserId}/product_ads/campaigns/search?limit=50&offset={offset}",
            ct);

    // Uma linha por dia com as métricas da campanha no período (a API aceita até 90 dias para trás).
    public async Task<List<JsonElement>> GetCampaignDailyAsync(
        string siteId, long campaignId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var json = await ml.GetJsonAsync(
            $"/advertising/{siteId}/product_ads/campaigns/{campaignId}" +
            $"?date_from={from:yyyy-MM-dd}&date_to={to:yyyy-MM-dd}&metrics={DailyMetrics}&aggregation_type=DAILY",
            "2", ct);

        // A resposta real vem como { "results": [...] }; a documentação mostra uma lista direta. Aceitamos as duas.
        var rows = json.ValueKind == JsonValueKind.Array ? json
            : json.TryGetProperty("results", out var results) ? results
            : default;

        return rows.ValueKind == JsonValueKind.Array ? rows.EnumerateArray().ToList() : [];
    }

    // Anúncios (ad groups) de uma campanha com as métricas somadas do período.
    public Task<List<JsonElement>> GetAdGroupsAsync(
        AdvertiserInfo advertiser, long campaignId, DateOnly from, DateOnly to, CancellationToken ct) =>
        GetPagedAsync(
            offset => $"/advertising/{advertiser.SiteId}/advertisers/{advertiser.AdvertiserId}/product_ads/ad_groups/search" +
                      $"?date_from={from:yyyy-MM-dd}&date_to={to:yyyy-MM-dd}&limit=200&offset={offset}" +
                      $"&sort=desc&sort_by=clicks&metrics={AdGroupMetrics}&filters[campaigns]={campaignId}",
            ct);

    // Percorre as páginas de uma busca ("results" + "paging.total") até trazer tudo.
    private async Task<List<JsonElement>> GetPagedAsync(Func<int, string> pathForOffset, CancellationToken ct)
    {
        var all = new List<JsonElement>();
        var offset = 0;

        while (true)
        {
            var page = await ml.GetJsonAsync(pathForOffset(offset), "2", ct);
            var results = page.TryGetProperty("results", out var r) && r.ValueKind == JsonValueKind.Array
                ? r.EnumerateArray().ToList()
                : [];

            all.AddRange(results);
            offset += results.Count;

            var total = page.TryGetProperty("paging", out var paging) && paging.TryGetProperty("total", out var t) ? t.GetInt32() : 0;
            if (results.Count == 0 || offset >= total) break;
        }

        return all;
    }
}
