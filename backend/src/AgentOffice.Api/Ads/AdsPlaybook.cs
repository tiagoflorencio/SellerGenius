using System.Text.Json;
using AgentOffice.Api.Data;

namespace AgentOffice.Api.Ads;

// Parâmetros do diagnóstico de Ads. São "heurísticas iniciais" (vêm do manual do projeto e das suas metas):
// ficam no banco para você ajustar, e no futuro o agente poderá propor novos valores com base no que aprendeu.
// Os valores padrão abaixo são pontos de partida, não verdades fixas nem benchmarks oficiais do Mercado Livre.
public class AdsPlaybookSettings
{
    // ---- Meta de custo de publicidade (substitui a margem enquanto o custo dos produtos não for informado) ----

    // Base do percentual: "tacos" (gasto / vendas totais do produto, com e sem Ads) ou "acos" (gasto / vendas por Ads).
    public string CostBasis { get; set; } = "tacos";

    // Faixa ideal de gasto com Ads em % do valor vendido.
    public double CostTargetMinPct { get; set; } = 8;
    public double CostTargetMaxPct { get; set; } = 10;

    // Teto tolerado para anúncios em fase de crescimento (poucas vendas): aceitam menos lucro para ganhar tração.
    public double GrowthCostMaxPct { get; set; } = 20;

    // Acima de (teto x fator) o problema é classificado como crítico.
    public double CriticalCostFactor { get; set; } = 1.3;

    // Produto com menos unidades vendidas (no total) que isso é considerado em fase de crescimento.
    public int GrowthMaxSoldUnits { get; set; } = 20;

    // ---- Volume mínimo para julgar (evita concluir com ruído) ----
    public int MinImpressionsForCtr { get; set; } = 1000;
    public int MinClicksForCvr { get; set; } = 100;

    // ---- Limites de atenção ----
    public double CtrLowPct { get; set; } = 0.3;               // CTR abaixo disso (com impressões suficientes) = investigar
    public double CvrLowFactor { get; set; } = 0.6;            // conversão abaixo de (mediana da conta x fator) = baixa
    public double ZeroSalesProbability { get; set; } = 0.05;   // "sem vendas" só é anormal se a chance de acontecer por acaso for menor que isso
    public double RoasTargetTolerance { get; set; } = 0.9;     // ROAS abaixo de (meta x tolerância) = abaixo da meta
    public double LostByBudgetHighPct { get; set; } = 20;      // impressões perdidas por orçamento
    public double LostByRankHighPct { get; set; } = 40;        // impressões perdidas por ranking
    public double BudgetUnderusePct { get; set; } = 40;        // uso do orçamento abaixo disso = subutilizado
    public double TrendChangePct { get; set; } = 30;           // variação entre os últimos 7 dias e os 7 anteriores

    // ---- Agente de recomendações ----
    public int MaxRecommendations { get; set; } = 8;           // máximo de recomendações por análise
    public int RecentChangeDays { get; set; } = 14;            // campanha alterada há menos que isso aguarda avaliação antes de nova mudança

    // Pesos da prioridade (manual, seção 7). São relativos: o que importa é a proporção entre eles.
    public double WeightImpact { get; set; } = 35;
    public double WeightConfidence { get; set; } = 25;
    public double WeightUrgency { get; set; } = 20;
    public double WeightEase { get; set; } = 10;
    public double WeightReversibility { get; set; } = 10;

    // Retorna a mensagem do primeiro problema encontrado, ou null se os valores são válidos.
    public string? Validate()
    {
        if (CostBasis is not ("tacos" or "acos")) return "CostBasis deve ser \"tacos\" ou \"acos\".";
        if (CostTargetMinPct < 0 || CostTargetMaxPct > 100) return "A faixa de custo deve estar entre 0 e 100%.";
        if (CostTargetMinPct >= CostTargetMaxPct) return "O mínimo da faixa de custo deve ser menor que o máximo.";
        if (GrowthCostMaxPct < CostTargetMaxPct || GrowthCostMaxPct > 100) return "O teto de crescimento deve ser maior ou igual ao teto normal (e até 100%).";
        if (CriticalCostFactor < 1) return "O fator crítico deve ser pelo menos 1.";
        if (GrowthMaxSoldUnits < 0) return "O limite de vendas para crescimento não pode ser negativo.";
        if (MinImpressionsForCtr < 0 || MinClicksForCvr < 0) return "Os volumes mínimos não podem ser negativos.";
        if (CtrLowPct < 0 || CtrLowPct > 100) return "CTR baixo deve estar entre 0 e 100%.";
        if (CvrLowFactor <= 0 || CvrLowFactor > 1) return "O fator de conversão baixa deve estar entre 0 e 1.";
        if (ZeroSalesProbability <= 0 || ZeroSalesProbability >= 1) return "A probabilidade de 'sem vendas' deve estar entre 0 e 1.";
        if (RoasTargetTolerance <= 0 || RoasTargetTolerance > 1) return "A tolerância da meta de ROAS deve estar entre 0 e 1.";
        if (LostByBudgetHighPct is < 0 or > 100 || LostByRankHighPct is < 0 or > 100 || BudgetUnderusePct is < 0 or > 100)
            return "Os percentuais de perda e uso de orçamento devem estar entre 0 e 100.";
        if (TrendChangePct <= 0) return "A variação de tendência deve ser maior que zero.";
        if (MaxRecommendations is < 1 or > 20) return "O máximo de recomendações deve estar entre 1 e 20.";
        if (RecentChangeDays is < 0 or > 60) return "Os dias de espera após uma mudança devem estar entre 0 e 60.";
        if (new[] { WeightImpact, WeightConfidence, WeightUrgency, WeightEase, WeightReversibility }.Any(w => w < 0)
            || WeightImpact + WeightConfidence + WeightUrgency + WeightEase + WeightReversibility <= 0)
            return "Os pesos da prioridade não podem ser negativos e a soma deve ser maior que zero.";
        return null;
    }
}

// Lê e grava os parâmetros no banco (tabela AppSettings). Sem registro salvo, vale o padrão.
public class AdsSettingsService(AgentOfficeDb db)
{
    private const string Key = "ads.playbook";
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AdsPlaybookSettings> GetAsync(CancellationToken ct)
    {
        var row = await db.AppSettings.FindAsync([Key], ct);
        return row is null
            ? new AdsPlaybookSettings()
            : JsonSerializer.Deserialize<AdsPlaybookSettings>(row.ValueJson, Json) ?? new AdsPlaybookSettings();
    }

    public async Task SaveAsync(AdsPlaybookSettings settings, CancellationToken ct)
    {
        var row = await db.AppSettings.FindAsync([Key], ct);
        if (row is null)
        {
            row = new AppSetting { Key = Key };
            db.AppSettings.Add(row);
        }

        row.ValueJson = JsonSerializer.Serialize(settings, Json);
        row.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
}
