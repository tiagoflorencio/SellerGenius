namespace AgentOffice.Api.Data;

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

    // Motivo anotado por você (futuro): alimenta o aprendizado do agente.
    public string? Note { get; set; }
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
