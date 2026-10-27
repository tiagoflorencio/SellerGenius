using AgentOffice.Api.Ads;
using AgentOffice.Api.Data;

namespace AgentOffice.Tests;

public class AdsAnalyzerTests
{
    private static readonly DateOnly To = new(2026, 10, 26);
    private static readonly DateOnly From = To.AddDays(-29);

    private static AdCampaign Campaign(string strategy = "PROFITABILITY", decimal budget = 100m) =>
        new() { CampaignId = 1, Name = "Teste", Status = "active", Strategy = strategy, Budget = budget, RoasTarget = 8 };

    // 30 dias iguais. TACOS = cost / (revenue + organic).
    private static List<AdCampaignDaily> Days(
        decimal cost = 12m, decimal revenue = 80m, decimal organic = 20m, int clicks = 10, int prints = 1000,
        int units = 1, double? lostBudget = 0, double? lostRank = 0) =>
        Enumerable.Range(0, 30).Select(i => new AdCampaignDaily
        {
            CampaignId = 1, Date = To.AddDays(-i), Cost = cost, Revenue = revenue, OrganicRevenue = organic,
            Clicks = clicks, Prints = prints, Units = units,
            ImpressionShare = 1 - (lostBudget ?? 0) - (lostRank ?? 0),
            LostImpressionShareByBudget = lostBudget, LostImpressionShareByRank = lostRank,
        }).ToList();

    private static GroupInput Group(long id, int clicks, int units, decimal cost = 50m, int soldUnits = 100, string status = "ACTIVE") =>
        new(new AdGroupMetric
        {
            AdGroupId = id, CampaignId = 1, Title = $"Anuncio {id}", Status = status, Prints = clicks * 100, Clicks = clicks,
            Units = units, Cost = cost, Revenue = units * 50m, AdvertisingSales = units,
        }, soldUnits, 1);

    private static CampaignDiagnosis Run(AdCampaign campaign, List<AdCampaignDaily> days, List<GroupInput>? groups = null, AdsPlaybookSettings? settings = null) =>
        AdsAnalyzer.Analyze([new CampaignInput(campaign, days, groups ?? [])], settings ?? new AdsPlaybookSettings(), From, To).Campaigns.Single();

    private static bool Has(AdDiagnosis d, string code) => d.Findings.Any(f => f.Code == code);

    [Fact]
    public void Flags_cost_above_ceiling_with_impact_in_reais()
    {
        // TACOS = 12% contra teto de 10%.
        var result = Run(Campaign(), Days());

        var finding = result.Diagnosis.Findings.Single(f => f.Code == "CUSTO_ACIMA_DA_FAIXA");
        Assert.Equal(Severity.Attention, finding.Severity);
        Assert.True(finding.ImpactBrl > 0);
        Assert.Equal(DiagnosisState.Attention, result.Diagnosis.State);
    }

    [Fact]
    public void Cost_far_above_ceiling_is_critical()
    {
        // TACOS = 20% (> 10% x 1,3).
        var result = Run(Campaign(), Days(cost: 20m));

        Assert.Equal(DiagnosisState.Critical, result.Diagnosis.State);
    }

    [Fact]
    public void Cost_inside_band_is_healthy()
    {
        var result = Run(Campaign(), Days(cost: 9m));

        Assert.False(Has(result.Diagnosis, "CUSTO_ACIMA_DA_FAIXA"));
        Assert.Equal(DiagnosisState.Healthy, result.Diagnosis.State);
    }

    [Fact]
    public void Growth_campaign_tolerates_higher_cost_than_profitability_campaign()
    {
        var days = Days(cost: 15m); // TACOS 15%

        Assert.True(Has(Run(Campaign("PROFITABILITY"), days).Diagnosis, "CUSTO_ACIMA_DA_FAIXA"));
        Assert.False(Has(Run(Campaign("INCREASE"), days).Diagnosis, "CUSTO_ACIMA_DA_FAIXA"));
    }

    [Fact]
    public void Parameters_change_the_verdict_without_code_changes()
    {
        var days = Days(); // TACOS 12%
        var relaxed = new AdsPlaybookSettings { CostTargetMaxPct = 15 };

        Assert.True(Has(Run(Campaign(), days).Diagnosis, "CUSTO_ACIMA_DA_FAIXA"));
        Assert.False(Has(Run(Campaign(), days, settings: relaxed).Diagnosis, "CUSTO_ACIMA_DA_FAIXA"));
    }

    [Fact]
    public void Acos_basis_uses_only_ad_revenue()
    {
        // ACOS = 12/80 = 15% (TACOS = 12%): com base em ACOS e teto de 10%, o custo está acima.
        var acos = new AdsPlaybookSettings { CostBasis = "acos", CostTargetMaxPct = 14 };

        Assert.True(Has(Run(Campaign(), Days(), settings: acos).Diagnosis, "CUSTO_ACIMA_DA_FAIXA"));
    }

    [Fact]
    public void Too_few_clicks_is_inconclusive_and_skips_cost_judgement()
    {
        var result = Run(Campaign(), Days(clicks: 1, prints: 200, cost: 30m));

        Assert.Equal(DiagnosisState.Inconclusive, result.Diagnosis.State);
        Assert.True(Has(result.Diagnosis, "AMOSTRA_INSUFICIENTE"));
        Assert.False(Has(result.Diagnosis, "CUSTO_ACIMA_DA_FAIXA"));
    }

    [Fact]
    public void Campaign_without_impressions_has_no_data()
    {
        var result = Run(Campaign(), Days(prints: 0, clicks: 0, cost: 0m, revenue: 0m, organic: 0m, units: 0));

        Assert.Equal(DiagnosisState.NoData, result.Diagnosis.State);
    }

    [Fact]
    public void Budget_loss_is_an_opportunity_only_when_cost_is_within_ceiling()
    {
        var profitable = Run(Campaign(), Days(cost: 8m, lostBudget: 0.3));
        var costly = Run(Campaign(), Days(cost: 12m, lostBudget: 0.3));

        Assert.True(Has(profitable.Diagnosis, "ESCALAR_ORCAMENTO"));
        Assert.False(Has(costly.Diagnosis, "ESCALAR_ORCAMENTO"));
        Assert.True(Has(costly.Diagnosis, "PERDA_ORCAMENTO_COM_CUSTO_ALTO"));
    }

    [Fact]
    public void Rank_loss_suggests_target_test_when_profitable_and_offer_work_when_costly()
    {
        var profitable = Run(Campaign(), Days(cost: 8m, lostRank: 0.5));
        var costly = Run(Campaign(), Days(cost: 12m, lostRank: 0.5));

        Assert.True(Has(profitable.Diagnosis, "META_RESTRITIVA"));
        var offer = costly.Diagnosis.Findings.Single(f => f.Code == "PERDA_RANKING_COM_CUSTO_ALTO");
        Assert.Equal(Where.Offer, offer.Where);
    }

    [Fact]
    public void Underused_budget_is_reported_when_there_is_no_budget_loss()
    {
        // Gasto de 12/dia contra orçamento de 100/dia = 12%.
        var result = Run(Campaign(budget: 100m), Days(lostBudget: 0, lostRank: 0.1));

        Assert.True(Has(result.Diagnosis, "ORCAMENTO_SUBUTILIZADO"));
    }

    [Fact]
    public void Zero_sales_is_abnormal_only_when_unlikely_given_the_account_conversion()
    {
        // Três anúncios convertem 10%; o quarto teve 150 cliques sem nenhuma venda (chance por acaso ~0,00001%).
        var groups = new List<GroupInput>
        {
            Group(1, 200, 20), Group(2, 200, 20), Group(3, 200, 20), Group(4, 150, 0),
        };

        var result = Run(Campaign(), Days(), groups);

        var zero = result.Groups.Single(g => g.AdGroupId == 4).Diagnosis;
        Assert.Equal(DiagnosisState.Critical, zero.State);
        Assert.Contains(zero.Findings, f => f.Code == "SEM_VENDAS_ANORMAL" && f.ImpactBrl == 50m);
    }

    [Fact]
    public void Few_clicks_without_sales_are_not_judged()
    {
        var groups = new List<GroupInput>
        {
            Group(1, 200, 20), Group(2, 200, 20), Group(3, 200, 20), Group(4, 30, 0),
        };

        var result = Run(Campaign(), Days(), groups);

        var few = result.Groups.Single(g => g.AdGroupId == 4).Diagnosis;
        Assert.False(Has(few, "SEM_VENDAS_ANORMAL"));
        Assert.Equal(DiagnosisState.Inconclusive, few.State);
    }

    [Fact]
    public void Conversion_is_compared_with_the_account_median_not_a_fixed_band()
    {
        var groups = new List<GroupInput>
        {
            Group(1, 200, 20), Group(2, 200, 20), Group(3, 200, 20), Group(4, 200, 4),
        };

        var result = Run(Campaign(), Days(), groups);

        Assert.Equal(10.0, AdsAnalyzer.Analyze([new CampaignInput(Campaign(), Days(), groups)], new AdsPlaybookSettings(), From, To).MedianCvr);
        Assert.True(Has(result.Groups.Single(g => g.AdGroupId == 4).Diagnosis, "CVR_ABAIXO_DA_MEDIANA"));
        Assert.False(Has(result.Groups.Single(g => g.AdGroupId == 1).Diagnosis, "CVR_ABAIXO_DA_MEDIANA"));
    }

    [Fact]
    public void Low_selling_product_in_profitability_campaign_is_flagged_as_growth_candidate()
    {
        var groups = new List<GroupInput> { Group(1, 200, 4, soldUnits: 3) };

        var result = Run(Campaign("PROFITABILITY"), Days(), groups);

        Assert.True(Has(result.Groups.Single().Diagnosis, "OBJETIVO_DESALINHADO"));
        Assert.True(Has(result.Diagnosis, "OBJETIVO_DESALINHADO"));
    }

    [Fact]
    public void Product_that_sells_in_the_period_is_not_treated_as_growth_even_if_item_total_is_low()
    {
        // Vendas totais do item baixas (3), mas 30 unidades vendidas no período: já tem tração.
        var groups = new List<GroupInput> { Group(1, 200, 30, soldUnits: 3) };

        var result = Run(Campaign("PROFITABILITY"), Days(), groups);

        Assert.False(Has(result.Groups.Single().Diagnosis, "OBJETIVO_DESALINHADO"));
    }

    [Fact]
    public void Growth_product_gets_the_growth_cost_ceiling()
    {
        // Anúncio com 3 vendas totais: teto de 20%. Custo de 15% do valor (cost 30 / receita 200).
        var group = new GroupInput(new AdGroupMetric
        {
            AdGroupId = 1, CampaignId = 1, Status = "ACTIVE", Prints = 20000, Clicks = 200, Units = 4, Cost = 30m,
            Revenue = 200m, OrganicRevenue = 0m,
        }, 3, 1);

        var result = Run(Campaign("INCREASE"), Days(), [group]);

        Assert.False(Has(result.Groups.Single().Diagnosis, "CUSTO_ACIMA_DA_FAIXA"));
    }

    [Fact]
    public void Ctr_trend_down_is_flagged_using_week_over_week_comparison()
    {
        var days = Days(clicks: 10);
        foreach (var d in days.Where(d => d.Date > To.AddDays(-7))) d.Clicks = 5; // CTR cai pela metade

        var result = Run(Campaign(), days);

        Assert.True(Has(result.Diagnosis, "CTR_CAINDO"));
    }

    [Fact]
    public void Settings_validation_rejects_inconsistent_values()
    {
        Assert.Null(new AdsPlaybookSettings().Validate());
        Assert.NotNull(new AdsPlaybookSettings { CostTargetMinPct = 12, CostTargetMaxPct = 10 }.Validate());
        Assert.NotNull(new AdsPlaybookSettings { CostBasis = "outro" }.Validate());
        Assert.NotNull(new AdsPlaybookSettings { GrowthCostMaxPct = 5 }.Validate());
    }
}
