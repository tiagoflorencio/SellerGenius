using AgentOffice.Api.Data;

namespace AgentOffice.Api.Ads;

// Chaves de uma publicação usadas para achar os anúncios de campanha dela.
public record ListingKeys(string ItemId, string? FamilyId, string? CatalogProductId);

// Liga os anúncios de campanha (ad groups) às publicações do painel.
// O Product Ads agrupa de três formas e cada uma guarda um id diferente em "ExternalId":
//   ITEM    -> id da própria publicação (MLB...)
//   FAMILY  -> family_id (todas as variantes do mesmo produto, modelo User Products)
//   CATALOG -> id do produto de catálogo (catalog_product_id)
// Atenção: FAMILY e CATALOG cobrem VÁRIAS publicações, e as métricas são do grupo todo, não de cada uma.
public static class AdLinker
{
    public static bool Matches(AdGroupMetric group, ListingKeys keys) => group.AdGroupType switch
    {
        "ITEM" => group.ExternalId == keys.ItemId,
        "FAMILY" => keys.FamilyId is not null && group.ExternalId == keys.FamilyId,
        "CATALOG" => keys.CatalogProductId is not null && group.ExternalId == keys.CatalogProductId,
        _ => false,
    };
}
