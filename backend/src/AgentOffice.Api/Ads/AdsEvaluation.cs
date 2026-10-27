using System.Globalization;
using AgentOffice.Api.Data;

namespace AgentOffice.Api.Ads;

// Onde e quando a mudança aconteceu. "GapStart" é o primeiro dia em que a mudança PODE ter ocorrido (o dia da
// sincronização anterior, no caso de mudanças detectadas); o dia do evento em si e o intervalo até ele ficam
// de fora da comparação, porque misturam configuração antiga e nova.
public record EvaluationInput(DateOnly EventDate, DateOnly GapStart, int WindowDays, bool Indirect, int Confounders);

public record WindowStats(DateOnly From, DateOnly To, int DaysWithDelivery, AdMetrics Metrics);

// Better: "higher" (subir é bom), "lower" (cair é bom) ou "neutral" (depende do objetivo, ex.: gasto).
// Direction: up, down, flat (dentro da faixa de estabilidade) ou na (sem base de comparação).
public record MetricDelta(string Key, string Label, string Unit, double? Before, double? After, double? ChangePct, string Direction, string Better);

public record EvaluationResult(
    string Status,        // aguardando, provisorio, final ou indisponivel
    string Verdict,       // melhorou, piorou, misto, neutro, inconclusivo ou aguardando
    string Reason,
    string Confidence,    // alta, media ou baixa
    int WindowDays,
    int ElapsedDays,      // dias de dados já disponíveis depois da mudança (até WindowDays)
    DateOnly? ReadyOn,    // quando a janela posterior termina
    WindowStats? Before,
    WindowStats? After,
    List<MetricDelta> Deltas,
    double? IncrementalRoas,
    List<string> Notes);

// Compara os resultados de uma campanha numa janela ANTES e numa janela DEPOIS de uma mudança. Todo o cálculo é
// feito aqui no código (nada de IA): a IA só lê o resultado. É uma indicação, não prova de causa: o manual pede
// janelas equivalentes e cuidado com correlação, promoções, preço, estoque e outras mudanças no mesmo período.
public static class AdsBeforeAfter
{
    private static readonly CultureInfo Br = new("pt-BR");

    // Vendas são atribuídas ao anúncio por até 14 dias após o clique: o resultado só amadurece depois disso.
    private const int AttributionDays = 14;

    // Fração mínima de dias com entrega (impressões ou gasto) em cada janela para comparar.
    private const double MinCoverage = 0.7;

    // "syncedOn" é o dia da última sincronização; sem ele, vale o último dia com métricas (dias sem linha, como campanha pausada, não contam como pendentes).
    public static EvaluationResult Evaluate(
        IReadOnlyCollection<AdCampaignDaily> days, EvaluationInput e, AdsPlaybookSettings s, DateOnly today, DateOnly? syncedOn = null)
    {
        var n = e.WindowDays;
        var afterFrom = e.EventDate.AddDays(1);
        var afterTo = e.EventDate.AddDays(n);
        var beforeTo = e.GapStart.AddDays(-1);
        var beforeFrom = e.GapStart.AddDays(-n);
        var notes = new List<string>();

        if (days.Count == 0)
            return Empty("indisponivel", "Não há métricas diárias desta campanha.", n, afterTo, notes);

        // Os dados do Mercado Livre vão até o dia anterior; só vale o que já foi sincronizado.
        var lastRow = days.Max(d => d.Date);
        var available = Min(syncedOn?.AddDays(-1) ?? lastRow, today.AddDays(-1));
        var elapsed = Math.Clamp(available.DayNumber - e.EventDate.DayNumber, 0, n);

        if (available < afterTo)
        {
            if (syncedOn is not null ? syncedOn < today : lastRow < today.AddDays(-1))
                notes.Add("Sincronize os dados de Ads para trazer os dias mais recentes.");
            return new EvaluationResult("aguardando", "aguardando",
                $"A janela de {n} dias depois da mudança termina em {afterTo.ToString("dd/MM", Br)}; há {elapsed} de {n} dias com dados.",
                "baixa", n, elapsed, afterTo, null, null, [], null, notes);
        }

        var before = Stats(days, beforeFrom, beforeTo);
        var after = Stats(days, afterFrom, afterTo);
        var deltas = BuildDeltas(days, before, after, beforeFrom, beforeTo, afterFrom, afterTo, n, s.EvaluationFlatBandPct);

        var matureOn = afterTo.AddDays(AttributionDays + 1);
        var status = today >= matureOn ? "final" : "provisorio";
        if (status == "provisorio")
            notes.Add($"As vendas atribuídas em até {AttributionDays} dias após o clique ainda estão sendo contabilizadas; o resultado fica definitivo a partir de {matureOn.ToString("dd/MM", Br)}.");

        if (e.EventDate > e.GapStart)
            notes.Add($"A mudança ocorreu entre {e.GapStart.ToString("dd/MM", Br)} e {e.EventDate.ToString("dd/MM", Br)} (data exata desconhecida); esse intervalo ficou fora da comparação.");
        if (e.Indirect)
            notes.Add("Efeito indireto: a mudança foi num anúncio ou publicação, mas a comparação usa a campanha inteira, então o efeito pode estar diluído.");
        if (e.Confounders > 0)
            notes.Add($"Houve {e.Confounders} outro(s) fator(es) no período (outra mudança ou anotação do diário): o resultado pode não ser só efeito desta mudança.");

        double? Value(string key) => deltas.First(d => d.Key == key).ChangePct;
        string Dir(string key) => deltas.First(d => d.Key == key).Direction;

        var incremental = IncrementalRoas(before.Metrics, after.Metrics, s.EvaluationFlatBandPct);

        string verdict;
        string reason;

        if (before.DaysWithDelivery < n * MinCoverage || after.DaysWithDelivery < n * MinCoverage)
        {
            verdict = "inconclusivo";
            reason = $"A campanha teve entrega em poucos dias (antes: {before.DaysWithDelivery} de {n}; depois: {after.DaysWithDelivery} de {n}); não dá para comparar com segurança.";
        }
        else if (before.Metrics.Clicks < s.MinClicksForCvr || after.Metrics.Clicks < s.MinClicksForCvr)
        {
            verdict = "inconclusivo";
            reason = $"Amostra pequena (cliques antes: {before.Metrics.Clicks}; depois: {after.Metrics.Clicks}; mínimo de {s.MinClicksForCvr}). Variações assim podem ser só ruído.";
        }
        else if (Dir("roas") == "na" || Dir("revenue_day") == "na")
        {
            verdict = "inconclusivo";
            reason = "Falta gasto ou receita em um dos períodos, então o retorno (ROAS) não pode ser comparado.";
        }
        else
        {
            (verdict, var lead) = Classify(Dir("roas"), Dir("revenue_day"), s.EvaluationFlatBandPct);
            reason = $"{lead} ROAS de {Fmt(before.Metrics.Roas)}x para {Fmt(after.Metrics.Roas)}x ({Signed(Value("roas"))}); receita por dia {Signed(Value("revenue_day"))}; gasto por dia {Signed(Value("cost_day"))}.";
            if (incremental is not null)
                reason += $" Cada R$ 1 a mais investido trouxe cerca de R$ {incremental.Value.ToString("0.00", Br)} a mais de receita.";

            // Retorno melhor não basta se o gasto passou do teto de custo definido nos parâmetros.
            var useAcos = s.CostBasis == "acos";
            var costAfter = useAcos ? after.Metrics.Acos : after.Metrics.Tacos;
            var costBefore = useAcos ? before.Metrics.Acos : before.Metrics.Tacos;
            if (verdict == "melhorou" && costAfter > s.CostTargetMaxPct && costAfter > costBefore)
            {
                verdict = "misto";
                reason += $" Atenção: o gasto com Ads ({(useAcos ? "ACOS" : "TACOS")} de {Fmt(costAfter)}%) passou do teto de {s.CostTargetMaxPct.ToString("0.#", Br)}%.";
            }
        }

        var minClicks = Math.Min(before.Metrics.Clicks, after.Metrics.Clicks);
        var confidence = verdict == "inconclusivo" || status == "provisorio" || e.Indirect || e.Confounders > 0
            ? "baixa"
            : minClicks >= s.MinClicksForCvr * 3 ? "alta" : "media";

        return new EvaluationResult(status, verdict, reason, confidence, n, n, afterTo, before, after, deltas, incremental, notes);
    }

    // Eficiência (ROAS) e escala (receita por dia) em conjunto: melhorar uma sem piorar a outra é bom;
    // trocar uma pela outra é "misto" e depende do seu objetivo (crescer ou lucrar).
    private static (string Verdict, string Lead) Classify(string efficiency, string scale, double band) => (efficiency, scale) switch
    {
        ("up", "up" or "flat") or ("flat", "up") => ("melhorou", "Retorno e/ou receita melhoraram, sem piora na outra frente."),
        ("flat", "flat") => ("neutro", $"Sem variação relevante (dentro de ±{band.ToString("0.#", Br)}%)."),
        ("down", "up") => ("misto", "Mais receita, porém com retorno menor."),
        ("up", "down") => ("misto", "Retorno melhor, porém com menos receita."),
        _ => ("piorou", "Retorno e/ou receita caíram, sem compensação na outra frente."),
    };

    private static double? IncrementalRoas(AdMetrics before, AdMetrics after, double band)
    {
        if (before.Cost <= 0) return null;
        var costDelta = after.Cost - before.Cost;
        if (costDelta <= 0 || (double)(costDelta / before.Cost) * 100 <= band) return null;
        return Math.Round((double)((after.Revenue - before.Revenue) / costDelta), 2);
    }

    private static WindowStats Stats(IReadOnlyCollection<AdCampaignDaily> days, DateOnly from, DateOnly to)
    {
        var rows = days.Where(d => d.Date >= from && d.Date <= to).ToList();
        return new WindowStats(from, to, rows.Count(d => d.Prints > 0 || d.Cost > 0), AdMetricsCalculator.From(rows));
    }

    private static List<MetricDelta> BuildDeltas(
        IReadOnlyCollection<AdCampaignDaily> days, WindowStats b, WindowStats a,
        DateOnly bFrom, DateOnly bTo, DateOnly aFrom, DateOnly aTo, int n, double band)
    {
        double PerDay(decimal v) => (double)v / n;

        double? Loss(DateOnly from, DateOnly to, Func<AdCampaignDaily, double?> pick)
        {
            var values = days.Where(d => d.Date >= from && d.Date <= to).Select(pick).Where(v => v is not null).Select(v => v!.Value).ToList();
            return values.Count == 0 ? null : Math.Round(values.Average() * 100, 1);
        }

        MetricDelta D(string key, string label, string unit, double? before, double? after, string better)
        {
            double? change = before is > 0 && after is not null ? (after.Value - before.Value) / before.Value * 100 : null;
            var direction = change is null ? "na" : Math.Abs(change.Value) <= band ? "flat" : change > 0 ? "up" : "down";
            return new MetricDelta(key, label, unit, before, after, change is null ? null : Math.Round(change.Value, 1), direction, better);
        }

        return
        [
            D("roas", "ROAS", "x", b.Metrics.Roas, a.Metrics.Roas, "higher"),
            D("revenue_day", "Receita por dia (ads)", "brl", Math.Round(PerDay(b.Metrics.Revenue), 2), Math.Round(PerDay(a.Metrics.Revenue), 2), "higher"),
            D("cost_day", "Gasto por dia", "brl", Math.Round(PerDay(b.Metrics.Cost), 2), Math.Round(PerDay(a.Metrics.Cost), 2), "neutral"),
            D("acos", "ACOS", "pct", b.Metrics.Acos, a.Metrics.Acos, "lower"),
            D("tacos", "TACOS", "pct", b.Metrics.Tacos, a.Metrics.Tacos, "lower"),
            D("clicks_day", "Cliques por dia", "num", Math.Round(PerDay(b.Metrics.Clicks), 1), Math.Round(PerDay(a.Metrics.Clicks), 1), "higher"),
            D("ctr", "CTR", "pct", b.Metrics.Ctr, a.Metrics.Ctr, "higher"),
            D("cpc", "CPC", "brl", (double?)b.Metrics.Cpc, (double?)a.Metrics.Cpc, "lower"),
            D("cvr", "Conversão", "pct", b.Metrics.Cvr, a.Metrics.Cvr, "higher"),
            D("lost_budget", "Perda por orçamento", "pct", Loss(bFrom, bTo, d => d.LostImpressionShareByBudget), Loss(aFrom, aTo, d => d.LostImpressionShareByBudget), "lower"),
            D("lost_rank", "Perda por ranking", "pct", Loss(bFrom, bTo, d => d.LostImpressionShareByRank), Loss(aFrom, aTo, d => d.LostImpressionShareByRank), "lower"),
        ];
    }

    public static EvaluationResult Unavailable(string reason, int windowDays) => Empty("indisponivel", reason, windowDays, null, []);

    private static EvaluationResult Empty(string status, string reason, int n, DateOnly? readyOn, List<string> notes) =>
        new(status, "inconclusivo", reason, "baixa", n, 0, readyOn, null, null, [], null, notes);

    private static DateOnly Min(DateOnly a, DateOnly b) => a < b ? a : b;

    private static string Fmt(double? v) => v is null ? "–" : v.Value.ToString("0.00", Br);

    private static string Signed(double? pct) => pct is null ? "sem base" : $"{(pct >= 0 ? "+" : "")}{pct.Value.ToString("0.#", Br)}%";
}
