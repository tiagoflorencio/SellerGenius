using System.Text.Json;
using AgentOffice.Api.Ads;
using AgentOffice.Api.Data;

namespace AgentOffice.Tests;

public class AdsRecommendationProcessorTests
{
    private static readonly DateOnly To = new(2026, 10, 26);
    private static readonly DateOnly From = To.AddDays(-29);
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 12, 0, 0, TimeSpan.Zero);

    // Campanha 1 com TACOS de 12% (acima do teto de 10%) e dois anúncios; campanha 2 saudável.
    private static AdsContext Context(AdsPlaybookSettings? settings = null, params AdChange[] changes)
    {
        settings ??= new AdsPlaybookSettings();

        List<AdCampaignDaily> Days(long id, decimal cost) => Enumerable.Range(0, 30).Select(i => new AdCampaignDaily
        {
            CampaignId = id, Date = To.AddDays(-i), Cost = cost, Revenue = 80m, OrganicRevenue = 20m,
            Clicks = 10, Prints = 1000, Units = 1, LostImpressionShareByBudget = 0, LostImpressionShareByRank = 0,
        }).ToList();

        GroupInput Group(long id, long campaignId, int clicks, int units) => new(new AdGroupMetric
        {
            AdGroupId = id, CampaignId = campaignId, Title = $"Anuncio {id}", Status = "ACTIVE", Clicks = clicks,
            Prints = clicks * 100, Units = units, Cost = 50m, Revenue = units * 50m,
        }, 100, 1);

        var inputs = new List<CampaignInput>
        {
            new(new AdCampaign { CampaignId = 1, Name = "Campanha A", Status = "active", Strategy = "PROFITABILITY", Budget = 100m, RoasTarget = 8 },
                Days(1, 12m), [Group(11, 1, 200, 20), Group(12, 1, 200, 20), Group(13, 1, 200, 20)]),
            new(new AdCampaign { CampaignId = 2, Name = "Campanha B", Status = "active", Strategy = "PROFITABILITY", Budget = 100m, RoasTarget = 8 },
                Days(2, 9m), [Group(21, 2, 200, 20)]),
        };

        var analysis = AdsAnalyzer.Analyze(inputs, settings, From, To);
        return new AdsContext(From, To, settings, inputs, analysis, changes.ToLookup(c => c.CampaignId));
    }

    private static LlmRecommendation Rec(
        string action = "adjust_roas_target", string scope = "campaign", long? campaign = 1, long? adGroup = null,
        string title = "Revisar meta de ROAS", string confidence = "alta", string[]? codes = null) =>
        new(scope, campaign, adGroup, action, "Campanha Ads", title, "Diagnóstico de teste.", ["evidência"], ["hipótese"],
            new LlmTest("mudar a meta", "-10%", 14, "ROAS", "ROAS >= meta"), "efeito", ["risco"], confidence,
            (codes ?? ["CUSTO_ACIMA_DA_FAIXA"]).ToList());

    private static ProcessResult Run(AdsContext ctx, params LlmRecommendation[] recs) =>
        AdsRecommendationProcessor.Process(new LlmOutput("resumo", null, recs.ToList(), null), ctx, Now);

    [Fact]
    public void Accepts_valid_recommendation_and_computes_priority_in_code()
    {
        var result = Run(Context(), Rec());

        var rec = Assert.Single(result.Recommendations);
        Assert.InRange(rec.Priority, 0, 100);
        Assert.False(rec.ImpactUnknown);          // o achado CUSTO_ACIMA_DA_FAIXA tem impacto em reais
        Assert.True(rec.ImpactBrl > 0);
        Assert.Contains("CUSTO_ACIMA_DA_FAIXA", rec.FindingCodes);
        Assert.Equal("alta", rec.Confidence);
    }

    [Fact]
    public void Discards_unknown_action_wrong_scope_and_nonexistent_targets()
    {
        var result = Run(Context(),
            Rec(action: "delete_everything"),
            Rec(action: "pause_ad_group", scope: "campaign"),          // pausar exige escopo ad_group
            Rec(campaign: 999),                                         // campanha inexistente
            Rec(action: "pause_ad_group", scope: "ad_group", campaign: null, adGroup: 999)); // anúncio inexistente

        Assert.Empty(result.Recommendations);
        Assert.Equal(4, result.Discarded.Count);
    }

    [Fact]
    public void Keeps_only_finding_codes_that_exist_in_the_scope()
    {
        var result = Run(Context(), Rec(codes: ["CUSTO_ACIMA_DA_FAIXA", "CODIGO_INVENTADO"]));

        Assert.Equal(["CUSTO_ACIMA_DA_FAIXA"], result.Recommendations.Single().FindingCodes);
    }

    [Fact]
    public void Unknown_impact_lowers_confidence_and_flags_it()
    {
        // Campanha B está saudável: sem achados que tragam impacto em reais.
        var withImpact = Run(Context(), Rec(campaign: 1)).Recommendations.Single();
        var noImpact = Run(Context(), Rec(campaign: 2, codes: [])).Recommendations.Single();

        Assert.True(noImpact.ImpactUnknown);
        Assert.Null(noImpact.ImpactBrl);
        Assert.True(noImpact.Content.Pontuacao.Confidence < withImpact.Content.Pontuacao.Confidence);
    }

    [Fact]
    public void Blocks_settings_change_on_recently_changed_campaign_but_allows_other_actions()
    {
        var change = new AdChange { CampaignId = 1, Field = "Budget", OldValue = "50", NewValue = "100", DetectedAt = Now.AddDays(-3) };
        var ctx = Context(null, change);

        var result = Run(ctx, Rec(action: "increase_budget"), Rec(action: "improve_listing", title: "Melhorar foto"));

        Assert.Equal("improve_listing", result.Recommendations.Single().Action);
        Assert.Contains(result.Discarded, d => d.Reason.Contains("aguarde"));
    }

    [Fact]
    public void Allows_settings_change_after_the_waiting_period()
    {
        var change = new AdChange { CampaignId = 1, Field = "Budget", OldValue = "50", NewValue = "100", DetectedAt = Now.AddDays(-20) };

        var result = Run(Context(null, change), Rec(action: "increase_budget"));

        Assert.Single(result.Recommendations);
    }

    [Fact]
    public void Keeps_only_the_highest_priority_settings_change_per_campaign()
    {
        var result = Run(Context(),
            Rec(action: "adjust_roas_target", title: "Alta confiança", confidence: "alta"),
            Rec(action: "decrease_budget", title: "Baixa confiança", confidence: "baixa"));

        var kept = Assert.Single(result.Recommendations);
        Assert.Equal("Alta confiança", kept.Title);
        Assert.Contains(result.Discarded, d => d.Title == "Baixa confiança" && d.Reason.Contains("uma por vez"));
    }

    [Fact]
    public void Allows_one_settings_change_in_each_of_two_campaigns()
    {
        var result = Run(Context(),
            Rec(action: "adjust_roas_target", campaign: 1),
            Rec(action: "increase_budget", campaign: 2, title: "Escalar B"));

        Assert.Equal(2, result.Recommendations.Count);
    }

    [Fact]
    public void Respects_the_maximum_number_of_recommendations_and_orders_by_priority()
    {
        var settings = new AdsPlaybookSettings { MaxRecommendations = 2 };
        var result = Run(Context(settings),
            Rec(action: "improve_listing", title: "A", confidence: "baixa"),
            Rec(action: "review_offer", title: "B", confidence: "alta"),
            Rec(action: "review_costs", title: "C", confidence: "media"));

        Assert.Equal(2, result.Recommendations.Count);
        Assert.True(result.Recommendations[0].Priority >= result.Recommendations[1].Priority);
        Assert.Contains(result.Discarded, d => d.Reason.Contains("limite"));
    }

    [Fact]
    public void Priority_follows_the_configured_weights()
    {
        // Só a urgência importa: CUSTO_ACIMA_DA_FAIXA é "attention" (70) ou "critical" (100) conforme o desvio.
        var settings = new AdsPlaybookSettings { WeightImpact = 0, WeightConfidence = 0, WeightUrgency = 1, WeightEase = 0, WeightReversibility = 0 };

        var rec = Run(Context(settings), Rec()).Recommendations.Single();

        Assert.Equal(rec.Content.Pontuacao.Urgency, rec.Priority);
    }

    [Fact]
    public void Normalizes_confidence_labels_with_accents_and_case()
    {
        var rec = Run(Context(), Rec(confidence: "Média")).Recommendations.Single();

        Assert.Equal("media", rec.Confidence);
    }

    [Fact]
    public void Advisor_input_is_serializable_and_limits_ad_groups_per_campaign()
    {
        var ctx = Context();

        var json = JsonSerializer.Serialize(AdsAdvisor.BuildInput(ctx, Now), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);

        var campaigns = doc.RootElement.GetProperty("campanhas");
        Assert.Equal(2, campaigns.GetArrayLength());
        Assert.Equal(3, campaigns[0].GetProperty("anuncios").GetArrayLength());
        Assert.True(doc.RootElement.TryGetProperty("parametros", out _));
        Assert.True(doc.RootElement.TryGetProperty("licoes_aprendidas", out _));
        Assert.True(campaigns[0].GetProperty("achados").GetArrayLength() >= 1);
    }

    [Fact]
    public void Settings_validation_covers_the_new_agent_parameters()
    {
        Assert.NotNull(new AdsPlaybookSettings { MaxRecommendations = 0 }.Validate());
        Assert.NotNull(new AdsPlaybookSettings { WeightImpact = -1 }.Validate());
        Assert.NotNull(new AdsPlaybookSettings { WeightImpact = 0, WeightConfidence = 0, WeightUrgency = 0, WeightEase = 0, WeightReversibility = 0 }.Validate());
        Assert.Null(new AdsPlaybookSettings().Validate());
    }
}
