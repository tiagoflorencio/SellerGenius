using AgentOffice.Api.Ads;
using AgentOffice.Api.Data;

namespace AgentOffice.Tests;

public class AdsEvaluationTests
{
    private static readonly AdsPlaybookSettings Settings = new();

    // Evento no dia 20/09; sincronização anterior no dia 18/09; janela de 14 dias.
    private static readonly DateOnly Event = new(2026, 9, 20);
    private static readonly DateOnly Gap = new(2026, 9, 18);

    private static EvaluationInput Input(int confounders = 0, bool indirect = false, DateOnly? gap = null) =>
        new(Event, gap ?? Gap, 14, indirect, confounders);

    private static List<AdCampaignDaily> Days(DateOnly from, int count, int clicks, decimal cost, decimal revenue) =>
        Enumerable.Range(0, count).Select(i => new AdCampaignDaily
        {
            CampaignId = 1,
            Date = from.AddDays(i),
            Clicks = clicks,
            Prints = clicks * 100,
            Cost = cost,
            Revenue = revenue,
            Units = clicks / 10,
        }).ToList();

    // 14 dias antes (04/09 a 17/09) e 14 dias depois (21/09 a 04/10).
    private static List<AdCampaignDaily> Series(int clicksBefore, decimal costBefore, decimal revBefore, int clicksAfter, decimal costAfter, decimal revAfter)
    {
        var rows = Days(Event.AddDays(-16), 14, clicksBefore, costBefore, revBefore);
        rows.AddRange(Days(Event.AddDays(1), 14, clicksAfter, costAfter, revAfter));
        return rows;
    }

    private static readonly DateOnly Mature = new(2026, 11, 30);

    [Fact]
    public void Aguarda_enquanto_a_janela_posterior_nao_terminou()
    {
        var rows = Days(Event.AddDays(-16), 14, 50, 100, 500);
        rows.AddRange(Days(Event.AddDays(1), 5, 50, 100, 500));

        var result = AdsBeforeAfter.Evaluate(rows, Input(), Settings, Event.AddDays(6));

        Assert.Equal("aguardando", result.Status);
        Assert.Equal("aguardando", result.Verdict);
        Assert.Equal(5, result.ElapsedDays);
        Assert.Empty(result.Deltas);
    }

    [Fact]
    public void Melhora_quando_retorno_e_receita_sobem()
    {
        // ROAS 5 -> 6,5; receita por dia 500 -> 650.
        var result = AdsBeforeAfter.Evaluate(Series(50, 100, 500, 50, 100, 650), Input(), Settings, Mature);

        Assert.Equal("final", result.Status);
        Assert.Equal("melhorou", result.Verdict);
        Assert.Equal("alta", result.Confidence);
        Assert.Equal(30, result.Deltas.First(d => d.Key == "roas").ChangePct);
    }

    [Fact]
    public void Misto_quando_gasta_mais_e_o_retorno_cai_mas_a_receita_sobe()
    {
        // Gasto 100 -> 150/dia; receita 500 -> 600/dia (ROAS 5 -> 4).
        var result = AdsBeforeAfter.Evaluate(Series(50, 100, 500, 60, 150, 600), Input(), Settings, Mature);

        Assert.Equal("misto", result.Verdict);
        // (600-500)*14 / (150-100)*14 = 2,00 de receita a mais por R$ 1 a mais.
        Assert.Equal(2.0, result.IncrementalRoas);
    }

    [Fact]
    public void Piora_quando_retorno_e_receita_caem()
    {
        var result = AdsBeforeAfter.Evaluate(Series(50, 100, 500, 50, 100, 350), Input(), Settings, Mature);

        Assert.Equal("piorou", result.Verdict);
    }

    [Fact]
    public void Retorno_melhor_mas_gasto_acima_do_teto_vira_misto()
    {
        var settings = new AdsPlaybookSettings { CostTargetMinPct = 3, CostTargetMaxPct = 6 };
        var rows = Days(Event.AddDays(-16), 14, 50, 40, 400);
        rows.ForEach(r => r.OrganicRevenue = 600);   // TACOS antes: 40 / 1000 = 4%
        var after = Days(Event.AddDays(1), 14, 50, 40, 450);
        after.ForEach(r => r.OrganicRevenue = 200);  // TACOS depois: 40 / 650 = 6,15%, acima do teto de 6%
        rows.AddRange(after);

        var result = AdsBeforeAfter.Evaluate(rows, Input(), settings, Mature);

        Assert.Equal("misto", result.Verdict);
        Assert.Contains("passou do teto", result.Reason);
    }

    [Fact]
    public void Neutro_dentro_da_faixa_de_estabilidade()
    {
        var result = AdsBeforeAfter.Evaluate(Series(50, 100, 500, 50, 100, 510), Input(), Settings, Mature);

        Assert.Equal("neutro", result.Verdict);
    }

    [Fact]
    public void Inconclusivo_com_poucos_cliques()
    {
        // 14 dias x 5 cliques = 70 cliques (< 100 mínimos), mesmo com grande variação.
        var result = AdsBeforeAfter.Evaluate(Series(5, 10, 50, 5, 10, 120), Input(), Settings, Mature);

        Assert.Equal("inconclusivo", result.Verdict);
        Assert.Contains("Amostra pequena", result.Reason);
        Assert.Equal("baixa", result.Confidence);
    }

    [Fact]
    public void Inconclusivo_quando_a_campanha_quase_nao_entregou_depois()
    {
        var rows = Days(Event.AddDays(-16), 14, 50, 100, 500);
        rows.AddRange(Days(Event.AddDays(1), 3, 50, 100, 500)); // só 3 de 14 dias com entrega

        var result = AdsBeforeAfter.Evaluate(rows, Input(), Settings, Mature, syncedOn: Mature);

        Assert.Equal("inconclusivo", result.Verdict);
        Assert.Contains("poucos dias", result.Reason);
    }

    [Fact]
    public void Provisorio_ate_as_vendas_atribuidas_amadurecerem()
    {
        // Janela posterior termina em 04/10; definitivo só depois de 14 dias de atribuição.
        var result = AdsBeforeAfter.Evaluate(Series(50, 100, 500, 50, 100, 650), Input(), Settings, new DateOnly(2026, 10, 10));

        Assert.Equal("provisorio", result.Status);
        Assert.Equal("melhorou", result.Verdict);
        Assert.Equal("baixa", result.Confidence);
    }

    [Fact]
    public void Dias_entre_a_sincronizacao_anterior_e_a_deteccao_ficam_fora_da_comparacao()
    {
        var rows = Series(50, 100, 500, 50, 100, 500);
        // Dias 18, 19 e 20 (intervalo em que a mudança ocorreu): valores absurdos que não podem contaminar nada.
        rows.AddRange(Days(Gap, 3, 500, 9999, 9999));

        var result = AdsBeforeAfter.Evaluate(rows, Input(), Settings, Mature);

        Assert.Equal("neutro", result.Verdict);
        Assert.Equal(new DateOnly(2026, 9, 4), result.Before!.From);
        Assert.Equal(new DateOnly(2026, 9, 17), result.Before.To);
        Assert.Equal(new DateOnly(2026, 9, 21), result.After!.From);
    }

    [Fact]
    public void Outros_fatores_ou_efeito_indireto_reduzem_a_confianca()
    {
        var rows = Series(50, 100, 500, 50, 100, 650);

        Assert.Equal("baixa", AdsBeforeAfter.Evaluate(rows, Input(confounders: 1), Settings, Mature).Confidence);
        var indirect = AdsBeforeAfter.Evaluate(rows, Input(indirect: true), Settings, Mature);
        Assert.Equal("baixa", indirect.Confidence);
        Assert.Contains(indirect.Notes, n => n.Contains("indireto"));
    }

    [Fact]
    public void Sem_metricas_a_avaliacao_fica_indisponivel()
    {
        var result = AdsBeforeAfter.Evaluate([], Input(), Settings, Mature);

        Assert.Equal("indisponivel", result.Status);
    }

    // ---- Ligação entre mudanças detectadas e recomendações aprovadas ----

    private static readonly DateTimeOffset T0 = new(2026, 9, 10, 12, 0, 0, TimeSpan.Zero);

    private static AdRecommendation Rec(int id, string action, DateTimeOffset? decided, long campaign = 1, string status = "Approved") =>
        new() { Id = id, Action = action, CampaignId = campaign, Status = status, DecidedAt = decided };

    private static AdChange Change(int id, string field, DateTimeOffset at, long campaign = 1) =>
        new() { Id = id, Field = field, CampaignId = campaign, DetectedAt = at };

    [Fact]
    public void Liga_mudanca_de_orcamento_a_recomendacao_aprovada_antes_dela()
    {
        var links = AdsDiaryLinker.Link(
            [Change(1, "Budget", T0.AddDays(2))],
            [Rec(10, "increase_budget", T0)],
            new HashSet<int>());

        Assert.Equal(10, links[1]);
    }

    [Theory]
    [InlineData("increase_budget", "RoasTarget")]   // campo diferente
    [InlineData("improve_listing", "Budget")]       // ação que não altera configuração
    public void Nao_liga_quando_acao_e_campo_nao_combinam(string action, string field)
    {
        var links = AdsDiaryLinker.Link([Change(1, field, T0.AddDays(2))], [Rec(10, action, T0)], new HashSet<int>());

        Assert.Empty(links);
    }

    [Fact]
    public void Nao_liga_mudanca_anterior_a_aprovacao_nem_de_outra_campanha_nem_recomendacao_nao_aprovada()
    {
        var change = Change(1, "Budget", T0);

        Assert.Empty(AdsDiaryLinker.Link([change], [Rec(10, "increase_budget", T0.AddDays(1))], new HashSet<int>()));
        Assert.Empty(AdsDiaryLinker.Link([change], [Rec(10, "increase_budget", T0.AddDays(-1), campaign: 2)], new HashSet<int>()));
        Assert.Empty(AdsDiaryLinker.Link([change], [Rec(10, "increase_budget", T0.AddDays(-1), status: "Rejected")], new HashSet<int>()));
    }

    [Fact]
    public void Cada_recomendacao_liga_so_uma_mudanca()
    {
        var links = AdsDiaryLinker.Link(
            [Change(1, "Budget", T0.AddDays(2)), Change(2, "Budget", T0.AddDays(5))],
            [Rec(10, "decrease_budget", T0)],
            new HashSet<int>());

        Assert.Single(links);
        Assert.Equal(10, links[1]);
        Assert.Empty(AdsDiaryLinker.Link([Change(3, "Budget", T0.AddDays(6))], [Rec(10, "decrease_budget", T0)], new HashSet<int> { 10 }));
    }
}
