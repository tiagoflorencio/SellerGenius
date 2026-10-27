using System.Text.Json;
using AgentOffice.Api.Ai;
using AgentOffice.Api.Data;

namespace AgentOffice.Api.Ads;

public record AdvisorRunResult(AdAnalysisRun Run, List<AdRecommendation> Recommendations);

// O agente de Product Ads: entrega à IA os dados e achados por regras, junto com o manual, e grava as
// recomendações devolvidas como PENDENTES da sua decisão. Não altera nada no Mercado Livre.
// Divisão de trabalho: o código calcula (métricas, achados, prioridade, travas) e a IA interpreta, explica e sugere testes.
public class AdsAdvisor(
    AdsDiagnosisService diagnosis,
    AdsManualProvider manualProvider,
    OpenAiClient ai,
    AgentOfficeDb db,
    ILogger<AdsAdvisor> logger)
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    private const string SystemPromptTemplate = """
        Você é o agente analista de Mercado Livre Product Ads (Brasil) de um vendedor. Você analisa as métricas das
        campanhas e dos anúncios, identifica gargalos e propõe intervenções priorizadas, auditáveis e seguras.

        PRECEDÊNCIA DAS EVIDÊNCIAS (da mais forte para a mais fraca):
        1. Os dados da conta e os "parametros" vigentes no JSON recebido. Se o manual divergir dos parâmetros, vale o parâmetro.
        2. "licoes_aprendidas" (resultados medidos de mudanças anteriores nesta conta), quando houver.
        3. O manual abaixo. Ele é uma política INICIAL, não um conjunto fixo de regras: quando os dados da conta mostrarem
           outra realidade, diga isso e explique por quê, em vez de seguir o texto às cegas.
        4. Conhecimento geral de marketplace.

        DIVISÃO DE TRABALHO:
        - O sistema já calculou todas as métricas e os "achados" por regras (fonte da verdade). Você interpreta, prioriza,
          explica e sugere testes. Não recalcule métricas e não invente números: cite apenas valores presentes no JSON.
        - Valor null significa indisponível (nunca zero). Se faltar dado importante, diga em "qualidade_dados".
        - Margem/custo dos produtos NÃO foi informado; a meta de custo vem de "parametros" (faixa de CostTargetMinPct a
          CostTargetMaxPct sobre a base CostBasis; anúncios em crescimento toleram até GrowthCostMaxPct).

        CONTEXTO DE NEGÓCIO:
        - Anúncios com poucas vendas devem buscar crescimento (aceitam menos lucro para conseguir as primeiras vendas e avaliações).
        - Anúncios que já vendem devem escalar mantendo um ROAS lucrativo.
        - Vendas são atribuídas ao anúncio em até 14 dias após o clique: dados recentes de receita ainda estão incompletos.
        - Métricas de anúncios de família/catálogo são do grupo inteiro, compartilhadas entre várias publicações.
        - A conta é somente leitura para o sistema: toda recomendação será aplicada manualmente por uma pessoa, após aprovação.

        REGRAS DE SEGURANÇA:
        - Uma mudança de configuração por vez em cada campanha; nunca recomende ações conflitantes.
        - Não recomende mudar campanhas listadas em "mudancas_recentes" nos últimos dias dos parâmetros (aguardam avaliação).
        - Não conclua com amostra pequena ("AMOSTRA_INSUFICIENTE"): prefira "collect_more_data" e explique.
        - Se o custo de Ads já está acima do teto (achado "CUSTO_ACIMA_DA_FAIXA") não recomende aumentar orçamento nem
          baixar a meta de ROAS (isso aumenta o gasto); prefira corrigir conversão, oferta, anúncio ou mix. Explique o raciocínio
          da direção da mudança no "teste_sugerido" (baixar a meta de ROAS = o ML gasta mais; subir = gasta menos).
        - Trate correlação como hipótese. Informe confiança e riscos. Se não houver o que melhorar, devolva poucas
          recomendações (ou "no_action") e explique no resumo; não force recomendações.
        - Máximo de recomendações: o valor de parametros.maxRecommendations.

        AÇÕES PERMITIDAS ("acao") e o "escopo" exigido:
        - increase_budget, decrease_budget, adjust_roas_target  -> escopo "campaign"
        - pause_ad_group, move_ad_group_to_growth_campaign, move_ad_group_to_profit_campaign -> escopo "ad_group"
        - improve_listing (foto, título, atributos, descrição), review_offer (preço, frete, prazo, estoque),
          review_costs (margem/meta financeira), collect_more_data, no_action -> escopo "campaign", "ad_group" ou "account"
        - Use EXATAMENTE esses nomes de ação (não invente outros, como "review_roas_target": para a meta de ROAS use
          "adjust_roas_target"). "escopo" só aceita campaign, ad_group ou account; NÃO é o campo "onde".
          Para melhorar a foto/título de uma publicação, use escopo "ad_group" com o "ad_group_id" do anúncio.

        FORMATO DE SAÍDA: responda SOMENTE com um JSON, em português do Brasil, exatamente assim:
        {
          "resumo": "3 a 5 frases com o panorama da conta e o que mais importa agora",
          "qualidade_dados": {"estado": "suficiente|limitado|insuficiente", "faltantes": ["..."], "alertas": ["..."]},
          "recomendacoes": [
            {
              "escopo": "campaign|ad_group|account",
              "campaign_id": 123 ou null,
              "ad_group_id": 123 ou null,
              "acao": "uma das ações permitidas",
              "onde": "Campanha Ads|Publicação|Oferta|Operação|Financeiro",
              "titulo": "frase curta e acionável",
              "diagnostico": "o problema ou a oportunidade, em 1 a 3 frases",
              "evidencias": ["afirmações curtas com os números do JSON (ex.: 'TACOS de 13,7% contra teto de 10%')"],
              "hipoteses": ["causas prováveis, marcadas como hipótese"],
              "teste_sugerido": {"mudanca": "o que mudar", "magnitude": "quanto (ex.: +10% de orçamento)",
                                 "janela_avaliacao_dias": 14, "metrica_sucesso": "o que medir", "criterio": "quando considerar que funcionou"},
              "efeito_esperado": "o que se espera que aconteça, sem prometer resultados",
              "riscos": ["..."],
              "confianca": "alta|media|baixa",
              "codigos_achados": ["códigos dos achados do JSON que sustentam a recomendação"]
            }
          ],
          "nao_recomendado": [{"tema": "ex.: escalar orçamento da campanha X", "motivo": "por que decidiu não recomendar"}]
        }

        === MANUAL DO PROJETO (princípios e política inicial) ===
        {{MANUAL}}
        === FIM DO MANUAL ===
        """;

    public async Task<AdvisorRunResult> RunAsync(int days, CancellationToken ct)
    {
        var context = await diagnosis.BuildAsync(days, ct);
        if (context.Inputs.Count == 0)
            throw new InvalidOperationException("Não há campanhas salvas. Sincronize os dados de Ads primeiro.");

        var manual = manualProvider.Load();
        var system = SystemPromptTemplate.Replace("{{MANUAL}}", manual.Text);
        var input = BuildInput(context, DateTimeOffset.UtcNow);
        var inputJson = JsonSerializer.Serialize(input, Web);

        var result = await ai.CompleteJsonDetailedAsync(system, inputJson, ai.AdsModel, ct);

        LlmOutput output;
        try
        {
            output = JsonSerializer.Deserialize<LlmOutput>(result.Content, Web)
                     ?? throw new OpenAiException("A IA devolveu um JSON vazio.");
        }
        catch (JsonException ex)
        {
            logger.LogWarning("JSON inesperado da IA: {Message}", ex.Message);
            throw new OpenAiException("A IA devolveu um JSON inesperado. Tente novamente.");
        }

        var now = DateTimeOffset.UtcNow;
        var processed = AdsRecommendationProcessor.Process(output, context, now);

        var run = new AdAnalysisRun
        {
            CreatedAt = now,
            Model = result.Model,
            PeriodFrom = context.From,
            PeriodTo = context.To,
            ManualHash = manual.Hash,
            SettingsJson = JsonSerializer.Serialize(context.Settings, Web),
            InputJson = inputJson,
            OutputJson = result.Content,
            Summary = output.Resumo?.Trim() ?? "",
            DataQualityJson = JsonSerializer.Serialize(new
            {
                estado = output.QualidadeDados?.Estado,
                faltantes = output.QualidadeDados?.Faltantes ?? [],
                alertas = output.QualidadeDados?.Alertas ?? [],
                naoRecomendado = output.NaoRecomendado ?? [],
            }, Web),
            DiscardedJson = JsonSerializer.Serialize(processed.Discarded, Web),
            PromptTokens = result.PromptTokens,
            CompletionTokens = result.CompletionTokens,
        };
        db.AdAnalysisRuns.Add(run);
        await db.SaveChangesAsync(ct); // precisa do Id da análise para ligar as recomendações

        var recommendations = processed.Recommendations.Select(r => new AdRecommendation
        {
            RunId = run.Id,
            CreatedAt = now,
            Status = "Pending", // só muda quando VOCÊ aprovar ou rejeitar
            Scope = r.Scope,
            CampaignId = r.CampaignId,
            AdGroupId = r.AdGroupId,
            Action = r.Action,
            Where = r.Where,
            Title = r.Title,
            Priority = r.Priority,
            Confidence = r.Confidence,
            ImpactBrl = r.ImpactBrl,
            ImpactUnknown = r.ImpactUnknown,
            FindingCodes = string.Join(",", r.FindingCodes),
            ContentJson = JsonSerializer.Serialize(r.Content, Web),
        }).ToList();

        db.AdRecommendations.AddRange(recommendations);
        await db.SaveChangesAsync(ct);
        return new AdvisorRunResult(run, recommendations);
    }

    // Dados enviados à IA: compactos (menos tokens = menor custo), com nomes em português como o manual.
    public static object BuildInput(AdsContext ctx, DateTimeOffset now)
    {
        var diagByCampaign = ctx.Analysis.Campaigns.ToDictionary(c => c.CampaignId);
        var allDays = ctx.Inputs.SelectMany(i => i.Days).ToList();

        return new
        {
            periodo = new { inicio = ctx.From, fim = ctx.To, dias = ctx.To.DayNumber - ctx.From.DayNumber + 1 },
            moeda = "BRL",
            observacao_fuso = "Dados até o dia anterior; vendas atribuídas em até 14 dias após o clique.",
            parametros = ctx.Settings,
            referencias_da_conta = new
            {
                cvr_mediana_pct = ctx.Analysis.MedianCvr,
                anuncios_na_amostra_cvr = ctx.Analysis.CvrSampleGroups,
                totais = Metrics(AdMetricsCalculator.From(allDays)),
            },
            campanhas = ctx.Inputs.Select(i =>
            {
                var c = i.Campaign;
                var diag = diagByCampaign[c.CampaignId];
                var view = AdsEndpoints.BuildView(c, i.Days, ctx.From, ctx.To);

                return new
                {
                    id = c.CampaignId,
                    nome = c.Name,
                    status = c.Status,
                    estrategia = c.Strategy,
                    orcamento_dia = c.Budget,
                    meta_roas = c.RoasTarget,
                    uso_orcamento_pct = view.BudgetUsagePct,
                    impressoes_ganhas_pct = Pct(view.ImpressionShare),
                    perdidas_por_orcamento_pct = Pct(view.LostByBudget),
                    perdidas_por_ranking_pct = Pct(view.LostByRank),
                    metricas = Metrics(view.Metrics),
                    ultima_semana = Week(i.Days, ctx.To, 0),
                    semana_anterior = Week(i.Days, ctx.To, 7),
                    estado = diag.Diagnosis.State,
                    achados = diag.Diagnosis.Findings.Select(Finding).ToList(),
                    mudancas_recentes = ctx.Changes[c.CampaignId]
                        .Where(ch => now - ch.DetectedAt < TimeSpan.FromDays(30))
                        .OrderByDescending(ch => ch.DetectedAt)
                        .Select(ch => new { campo = ch.Field, de = ch.OldValue, para = ch.NewValue, detectado_em = ch.DetectedAt })
                        .ToList(),
                    anuncios = i.Groups
                        .Where(g => g.Group.Status == "ACTIVE" && (g.Group.Cost > 0 || g.Group.Prints > 0))
                        .OrderByDescending(g => g.Group.Cost)
                        .Take(15)
                        .Select(g =>
                        {
                            var gd = diag.Groups.First(x => x.AdGroupId == g.Group.AdGroupId).Diagnosis;
                            return new
                            {
                                id = g.Group.AdGroupId,
                                tipo = g.Group.AdGroupType,
                                titulo = g.Group.Title,
                                publicacoes_ligadas = g.LinkedListings,
                                tracao_unidades = g.Traction,
                                participacao_vendas_ads_pct = g.Group.Sov,
                                metricas = Metrics(AdMetricsCalculator.From(g.Group)),
                                estado = gd.State,
                                achados = gd.Findings.Where(f => f.Severity != Severity.Info).Select(Finding).ToList(),
                            };
                        }).ToList(),
                };
            }).ToList(),
            licoes_aprendidas = Array.Empty<object>(), // preenchido quando o diário de mudanças e a avaliação existirem
        };
    }

    private static object Metrics(AdMetrics m) => new
    {
        impressoes = m.Prints,
        cliques = m.Clicks,
        custo = m.Cost,
        receita_ads = m.Revenue,
        receita_organica = m.OrganicRevenue,
        unidades_ads = m.Units,
        unidades_organicas = m.OrganicUnits,
        ctr_pct = m.Ctr,
        cpc = m.Cpc,
        cvr_pct = m.Cvr,
        roas = m.Roas,
        acos_pct = m.Acos,
        tacos_pct = m.Tacos,
    };

    private static object Finding(AdFinding f) => new
    {
        codigo = f.Code,
        severidade = f.Severity,
        onde = f.Where,
        titulo = f.Title,
        evidencia = f.Evidence,
        impacto_brl = f.ImpactBrl,
    };

    private static double? Pct(double? fraction) => fraction is null ? null : Math.Round(fraction.Value * 100, 1);

    // Resumo de uma semana (imediatas: gasto, cliques, CTR, CPC), deslocada "offsetDays" dias para trás.
    private static object Week(List<AdCampaignDaily> days, DateOnly to, int offsetDays)
    {
        var end = to.AddDays(-offsetDays);
        var m = AdMetricsCalculator.From(days.Where(d => d.Date > end.AddDays(-7) && d.Date <= end));
        return new { custo = m.Cost, cliques = m.Clicks, impressoes = m.Prints, ctr_pct = m.Ctr, cpc = m.Cpc };
    }
}
