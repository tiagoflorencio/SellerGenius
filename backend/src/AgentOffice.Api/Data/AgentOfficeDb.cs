using Microsoft.EntityFrameworkCore;

namespace AgentOffice.Api.Data;

// Anúncio no estado ATUAL (uma linha por anúncio, sobrescrita a cada sincronização).
public class Listing
{
    // Chave primária: o id do Mercado Livre (ex.: "MLB123456789").
    public string ItemId { get; set; } = "";
    public string Title { get; set; } = "";
    public decimal Price { get; set; }
    public string CurrencyId { get; set; } = "";
    public string Status { get; set; } = "";
    public int AvailableQuantity { get; set; }

    // Vendas acumuladas desde a criação do anúncio (o ML não informa vendas por período neste campo).
    public int SoldQuantity { get; set; }

    // Visitas nos últimos 30 dias. Null se não foi possível consultar.
    public int? Visits30d { get; set; }

    // Unidades vendidas nos últimos 30 dias (soma dos pedidos). Null se a API de pedidos não respondeu.
    public int? Sales30d { get; set; }

    public int PicturesCount { get; set; }
    public bool FreeShipping { get; set; }
    public string? CategoryId { get; set; }
    public string? ListingTypeId { get; set; }
    public string? Permalink { get; set; }

    // Índice de qualidade do anúncio (0 a 1), quando o ML informa.
    public double? Health { get; set; }

    public DateTimeOffset SyncedAt { get; set; }
}

// Dados COMPLETOS de um anúncio, guardados para podermos criar variações dele no futuro.
// A tabela Listings guarda só o resumo usado nas métricas; esta guarda tudo o que o Mercado Livre devolve.
public class ListingDetail
{
    // Chave primária e também ligação com Listings (mesmo id).
    public string ItemId { get; set; } = "";

    // Descrição em texto puro (GET /items/{id}/description). Null se o anúncio não tiver descrição.
    public string? Description { get; set; }

    // JSON original e completo do anúncio (atributos, sale_terms, fotos, envio, variações, tags...).
    public string RawItemJson { get; set; } = "";

    // Campos extraídos do JSON só para consulta rápida.
    public string? Condition { get; set; }
    public string? BuyingMode { get; set; }
    public string? FamilyName { get; set; }

    // Id da família (User Products). É o elo com os anúncios de campanha do tipo FAMILY.
    public string? FamilyId { get; set; }
    public string? UserProductId { get; set; }
    public string? CatalogProductId { get; set; }
    public string? DomainId { get; set; }

    public DateTimeOffset SyncedAt { get; set; }
}

// Regras e atributos de uma categoria do Mercado Livre (limites de título, atributos obrigatórios...).
// Muda pouco, então fica em cache e só é buscada de novo depois de alguns dias.
public class CategoryInfo
{
    public string CategoryId { get; set; } = "";
    public string Name { get; set; } = "";

    // Caminho da categoria, ex.: "Beleza e Cuidado Pessoal > Cuidados com a Barba".
    public string PathFromRoot { get; set; } = "";

    public int MaxTitleLength { get; set; } = 60;
    public int MaxDescriptionLength { get; set; }
    public int MaxPicturesPerItem { get; set; }

    // JSONs originais: settings da categoria, atributos (/categories/{id}/attributes) e sale_terms.
    public string SettingsJson { get; set; } = "";
    public string AttributesJson { get; set; } = "";
    public string SaleTermsJson { get; set; } = "";

    public DateTimeOffset FetchedAt { get; set; }
}

// Foto do anúncio em um dia específico. Cada sincronização acrescenta uma linha,
// formando o histórico que permite ver se uma mudança melhorou ou piorou o desempenho.
public class ListingSnapshot
{
    public int Id { get; set; }
    public string ItemId { get; set; } = "";
    public DateTimeOffset CapturedAt { get; set; }
    public decimal Price { get; set; }
    public string Status { get; set; } = "";
    public int AvailableQuantity { get; set; }
    public int SoldQuantity { get; set; }
    public int? Visits30d { get; set; }
    public int? Sales30d { get; set; }
}

// Sugestão gerada pelo agente de IA para um anúncio. Nasce "Pending" e só vira algo concreto
// (futuramente) quando o dono aprovar. Nada é enviado ao Mercado Livre a partir daqui.
public class ListingSuggestion
{
    public int Id { get; set; }
    public string ItemId { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public string Model { get; set; } = "";

    // Pending (aguardando decisão), Approved ou Rejected.
    public string Status { get; set; } = "Pending";

    // Alertas por regras que motivaram a sugestão, separados por vírgula.
    public string Flags { get; set; } = "";

    // Resposta da IA (diagnóstico, títulos, ações) em JSON.
    public string ContentJson { get; set; } = "";
}

// DbContext: a "porta de entrada" do EF Core para o banco. Cada DbSet vira uma tabela.
public class AgentOfficeDb(DbContextOptions<AgentOfficeDb> options) : DbContext(options)
{
    public DbSet<Listing> Listings => Set<Listing>();
    public DbSet<ListingSnapshot> ListingSnapshots => Set<ListingSnapshot>();
    public DbSet<ListingSuggestion> ListingSuggestions => Set<ListingSuggestion>();
    public DbSet<ListingDetail> ListingDetails => Set<ListingDetail>();
    public DbSet<CategoryInfo> Categories => Set<CategoryInfo>();
    public DbSet<AdCampaign> AdCampaigns => Set<AdCampaign>();
    public DbSet<AdCampaignDaily> AdCampaignDaily => Set<AdCampaignDaily>();
    public DbSet<AdGroupMetric> AdGroupMetrics => Set<AdGroupMetric>();
    public DbSet<AdCampaignSnapshot> AdCampaignSnapshots => Set<AdCampaignSnapshot>();
    public DbSet<AdChange> AdChanges => Set<AdChange>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<AdAnalysisRun> AdAnalysisRuns => Set<AdAnalysisRun>();
    public DbSet<AdRecommendation> AdRecommendations => Set<AdRecommendation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Define ItemId como chave primária de Listing.
        modelBuilder.Entity<Listing>().HasKey(l => l.ItemId);
        modelBuilder.Entity<ListingDetail>().HasKey(d => d.ItemId);
        modelBuilder.Entity<CategoryInfo>().HasKey(c => c.CategoryId);
        modelBuilder.Entity<AdCampaign>().HasKey(c => c.CampaignId);
        modelBuilder.Entity<AdGroupMetric>().HasKey(g => g.AdGroupId);
        modelBuilder.Entity<AppSetting>().HasKey(s => s.Key);
        modelBuilder.Entity<AdCampaignSnapshot>().HasIndex(s => new { s.CampaignId, s.Date }).IsUnique();
        modelBuilder.Entity<AdChange>().HasIndex(c => new { c.CampaignId, c.DetectedAt });
        modelBuilder.Entity<AdRecommendation>().HasIndex(r => r.RunId);

        // Um registro por campanha e dia; o índice único impede duplicar o mesmo dia em sincronizações repetidas.
        modelBuilder.Entity<AdCampaignDaily>().HasIndex(d => new { d.CampaignId, d.Date }).IsUnique();

        // Índice para buscar rápido o histórico de um anúncio por data.
        modelBuilder.Entity<ListingSnapshot>().HasIndex(s => new { s.ItemId, s.CapturedAt });
    }
}
