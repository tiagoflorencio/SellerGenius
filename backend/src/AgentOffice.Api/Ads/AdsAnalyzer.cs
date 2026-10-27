using System.Globalization;
using AgentOffice.Api.Data;

namespace AgentOffice.Api.Ads;

public static class Severity
{
    public const string Critical = "critical";         // prejuízo claro ou problema grave com amostra suficiente
    public const string Attention = "attention";       // desvio que merece investigação
    public const string Opportunity = "opportunity";   // espaço para melhorar resultado (ex.: escalar)
    public const string Info = "info";                 // contexto, não exige ação
}

public static class DiagnosisState
{
    public const string Healthy = "saudavel";
    public const string Attention = "atencao";
    public const string Critical = "critico";
    public const string Inconclusive = "inconclusivo"; // pouco volume para concluir
    public const string NoData = "sem_dados";
}

// Onde intervir (manual, seção 6).
public static class Where
{
    public const string Campaign = "Campanha Ads";
    public const string Listing = "Publicação";
    public const string Offer = "Oferta";
}

public record AdFinding(string Code, string Severity, string Where, string Title, string Evidence, decimal? ImpactBrl);

public record AdDiagnosis(string State, List<AdFinding> Findings);

// Anúncio de campanha + o que sabemos das publicações ligadas a ele.
// "Tração" mede a maturidade do produto: o maior entre as vendas totais das publicações ligadas e as unidades vendidas
// (com e sem Ads) no período. O total sozinho subestima produtos do modelo de família/catálogo, cujas vendas
// nem sempre aparecem no item.
public record GroupInput(AdGroupMetric Group, int LinkedSoldUnits, int LinkedListings)
{
    public int Traction => Math.Max(LinkedSoldUnits, Group.Units + Group.OrganicUnits);
}

public record CampaignInput(AdCampaign Campaign, List<AdCampaignDaily> Days, List<GroupInput> Groups);

public record GroupDiagnosis(long AdGroupId, string Title, AdDiagnosis Diagnosis);

public record CampaignDiagnosis(long CampaignId, string Name, AdDiagnosis Diagnosis, List<GroupDiagnosis> Groups);

public record AdsAnalysis(double? MedianCvr, int CvrSampleGroups, List<CampaignDiagnosis> Campaigns);

// Diagnóstico por REGRAS dos dados de Product Ads, baseado no manual do projeto.
// Aponta ONDE e POR QUE há um problema ou oportunidade, sempre com números como evidência; nunca altera nada.
// Todos os limites vêm de AdsPlaybookSettings (editáveis), e a conversão é comparada com a mediana da PRÓPRIA conta,
// porque o manual não define faixas universais de CVR. Quanto mais histórico, melhor essa referência fica.
public static class AdsAnalyzer
{
    private static readonly CultureInfo Br = new("pt-BR");

    public static AdsAnalysis Analyze(IReadOnlyList<CampaignInput> campaigns, AdsPlaybookSettings s, DateOnly from, DateOnly to)
    {
        // Referência de conversão: mediana dos anúncios ativos com volume suficiente.
        var cvrs = campaigns
            .SelectMany(c => c.Groups)
            .Where(g => g.Group.Status == "ACTIVE" && g.Group.Clicks >= s.MinClicksForCvr && s.MinClicksForCvr > 0)
            .Select(g => g.Group.Units * 100.0 / g.Group.Clicks)
            .ToList();
        double? medianCvr = cvrs.Count >= 3 ? Median(cvrs) : null;

        var result = campaigns.Select(c => AnalyzeCampaign(c, s, medianCvr, from, to)).ToList();

        return new AdsAnalysis(medianCvr, cvrs.Count, result);
    }

    // ---------------------------------------------------------------- campanha

    private static CampaignDiagnosis AnalyzeCampaign(
        CampaignInput input, AdsPlaybookSettings s, double? medianCvr, DateOnly from, DateOnly to)
    {
        var c = input.Campaign;
        var m = AdMetricsCalculator.From(input.Days);
        var groups = input.Groups
            .OrderByDescending(g => g.Group.Cost)
            .Select(g => new GroupDiagnosis(g.Group.AdGroupId, g.Group.Title, AnalyzeGroup(g, c, s, medianCvr)))
            .ToList();

        if (m.Prints == 0) return new CampaignDiagnosis(c.CampaignId, c.Name, new AdDiagnosis(DiagnosisState.NoData, []), groups);

        var findings = new List<AdFinding>();
        var growth = IsGrowthStrategy(c.Strategy);
        var ceiling = growth ? s.GrowthCostMaxPct : s.CostTargetMaxPct;
        var sampleOk = s.MinClicksForCvr <= 0 || m.Clicks >= s.MinClicksForCvr;
        var costPct = CostPct(m, s);

        if (!sampleOk)
        {
            findings.Add(new AdFinding("AMOSTRA_INSUFICIENTE", Severity.Info, Where.Campaign,
                "Poucos cliques para concluir sobre custo e conversão",
                $"{m.Clicks} cliques no período; o mínimo configurado é {s.MinClicksForCvr}. Aguarde mais dados.", null));
        }
        else
        {
            AddCostFinding(findings, m, costPct, ceiling, s, Where.Campaign, "campanha");
        }

        // ---- Entrega: orçamento x ranking ----
        var withPrints = input.Days.Where(d => d.Prints > 0).ToList();
        var lostBudget = Average(withPrints, d => d.LostImpressionShareByBudget);
        var lostRank = Average(withPrints, d => d.LostImpressionShareByRank);
        var withinCeiling = costPct is not null && costPct <= ceiling;
        var aboveCeiling = costPct is not null && costPct > ceiling;

        var highBudgetLoss = lostBudget is not null && lostBudget * 100 >= s.LostByBudgetHighPct;
        var highRankLoss = lostRank is not null && lostRank * 100 >= s.LostByRankHighPct;

        if (highBudgetLoss)
        {
            if (withinCeiling)
                findings.Add(new AdFinding("ESCALAR_ORCAMENTO", Severity.Opportunity, Where.Campaign,
                    "Orçamento limita um resultado dentro da meta",
                    $"{Pct(lostBudget * 100)} das impressões perdidas por orçamento, com custo de {Pct(costPct)} (teto {Pct(ceiling)}). " +
                    "Teste um aumento gradual (de +10% a +20%) e acompanhe o retorno marginal.", null));
            else if (aboveCeiling)
                findings.Add(new AdFinding("PERDA_ORCAMENTO_COM_CUSTO_ALTO", Severity.Info, Where.Campaign,
                    "Perda por orçamento, mas o custo já está acima do teto",
                    $"{Pct(lostBudget * 100)} das impressões perdidas por orçamento; corrija a eficiência antes de escalar.", null));
        }

        if (highRankLoss)
        {
            if (withinCeiling)
                findings.Add(new AdFinding("META_RESTRITIVA", Severity.Opportunity, Where.Campaign,
                    "Perda alta por ranking com espaço no custo",
                    $"{Pct(lostRank * 100)} das impressões perdidas por ranking, com custo de {Pct(costPct)} (teto {Pct(ceiling)}). " +
                    "Considere um teste controlado com meta de ROAS menos restritiva (mude uma coisa por vez).", null));
            else if (aboveCeiling)
                findings.Add(new AdFinding("PERDA_RANKING_COM_CUSTO_ALTO", Severity.Attention, Where.Offer,
                    "Perda alta por ranking e custo acima do teto",
                    $"{Pct(lostRank * 100)} das impressões perdidas por ranking e custo de {Pct(costPct)} (teto {Pct(ceiling)}). " +
                    "Melhorar a competitividade da oferta (preço, foto, título, reputação) tende a ajudar mais do que pagar mais.", null));
        }

        // Orçamento sobrando sem perda por orçamento: o limite está na demanda ou na meta, não no orçamento.
        var windowDays = to.DayNumber - from.DayNumber + 1;
        if (c.Status == "active" && c.Budget > 0 && windowDays > 0 && !highBudgetLoss)
        {
            var usage = (double)(m.Cost / windowDays / c.Budget) * 100;
            if (usage < s.BudgetUnderusePct)
                findings.Add(new AdFinding("ORCAMENTO_SUBUTILIZADO", Severity.Info, Where.Campaign,
                    "Orçamento pouco utilizado",
                    $"Uso de {Pct(usage)} do orçamento. Aumentar o orçamento não ajuda; o limite é a demanda" +
                    (highRankLoss ? " ou a meta de ROAS (perda alta por ranking)." : "."), null));
        }

        // ---- Resultado x meta de ROAS definida no Mercado Livre ----
        if (sampleOk && c.RoasTarget is > 0 && m.Roas is not null && m.Roas < c.RoasTarget * s.RoasTargetTolerance)
        {
            findings.Add(new AdFinding("ROAS_ABAIXO_DA_META", Severity.Attention, Where.Campaign,
                "ROAS abaixo da meta da campanha",
                $"ROAS de {m.Roas:0.00}x contra meta de {c.RoasTarget:0.00}x. Decomponha em CPC, conversão e ticket médio antes de mudar a meta.", null));
        }

        // ---- Atração do anúncio ----
        if (m.Prints >= s.MinImpressionsForCtr && m.Ctr is not null && m.Ctr < s.CtrLowPct)
        {
            findings.Add(new AdFinding("CTR_BAIXO", Severity.Attention, Where.Listing,
                "CTR baixo",
                $"CTR de {Pct(m.Ctr)} em {m.Prints:N0} impressões (limite configurado {Pct(s.CtrLowPct)}). " +
                "Investigue foto principal, título e preço; não conclua que o lance é a causa.", null));
        }

        AddTrendFindings(findings, input.Days, s, to);
        AddObjectiveMismatch(findings, input.Groups, c, s);

        return new CampaignDiagnosis(c.CampaignId, c.Name, new AdDiagnosis(StateOf(findings, sampleOk), findings), groups);
    }

    // ---------------------------------------------------------------- anúncio de campanha (ad group)

    private static AdDiagnosis AnalyzeGroup(GroupInput input, AdCampaign campaign, AdsPlaybookSettings s, double? medianCvr)
    {
        var g = input.Group;
        if (g.Status != "ACTIVE") return new AdDiagnosis(DiagnosisState.NoData, []);

        var m = AdMetricsCalculator.From(g);
        if (m.Prints == 0) return new AdDiagnosis(DiagnosisState.NoData, []);

        var findings = new List<AdFinding>();
        var sampleOk = s.MinClicksForCvr <= 0 || m.Clicks >= s.MinClicksForCvr;

        // Objetivo esperado pelo produto: pouca venda => crescimento (aceita custo maior para ganhar tração).
        var growth = input.LinkedListings > 0
            ? input.Traction < s.GrowthMaxSoldUnits
            : IsGrowthStrategy(campaign.Strategy);
        var ceiling = growth ? s.GrowthCostMaxPct : s.CostTargetMaxPct;

        if (!sampleOk)
        {
            findings.Add(new AdFinding("AMOSTRA_INSUFICIENTE", Severity.Info, Where.Campaign,
                "Poucos cliques para concluir",
                $"{m.Clicks} cliques; o mínimo configurado é {s.MinClicksForCvr}.", null));
        }
        else
        {
            AddCostFinding(findings, m, CostPct(m, s), ceiling, s, Where.Campaign, "anúncio");

            // Cliques sem nenhuma venda: só é anormal se improvável por acaso dada a conversão típica da conta.
            if (m.Units == 0 && medianCvr is not null)
            {
                var chanceByLuck = Math.Pow(1 - medianCvr.Value / 100, m.Clicks);
                if (chanceByLuck < s.ZeroSalesProbability)
                    findings.Add(new AdFinding("SEM_VENDAS_ANORMAL", Severity.Critical, Where.Campaign,
                        "Cliques sem nenhuma venda",
                        $"{m.Clicks} cliques e nenhuma venda; com a conversão típica da conta ({Pct(medianCvr)}) a chance disso por acaso é de {chanceByLuck:P1}. " +
                        $"Gasto de {Money(m.Cost)}. Revise oferta (preço, frete, descrição) e considere pausar.", m.Cost));
            }
            else if (m.Cvr is not null && medianCvr is not null && m.Cvr < medianCvr * s.CvrLowFactor)
            {
                findings.Add(new AdFinding("CVR_ABAIXO_DA_MEDIANA", Severity.Attention, Where.Offer,
                    "Conversão abaixo da mediana da conta",
                    $"Conversão de {Pct(m.Cvr)} contra mediana de {Pct(medianCvr)} da conta (limite: {s.CvrLowFactor:0.##}x). " +
                    "Verifique preço, frete, prazo, avaliações, estoque e descrição.", null));
            }
        }

        if (m.Prints >= s.MinImpressionsForCtr && m.Ctr is not null && m.Ctr < s.CtrLowPct)
        {
            findings.Add(new AdFinding("CTR_BAIXO", Severity.Attention, Where.Listing,
                "CTR baixo",
                $"CTR de {Pct(m.Ctr)} em {m.Prints:N0} impressões (limite {Pct(s.CtrLowPct)}). Investigue foto principal, título e preço.", null));
        }

        // Estratégia da campanha x maturidade do produto.
        if (input.LinkedListings > 0)
        {
            var campaignGrowth = IsGrowthStrategy(campaign.Strategy);
            if (growth && !campaignGrowth)
                findings.Add(new AdFinding("OBJETIVO_DESALINHADO", Severity.Info, Where.Campaign,
                    "Produto em fase de crescimento numa campanha de rentabilidade",
                    $"O produto soma {input.Traction} unidades vendidas (total ou no período; limite de crescimento: {s.GrowthMaxSoldUnits}). " +
                    "Candidato a uma campanha de crescimento para ganhar as primeiras vendas e avaliações.", null));
            else if (!growth && campaignGrowth)
                findings.Add(new AdFinding("OBJETIVO_DESALINHADO", Severity.Info, Where.Campaign,
                    "Produto que já vende numa campanha de crescimento",
                    $"O produto soma {input.Traction} unidades vendidas. Candidato a uma campanha de rentabilidade/escala com ROAS lucrativo.", null));
        }

        return new AdDiagnosis(StateOf(findings, sampleOk), findings);
    }

    // ---------------------------------------------------------------- regras compartilhadas

    // Compara o custo (% do valor vendido) com a faixa-alvo. Acima do teto = atenção; muito acima = crítico.
    private static void AddCostFinding(List<AdFinding> findings, AdMetrics m, double? costPct, double ceiling, AdsPlaybookSettings s, string where, string subject)
    {
        if (costPct is null) return;

        var baseRevenue = s.CostBasis == "tacos" ? m.Revenue + m.OrganicRevenue : m.Revenue;
        var basis = s.CostBasis.ToUpperInvariant();

        if (costPct > ceiling)
        {
            var excess = Math.Round(m.Cost - (decimal)ceiling / 100m * baseRevenue, 2);
            var critical = costPct > ceiling * s.CriticalCostFactor;
            findings.Add(new AdFinding("CUSTO_ACIMA_DA_FAIXA", critical ? Severity.Critical : Severity.Attention, where,
                $"Custo de Ads acima do teto ({subject})",
                $"{basis} de {Pct(costPct)} contra teto de {Pct(ceiling)}; " +
                (excess > 0 ? $"cerca de {Money(excess)} acima do teto no período. " : "") +
                "Avalie ROAS objetivo, conversão e mix de anúncios.", excess > 0 ? excess : null));
        }
        else if (costPct < s.CostTargetMinPct)
        {
            findings.Add(new AdFinding("CUSTO_ABAIXO_DA_FAIXA", Severity.Info, where,
                $"Custo de Ads abaixo da faixa ({subject})",
                $"{basis} de {Pct(costPct)}, abaixo do mínimo de {Pct(s.CostTargetMinPct)}: pode haver espaço para investir mais, se houver demanda.", null));
        }
    }

    // Tendência: últimos 7 dias contra os 7 anteriores, só em métricas que não dependem da atribuição de vendas (até 14 dias).
    private static void AddTrendFindings(List<AdFinding> findings, List<AdCampaignDaily> days, AdsPlaybookSettings s, DateOnly to)
    {
        var last = days.Where(d => d.Date > to.AddDays(-7)).ToList();
        var previous = days.Where(d => d.Date <= to.AddDays(-7) && d.Date > to.AddDays(-14)).ToList();
        if (last.Count < 7 || previous.Count < 7) return;

        var a = AdMetricsCalculator.From(last);
        var b = AdMetricsCalculator.From(previous);
        if (b.Clicks < Math.Max(1, s.MinClicksForCvr / 3) || a.Clicks == 0) return;

        double Change(decimal now, decimal then) => then == 0 ? 0 : (double)((now - then) / then) * 100;

        var cost = Change(a.Cost, b.Cost);
        if (cost >= s.TrendChangePct)
            findings.Add(new AdFinding("CUSTO_SUBINDO", Severity.Info, Where.Campaign, "Gasto subindo",
                $"Gasto dos últimos 7 dias {Money(a.Cost)}, {Pct(cost)} acima dos 7 anteriores ({Money(b.Cost)}). " +
                "Confira se a receita acompanhou (vendas levam até 14 dias para serem atribuídas).", null));

        if (a.Cpc is not null && b.Cpc is not null)
        {
            var cpc = Change(a.Cpc.Value, b.Cpc.Value);
            if (cpc >= s.TrendChangePct)
                findings.Add(new AdFinding("CPC_SUBINDO", Severity.Info, Where.Campaign, "CPC subindo",
                    $"CPC de {Money(a.Cpc.Value)} contra {Money(b.Cpc.Value)} na semana anterior ({Pct(cpc)}). Sinal de mais disputa no leilão.", null));
        }

        if (a.Ctr is not null && b.Ctr is not null && b.Ctr > 0)
        {
            var ctr = (a.Ctr.Value - b.Ctr.Value) / b.Ctr.Value * 100;
            if (ctr <= -s.TrendChangePct)
                findings.Add(new AdFinding("CTR_CAINDO", Severity.Attention, Where.Listing, "CTR caindo",
                    $"CTR de {Pct(a.Ctr)} contra {Pct(b.Ctr)} na semana anterior ({Pct(ctr)}). Veja foto, título, preço e concorrência.", null));
        }
    }

    // Resume, no nível da campanha, o gasto em anúncios cujo objetivo esperado difere da estratégia da campanha.
    private static void AddObjectiveMismatch(List<AdFinding> findings, List<GroupInput> groups, AdCampaign c, AdsPlaybookSettings s)
    {
        var campaignGrowth = IsGrowthStrategy(c.Strategy);
        var linked = groups.Where(g => g.LinkedListings > 0 && g.Group.Status == "ACTIVE" && g.Group.Cost > 0).ToList();
        var totalCost = linked.Sum(g => g.Group.Cost);
        if (totalCost <= 0) return;

        var mismatched = linked.Where(g => (g.Traction < s.GrowthMaxSoldUnits) != campaignGrowth).ToList();
        var share = (double)(mismatched.Sum(g => g.Group.Cost) / totalCost) * 100;
        if (mismatched.Count == 0 || share < 20) return;

        findings.Add(new AdFinding("OBJETIVO_DESALINHADO", Severity.Info, Where.Campaign,
            campaignGrowth ? "Anúncios que já vendem numa campanha de crescimento" : "Anúncios em fase de crescimento numa campanha de rentabilidade",
            $"{mismatched.Count} anúncio(s), {Pct(share)} do gasto, têm objetivo esperado diferente da estratégia da campanha " +
            $"(limite de crescimento: {s.GrowthMaxSoldUnits} unidades vendidas, total ou no período).", null));
    }

    // ---------------------------------------------------------------- utilitários

    private static bool IsGrowthStrategy(string? strategy) => strategy is "INCREASE" or "VISIBILITY";

    private static double? CostPct(AdMetrics m, AdsPlaybookSettings s) => s.CostBasis == "acos" ? m.Acos : m.Tacos;

    // crítico > atenção > (sem amostra suficiente => inconclusivo) > saudável. Oportunidades e informações não pioram o estado.
    private static string StateOf(List<AdFinding> findings, bool sampleOk)
    {
        if (findings.Any(f => f.Severity == Severity.Critical)) return DiagnosisState.Critical;
        if (findings.Any(f => f.Severity == Severity.Attention)) return DiagnosisState.Attention;
        return sampleOk ? DiagnosisState.Healthy : DiagnosisState.Inconclusive;
    }

    private static double? Average(List<AdCampaignDaily> days, Func<AdCampaignDaily, double?> selector)
    {
        var values = days.Select(selector).OfType<double>().ToList();
        return values.Count == 0 ? null : values.Average();
    }

    private static double Median(List<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        var mid = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
    }

    private static string Pct(double? value) => value is null ? "–" : $"{value.Value.ToString("0.##", Br)}%";

    private static string Money(decimal value) => value.ToString("C2", Br);
}
