using AgentOffice.Api.Data;

namespace AgentOffice.Api.Ads;

// Métricas de um conjunto de dias (uma campanha, um anúncio ou todas as campanhas).
// Os valores somáveis vêm do banco; as taxas são calculadas aqui a partir das somas, o que as torna
// corretas para qualquer período. Quando o denominador é zero, a taxa fica null (indefinida).
public record AdMetrics(
    int Clicks,
    int Prints,
    decimal Cost,
    decimal Revenue,
    decimal DirectRevenue,
    decimal IndirectRevenue,
    int Units,
    int AdvertisingSales,
    int OrganicUnits,
    decimal OrganicRevenue,
    double? Ctr,    // % de cliques sobre impressões
    decimal? Cpc,   // custo por clique
    double? Acos,   // % do faturamento por publicidade gasto em publicidade
    double? Roas,   // receita por real investido
    double? Cvr,    // % de cliques que viraram venda
    double? Tacos); // % do faturamento TOTAL (publicidade + orgânico) gasto em publicidade

public static class AdMetricsCalculator
{
    public static AdMetrics From(IEnumerable<AdCampaignDaily> days) =>
        Build(
            clicks: days.Sum(d => d.Clicks),
            prints: days.Sum(d => d.Prints),
            cost: days.Sum(d => d.Cost),
            revenue: days.Sum(d => d.Revenue),
            directRevenue: days.Sum(d => d.DirectRevenue),
            indirectRevenue: days.Sum(d => d.IndirectRevenue),
            units: days.Sum(d => d.Units),
            advertisingSales: days.Sum(d => d.AdvertisingSales),
            organicUnits: days.Sum(d => d.OrganicUnits),
            organicRevenue: days.Sum(d => d.OrganicRevenue));

    public static AdMetrics From(AdGroupMetric group) =>
        Build(group.Clicks, group.Prints, group.Cost, group.Revenue, 0, 0,
            group.Units, group.AdvertisingSales, group.OrganicUnits, group.OrganicRevenue);

    // Soma várias métricas já calculadas (ex.: o total de todas as campanhas).
    public static AdMetrics Sum(IEnumerable<AdMetrics> items) =>
        Build(
            items.Sum(m => m.Clicks), items.Sum(m => m.Prints), items.Sum(m => m.Cost), items.Sum(m => m.Revenue),
            items.Sum(m => m.DirectRevenue), items.Sum(m => m.IndirectRevenue), items.Sum(m => m.Units),
            items.Sum(m => m.AdvertisingSales), items.Sum(m => m.OrganicUnits), items.Sum(m => m.OrganicRevenue));

    // Métricas de um único dia (para o gráfico).
    public static AdMetrics From(AdCampaignDaily day) => From(new[] { day });

    private static AdMetrics Build(
        int clicks, int prints, decimal cost, decimal revenue, decimal directRevenue, decimal indirectRevenue,
        int units, int advertisingSales, int organicUnits, decimal organicRevenue)
    {
        var totalRevenue = revenue + organicRevenue;

        return new AdMetrics(
            clicks, prints, Round(cost), Round(revenue), Round(directRevenue), Round(indirectRevenue),
            units, advertisingSales, organicUnits, Round(organicRevenue),
            Ctr: prints > 0 ? Math.Round(clicks * 100.0 / prints, 2) : null,
            Cpc: clicks > 0 ? Round(cost / clicks) : null,
            Acos: revenue > 0 ? Math.Round((double)(cost / revenue) * 100, 2) : null,
            Roas: cost > 0 ? Math.Round((double)(revenue / cost), 2) : null,
            Cvr: clicks > 0 ? Math.Round(units * 100.0 / clicks, 2) : null,
            Tacos: totalRevenue > 0 ? Math.Round((double)(cost / totalRevenue) * 100, 2) : null);
    }

    private static decimal Round(decimal value) => Math.Round(value, 2);
}
