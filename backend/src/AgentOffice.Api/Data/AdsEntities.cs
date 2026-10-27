namespace AgentOffice.Api.Data;

// Uma execução do agente de Ads. Guarda exatamente o que foi enviado e recebido: é a trilha de auditoria e a base
// para comparar, no futuro, o que foi recomendado com o que aconteceu depois.
public class AdAnalysisRun
{
    public int Id { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public string Model { get; set; } = "";
    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }

    // Resumo (hash) do manual usado, para saber qual versão do manual gerou cada análise.
    public string ManualHash { get; set; } = "";

    // Parâmetros vigentes na hora da análise.
    public string SettingsJson { get; set; } = "";

    // Dados enviados à IA e resposta bruta recebida.
    public string InputJson { get; set; } = "";
    public string OutputJson { get; set; } = "";

    public string Summary { get; set; } = "";

    // Qualidade dos dados segundo a IA (JSON: estado, faltantes, alertas) e itens descartados pelas travas do código.
    public string DataQualityJson { get; set; } = "";
    public string DiscardedJson { get; set; } = "[]";

    public int? PromptTokens { get; set; }
    public int? CompletionTokens { get; set; }
}

// Recomendação do agente. Nasce "Pending"; só você decide. Nada é aplicado no Mercado Livre por este sistema.
public class AdRecommendation
{
    public int Id { get; set; }
    public int RunId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    // Pending, Approved ou Rejected.
    public string Status { get; set; } = "Pending";

    // campaign, ad_group ou account.
    public string Scope { get; set; } = "";
    public long? CampaignId { get; set; }
    public long? AdGroupId { get; set; }

    public string Action { get; set; } = "";
    public string Where { get; set; } = "";
    public string Title { get; set; } = "";

    // Prioridade de 0 a 100, calculada pelo código (não pela IA) com os pesos dos parâmetros.
    public double Priority { get; set; }
    public string Confidence { get; set; } = "";
    public decimal? ImpactBrl { get; set; }
    public bool ImpactUnknown { get; set; }

    // Códigos dos achados por regras que motivaram a recomendação, separados por vírgula.
    public string FindingCodes { get; set; } = "";

    // Recomendação completa (texto da IA + notas de prioridade) em JSON.
    public string ContentJson { get; set; } = "";

    public DateTimeOffset? DecidedAt { get; set; }

    // Motivo da sua decisão. Recusas explicadas valem ouro para o aprendizado do agente.
    public string? DecisionNote { get; set; }

    // Dia em que VOCÊ aplicou a mudança no Mercado Livre. Marca o início da avaliação antes/depois
    // (mudanças de campanha também são detectadas sozinhas na sincronização; as demais dependem desta data).
    public DateOnly? AppliedOn { get; set; }
}

// Retrato diário da configuração de uma campanha. O Mercado Livre não guarda o histórico de orçamento/meta,
// então o guardamos a cada sincronização: é a base para comparar resultados antes e depois de uma mudança.
public class AdCampaignSnapshot
{
    public int Id { get; set; }
    public long CampaignId { get; set; }
    public DateOnly Date { get; set; }
    public string Status { get; set; } = "";
    public string? Strategy { get; set; }
    public decimal Budget { get; set; }
    public double? RoasTarget { get; set; }
    public bool AutomaticBudget { get; set; }
}

// Mudança detectada na configuração de uma campanha (feita por você no Mercado Livre; nosso sistema só lê).
// A data é a da sincronização que percebeu a diferença, então pode ter ocorrido a qualquer momento desde a anterior.
public class AdChange
{
    public int Id { get; set; }
    public long CampaignId { get; set; }
    public DateTimeOffset DetectedAt { get; set; }

    // Budget, RoasTarget, Status, Strategy ou AutomaticBudget.
    public string Field { get; set; } = "";
    public string OldValue { get; set; } = "";
    public string NewValue { get; set; } = "";

    // Motivo anotado por você: alimenta o aprendizado do agente.
    public string? Note { get; set; }

    // Recomendação aprovada que originou esta mudança (ligada automaticamente pelo diário).
    public int? RecommendationId { get; set; }
}

// Anotação livre do diário (promoção, mudança de preço, falta de estoque...): fatores que explicam resultados
// além das mudanças de campanha. Entram na avaliação como possíveis fatores de confusão.
public class AdDiaryNote
{
    public int Id { get; set; }
    public DateOnly Date { get; set; }

    // Null = vale para a conta toda.
    public long? CampaignId { get; set; }
    public string Text { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
}

// Parâmetros e preferências do sistema, em JSON. Ficam no banco (e não no código) para poderem mudar sem novo
// deploy: você edita e, no futuro, o agente poderá propor ajustes baseados no que aprendeu.
public class AppSetting
{
    // Ex.: "ads.playbook".
    public string Key { get; set; } = "";
    public string ValueJson { get; set; } = "";
    public DateTimeOffset UpdatedAt { get; set; }
}

// Campanha de Product Ads (estado atual: orçamento, estratégia, metas). As métricas ficam em AdCampaignDaily.
public class AdCampaign
{
    // Chave primária: id da campanha no Mercado Livre.
    public long CampaignId { get; set; }
    public long AdvertiserId { get; set; }
    public string SiteId { get; set; } = "";
    public string Name { get; set; } = "";

    // active ou paused.
    public string Status { get; set; } = "";

    // PROFITABILITY (rentabilidade), INCREASE (crescimento) ou VISIBILITY (visibilidade).
    public string? Strategy { get; set; }

    // Orçamento médio diário da campanha (o que não é gasto num dia sobra para os próximos do mês).
    public decimal Budget { get; set; }
    public decimal DailyBudget { get; set; }
    public bool AutomaticBudget { get; set; }

    // Meta de retorno: ROAS objetivo (receita por real investido) e o ACOS equivalente (em %).
    public double? RoasTarget { get; set; }
    public double? AcosTarget { get; set; }

    public DateTimeOffset? CampaignCreatedAt { get; set; }
    public DateTimeOffset? CampaignUpdatedAt { get; set; }
    public DateTimeOffset SyncedAt { get; set; }
}

// Métricas de UM dia de UMA campanha. Guardamos só valores somáveis; taxas (CTR, ROAS, ACOS...) são
// calculadas a partir das somas, o que permite agregar qualquer período corretamente.
// Cada sincronização acrescenta/atualiza dias, formando um histórico maior que os 90 dias da API.
public class AdCampaignDaily
{
    public int Id { get; set; }
    public long CampaignId { get; set; }
    public DateOnly Date { get; set; }

    public int Clicks { get; set; }
    public int Prints { get; set; }   // impressões
    public decimal Cost { get; set; } // investimento

    // Receita atribuída à publicidade (vendas diretas + assistidas). Janela de atribuição: 14 dias.
    public decimal Revenue { get; set; }
    public decimal DirectRevenue { get; set; }
    public decimal IndirectRevenue { get; set; }

    public int Units { get; set; }             // unidades vendidas por publicidade
    public int AdvertisingSales { get; set; }  // vendas (pedidos) por publicidade
    public int OrganicUnits { get; set; }      // unidades vendidas dos mesmos anúncios sem publicidade
    public decimal OrganicRevenue { get; set; }

    // Competitividade do dia (frações de 0 a 1). Null se a API não informou.
    public double? ImpressionShare { get; set; }
    public double? LostImpressionShareByBudget { get; set; }
    public double? LostImpressionShareByRank { get; set; }
    public double? AcosBenchmark { get; set; }
}

// Anúncio (ad group) dentro de uma campanha, com as métricas somadas do período da última sincronização.
// ad_group_type: ITEM (id = item_id), FAMILY (id = family_id, User Products) ou CATALOG (id do produto de catálogo).
public class AdGroupMetric
{
    // Chave primária: id do ad group.
    public long AdGroupId { get; set; }
    public long CampaignId { get; set; }
    public string AdGroupType { get; set; } = "";
    public string ExternalId { get; set; } = "";
    public string Title { get; set; } = "";
    public string Status { get; set; } = "";
    public string? Thumbnail { get; set; }
    public string? DomainId { get; set; }
    public bool CatalogListing { get; set; }

    // Etiquetas calculadas pelo Mercado Livre (ex.: TOP-ORDERS).
    public string Tags { get; set; } = "";

    public DateOnly PeriodFrom { get; set; }
    public DateOnly PeriodTo { get; set; }

    public int Clicks { get; set; }
    public int Prints { get; set; }
    public decimal Cost { get; set; }
    public decimal Revenue { get; set; }
    public int Units { get; set; }
    public int AdvertisingSales { get; set; }
    public int OrganicUnits { get; set; }
    public decimal OrganicRevenue { get; set; }

    // Participação nas vendas por publicidade (%). Não é somável, por isso vem direto da API.
    public double? Sov { get; set; }

    public DateTimeOffset SyncedAt { get; set; }
}
