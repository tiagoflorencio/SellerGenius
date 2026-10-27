using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace AgentOffice.Api.Ads;

// ---- Formato que pedimos à IA (chaves em português, conforme o manual) ----

public record LlmDataQuality(
    [property: JsonPropertyName("estado")] string? Estado,
    [property: JsonPropertyName("faltantes")] List<string>? Faltantes,
    [property: JsonPropertyName("alertas")] List<string>? Alertas);

public record LlmTest(
    [property: JsonPropertyName("mudanca")] string? Mudanca,
    [property: JsonPropertyName("magnitude")] string? Magnitude,
    [property: JsonPropertyName("janela_avaliacao_dias")] int? JanelaAvaliacaoDias,
    [property: JsonPropertyName("metrica_sucesso")] string? MetricaSucesso,
    [property: JsonPropertyName("criterio")] string? Criterio);

public record LlmRecommendation(
    [property: JsonPropertyName("escopo")] string? Escopo,
    [property: JsonPropertyName("campaign_id")] long? CampaignId,
    [property: JsonPropertyName("ad_group_id")] long? AdGroupId,
    [property: JsonPropertyName("acao")] string? Acao,
    [property: JsonPropertyName("onde")] string? Onde,
    [property: JsonPropertyName("titulo")] string? Titulo,
    [property: JsonPropertyName("diagnostico")] string? Diagnostico,
    [property: JsonPropertyName("evidencias")] List<string>? Evidencias,
    [property: JsonPropertyName("hipoteses")] List<string>? Hipoteses,
    [property: JsonPropertyName("teste_sugerido")] LlmTest? TesteSugerido,
    [property: JsonPropertyName("efeito_esperado")] string? EfeitoEsperado,
    [property: JsonPropertyName("riscos")] List<string>? Riscos,
    [property: JsonPropertyName("confianca")] string? Confianca,
    [property: JsonPropertyName("codigos_achados")] List<string>? CodigosAchados);

public record LlmNotRecommended(
    [property: JsonPropertyName("tema")] string? Tema,
    [property: JsonPropertyName("motivo")] string? Motivo);

public record LlmOutput(
    [property: JsonPropertyName("resumo")] string? Resumo,
    [property: JsonPropertyName("qualidade_dados")] LlmDataQuality? QualidadeDados,
    [property: JsonPropertyName("recomendacoes")] List<LlmRecommendation>? Recomendacoes,
    [property: JsonPropertyName("nao_recomendado")] List<LlmNotRecommended>? NaoRecomendado);

// ---- Resultado depois das travas e do cálculo de prioridade (feitos pelo código, não pela IA) ----

public record PriorityBreakdown(double Impact, double Confidence, double Urgency, double Ease, double Reversibility);

// O que fica gravado em AdRecommendation.ContentJson e vai para a tela.
public record RecommendationContent(
    [property: JsonPropertyName("titulo")] string Titulo,
    [property: JsonPropertyName("diagnostico")] string Diagnostico,
    [property: JsonPropertyName("evidencias")] List<string> Evidencias,
    [property: JsonPropertyName("hipoteses")] List<string> Hipoteses,
    [property: JsonPropertyName("teste_sugerido")] LlmTest? TesteSugerido,
    [property: JsonPropertyName("efeito_esperado")] string? EfeitoEsperado,
    [property: JsonPropertyName("riscos")] List<string> Riscos,
    [property: JsonPropertyName("confianca")] string Confianca,
    [property: JsonPropertyName("pontuacao")] PriorityBreakdown Pontuacao,
    [property: JsonPropertyName("achados")] List<AdFinding> Achados);

public record ProcessedRecommendation(
    string Scope,
    long? CampaignId,
    long? AdGroupId,
    string Action,
    string Where,
    string Title,
    double Priority,
    string Confidence,
    decimal? ImpactBrl,
    bool ImpactUnknown,
    List<string> FindingCodes,
    RecommendationContent Content);

public record DiscardedItem(string Title, string Reason);

public record ProcessResult(List<ProcessedRecommendation> Recommendations, List<DiscardedItem> Discarded);

// As travas do código sobre o que a IA propõe. A IA interpreta e escreve; o código valida, pontua e filtra.
// Assim números e prioridades são auditáveis e o agente não consegue, por exemplo, sugerir duas mudanças
// conflitantes na mesma campanha nem mexer numa campanha que acabou de ser alterada.
public static class AdsRecommendationProcessor
{
    // Facilidade e reversibilidade (0-100) de cada tipo de ação, e onde/em que escopo ela vale.
    private record ActionInfo(string Where, double Ease, double Reversibility, string[] Scopes, bool ChangesCampaignSettings);

    private static readonly Dictionary<string, ActionInfo> Actions = new()
    {
        ["increase_budget"] = new(Where.Campaign, 90, 100, ["campaign"], true),
        ["decrease_budget"] = new(Where.Campaign, 90, 100, ["campaign"], true),
        ["increase_roas_target"] = new(Where.Campaign, 80, 100, ["campaign"], true),
        ["decrease_roas_target"] = new(Where.Campaign, 80, 100, ["campaign"], true),
        ["pause_ad_group"] = new(Where.Campaign, 85, 90, ["ad_group"], false),
        ["move_ad_group_to_growth_campaign"] = new(Where.Campaign, 60, 80, ["ad_group"], false),
        ["move_ad_group_to_profit_campaign"] = new(Where.Campaign, 60, 80, ["ad_group"], false),
        ["improve_listing"] = new(Where.Listing, 45, 60, ["campaign", "ad_group"], false),
        ["review_offer"] = new(Where.Offer, 50, 60, ["campaign", "ad_group"], false),
        ["review_costs"] = new("Financeiro", 70, 100, ["campaign", "ad_group", "account"], false),
        ["collect_more_data"] = new(Where.Campaign, 100, 100, ["campaign", "ad_group", "account"], false),
        ["no_action"] = new(Where.Campaign, 100, 100, ["campaign", "ad_group", "account"], false),
    };

    public static IReadOnlyCollection<string> ActionNames => Actions.Keys;

    // Um desvio de custo equivalente a esta fração do gasto total do período vale 100 pontos de impacto.
    private const double FullImpactShareOfSpend = 0.10;
    private const double UnknownImpactScore = 40;
    private const double UnknownImpactConfidenceFactor = 0.8; // manual: sem impacto estimado, reduzir a confiança

    public static ProcessResult Process(LlmOutput output, AdsContext ctx, DateTimeOffset now)
    {
        var s = ctx.Settings;
        var discarded = new List<DiscardedItem>();
        var accepted = new List<ProcessedRecommendation>();

        var totalCost = ctx.Inputs.SelectMany(i => i.Days).Sum(d => d.Cost);
        var campaignById = ctx.Analysis.Campaigns.ToDictionary(c => c.CampaignId);
        var campaignOfGroup = ctx.Inputs
            .SelectMany(i => i.Groups.Select(g => (Group: g.Group.AdGroupId, Campaign: i.Campaign.CampaignId)))
            .ToDictionary(x => x.Group, x => x.Campaign);

        foreach (var rec in output.Recomendacoes ?? [])
        {
            var title = rec.Titulo?.Trim() ?? "";
            void Drop(string reason) => discarded.Add(new DiscardedItem(string.IsNullOrEmpty(title) ? "(sem título)" : title, reason));

            var action = Normalize(rec.Acao);
            var scope = NormalizeScope(rec.Escopo);

            if (title.Length == 0 || string.IsNullOrWhiteSpace(rec.Diagnostico)) { Drop("Sem título ou sem diagnóstico."); continue; }
            if (!Actions.TryGetValue(action, out var info)) { Drop($"Ação desconhecida: {rec.Acao}."); continue; }
            if (!info.Scopes.Contains(scope)) { Drop($"A ação {action} não vale para o escopo {rec.Escopo}."); continue; }

            // Resolve a campanha/anúncio e confere se existem nos dados.
            long? campaignId = null;
            long? adGroupId = null;
            if (scope == "campaign")
            {
                if (rec.CampaignId is null || !campaignById.ContainsKey(rec.CampaignId.Value)) { Drop("Campanha inexistente nos dados."); continue; }
                campaignId = rec.CampaignId;
            }
            else if (scope == "ad_group")
            {
                if (rec.AdGroupId is null || !campaignOfGroup.TryGetValue(rec.AdGroupId.Value, out var parent)) { Drop("Anúncio de campanha inexistente nos dados."); continue; }
                adGroupId = rec.AdGroupId;
                campaignId = parent;
            }

            // Achados por regras que sustentam a recomendação (só os que realmente existem no escopo).
            var findings = FindingsInScope(ctx, scope, campaignId, adGroupId);
            var codes = (rec.CodigosAchados ?? [])
                .Where(c => findings.Any(f => f.Code == c))
                .Distinct()
                .ToList();
            var matched = findings.Where(f => codes.Contains(f.Code)).ToList();

            // Trava: com o custo de Ads já acima do teto, gastar mais (orçamento maior ou meta de ROAS menor) contraria a sua meta de custo.
            if (action is "increase_budget" or "decrease_roas_target" && campaignId is not null
                && campaignById[campaignId.Value].Diagnosis.Findings.Any(f => f.Code == "CUSTO_ACIMA_DA_FAIXA"))
            {
                Drop("O custo de Ads da campanha já está acima do teto; aumentar o gasto contraria a meta de custo.");
                continue;
            }

            // Trava: campanha alterada há pouco tempo aguarda a avaliação antes de nova mudança de configuração.
            if (info.ChangesCampaignSettings && campaignId is not null && s.RecentChangeDays > 0)
            {
                var recent = ctx.Changes[campaignId.Value].FirstOrDefault(c => now - c.DetectedAt < TimeSpan.FromDays(s.RecentChangeDays));
                if (recent is not null)
                {
                    Drop($"A campanha foi alterada em {recent.DetectedAt:dd/MM} ({recent.Field}); aguarde {s.RecentChangeDays} dias para avaliar antes de nova mudança.");
                    continue;
                }
            }

            // ---- Pontuação (0-100), toda calculada pelo código ----
            var impactBrl = matched.Where(f => f.ImpactBrl is not null).Select(f => f.ImpactBrl!.Value).DefaultIfEmpty().Max();
            decimal? impact = impactBrl > 0 ? impactBrl : null;
            var impactUnknown = impact is null;

            var impactScore = impactUnknown || totalCost <= 0
                ? UnknownImpactScore
                : Math.Min(100, (double)(impact!.Value / totalCost) / FullImpactShareOfSpend * 100);

            var confidenceLabel = NormalizeConfidence(rec.Confianca);
            var confidenceScore = confidenceLabel switch { "alta" => 85.0, "media" => 60.0, _ => 35.0 };
            if (impactUnknown) confidenceScore *= UnknownImpactConfidenceFactor;
            if (matched.Any(f => f.Code == "AMOSTRA_INSUFICIENTE")) confidenceScore = Math.Min(confidenceScore, 35);

            var urgencyScore = matched.Count == 0 ? 30 : matched.Max(f => f.Severity switch
            {
                Severity.Critical => 100.0,
                Severity.Attention => 70.0,
                Severity.Opportunity => 45.0,
                _ => 20.0,
            });

            var breakdown = new PriorityBreakdown(
                Math.Round(impactScore, 1), Math.Round(confidenceScore, 1), urgencyScore, info.Ease, info.Reversibility);

            var weights = new[] { s.WeightImpact, s.WeightConfidence, s.WeightUrgency, s.WeightEase, s.WeightReversibility };
            var scores = new[] { breakdown.Impact, breakdown.Confidence, breakdown.Urgency, breakdown.Ease, breakdown.Reversibility };
            var priority = Math.Round(weights.Zip(scores, (w, v) => w * v).Sum() / weights.Sum(), 1);

            var content = new RecommendationContent(
                title,
                rec.Diagnostico!.Trim(),
                Clean(rec.Evidencias),
                Clean(rec.Hipoteses),
                rec.TesteSugerido,
                rec.EfeitoEsperado?.Trim(),
                Clean(rec.Riscos),
                confidenceLabel,
                breakdown,
                matched);

            accepted.Add(new ProcessedRecommendation(
                scope, campaignId, adGroupId, action, info.Where, title, priority, confidenceLabel,
                impact, impactUnknown, codes, content));
        }

        // ---- Trava: uma mudança de configuração por vez em cada campanha (manual, seção 4, regra 6) ----
        var conflicting = accepted
            .Where(r => Actions[r.Action].ChangesCampaignSettings)
            .GroupBy(r => r.CampaignId)
            .SelectMany(g => g.OrderByDescending(r => r.Priority).Skip(1))
            .ToHashSet();
        foreach (var r in conflicting)
            discarded.Add(new DiscardedItem(r.Title, "Conflita com outra mudança de configuração da mesma campanha; uma por vez."));
        accepted.RemoveAll(conflicting.Contains);

        // ---- Duplicadas (mesma ação no mesmo alvo) ----
        var duplicates = accepted
            .GroupBy(r => (r.Scope, r.CampaignId, r.AdGroupId, r.Action))
            .SelectMany(g => g.OrderByDescending(r => r.Priority).Skip(1))
            .ToHashSet();
        foreach (var r in duplicates) discarded.Add(new DiscardedItem(r.Title, "Duplicada."));
        accepted.RemoveAll(duplicates.Contains);

        // ---- Ordena por prioridade e respeita o limite ----
        var ordered = accepted.OrderByDescending(r => r.Priority).ToList();
        foreach (var r in ordered.Skip(s.MaxRecommendations))
            discarded.Add(new DiscardedItem(r.Title, $"Acima do limite de {s.MaxRecommendations} recomendações por análise."));

        return new ProcessResult(ordered.Take(s.MaxRecommendations).ToList(), discarded);
    }

    // Achados do escopo: da campanha, do anúncio de campanha, ou (conta) de todos.
    private static List<AdFinding> FindingsInScope(AdsContext ctx, string scope, long? campaignId, long? adGroupId)
    {
        var campaigns = ctx.Analysis.Campaigns;

        if (scope == "ad_group")
            return campaigns.SelectMany(c => c.Groups).FirstOrDefault(g => g.AdGroupId == adGroupId)?.Diagnosis.Findings ?? [];

        if (scope == "campaign")
        {
            var c = campaigns.FirstOrDefault(x => x.CampaignId == campaignId);
            return c is null ? [] : [.. c.Diagnosis.Findings, .. c.Groups.SelectMany(g => g.Diagnosis.Findings)];
        }

        return campaigns.SelectMany(c => c.Diagnosis.Findings).ToList();
    }

    private static List<string> Clean(List<string>? items) =>
        (items ?? []).Select(i => i?.Trim() ?? "").Where(i => i.Length > 0).ToList();

    // Minúsculas e sem acentos, para aceitar "média", "Media", "campaign " etc.
    private static string Normalize(string? value)
    {
        var text = (value ?? "").Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        return new string(text.Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
    }

    // Aceita sinônimos óbvios que o modelo às vezes usa no lugar do escopo (ex.: "publicação" = anúncio de campanha).
    private static string NormalizeScope(string? value) => Normalize(value) switch
    {
        "campanha" => "campaign",
        "publicacao" or "anuncio" or "listing" or "ad" or "adgroup" or "grupo" => "ad_group",
        "conta" => "account",
        var other => other,
    };

    private static string NormalizeConfidence(string? value) => Normalize(value) switch
    {
        "alta" => "alta",
        "media" => "media",
        _ => "baixa",
    };
}
