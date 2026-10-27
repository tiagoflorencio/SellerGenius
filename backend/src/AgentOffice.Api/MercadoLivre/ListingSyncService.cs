using System.Text.Json;
using AgentOffice.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace AgentOffice.Api.MercadoLivre;

// Sincronização: LÊ os anúncios no Mercado Livre e GRAVA uma cópia no banco local.
// Nada é escrito no Mercado Livre; só o nosso SQLite é alterado.
// "Scoped": uma instância por requisição HTTP, como exige o DbContext.
public class ListingSyncService(MercadoLivreClient ml, AgentOfficeDb db, ILogger<ListingSyncService> logger)
{
    // Categorias mudam pouco; só buscamos de novo depois deste prazo.
    private static readonly TimeSpan CategoryCacheDuration = TimeSpan.FromDays(7);

    public async Task<int> SyncAsync(CancellationToken ct)
    {
        // 1) Quem sou eu? Precisamos do id do vendedor para listar os anúncios.
        var me = await ml.GetMeAsync(ct);

        // 2) Ids de todos os anúncios e depois o JSON COMPLETO de cada um.
        var ids = await ml.GetItemIdsAsync(me.Id, ct);
        var rawItems = await ml.GetItemsAsync(ids, ct);

        // Vendas dos últimos 30 dias (via pedidos). Se a permissão de pedidos faltar, seguimos sem esse dado.
        Dictionary<string, int>? sales = null;
        try
        {
            sales = await ml.GetSalesLast30DaysAsync(me.Id, ct);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning("Não foi possível obter pedidos: {Status}", ex.StatusCode);
        }

        // 3) Carrega o que já temos salvo, indexado por id, para decidir entre inserir e atualizar.
        var existing = await db.Listings.ToDictionaryAsync(l => l.ItemId, ct);
        var existingDetails = await db.ListingDetails.ToDictionaryAsync(d => d.ItemId, ct);
        var now = DateTimeOffset.UtcNow;
        var synced = 0;

        foreach (var raw in rawItems)
        {
            // Converte o JSON bruto nos campos que usamos nas métricas.
            var item = raw.Deserialize<MlItem>();
            if (item is null) continue;

            // Visitas: se falhar para um anúncio, seguimos com null em vez de abortar tudo.
            int? visits = null;
            try
            {
                visits = await ml.GetVisitsLast30DaysAsync(item.Id, ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Não foi possível obter visitas de {ItemId}: {Status}", item.Id, ex.StatusCode);
            }

            // Descrição: chamada separada (a API não a inclui no JSON do anúncio).
            string? description = null;
            try
            {
                description = await ml.GetItemDescriptionAsync(item.Id, ct);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Não foi possível obter a descrição de {ItemId}: {Status}", item.Id, ex.StatusCode);
            }

            // Insere se for novo, senão atualiza o registro existente.
            if (!existing.TryGetValue(item.Id, out var listing))
            {
                listing = new Listing { ItemId = item.Id };
                db.Listings.Add(listing);
            }

            listing.Title = item.Title;
            listing.Price = item.Price;
            listing.CurrencyId = item.CurrencyId ?? "";
            listing.Status = item.Status ?? "";
            listing.AvailableQuantity = item.AvailableQuantity;
            listing.SoldQuantity = item.SoldQuantity;
            listing.Visits30d = visits;
            listing.Sales30d = sales is null ? null : sales.GetValueOrDefault(item.Id);
            listing.PicturesCount = item.Pictures?.Count ?? 0;
            listing.FreeShipping = item.Shipping?.FreeShipping ?? false;
            listing.CategoryId = item.CategoryId;
            listing.ListingTypeId = item.ListingTypeId;
            listing.Permalink = item.Permalink;
            listing.Health = item.Health;
            listing.SyncedAt = now;

            // Dados completos do anúncio (base para criar variações).
            if (!existingDetails.TryGetValue(item.Id, out var detail))
            {
                detail = new ListingDetail { ItemId = item.Id };
                db.ListingDetails.Add(detail);
            }

            detail.Description = description;
            detail.RawItemJson = raw.GetRawText();
            detail.Condition = GetString(raw, "condition");
            detail.BuyingMode = GetString(raw, "buying_mode");
            detail.FamilyName = GetString(raw, "family_name");
            detail.FamilyId = GetId(raw, "family_id");
            detail.UserProductId = GetString(raw, "user_product_id");
            detail.CatalogProductId = GetString(raw, "catalog_product_id");
            detail.DomainId = GetString(raw, "domain_id");
            detail.SyncedAt = now;

            // Histórico: acrescenta uma "foto" de hoje deste anúncio.
            db.ListingSnapshots.Add(new ListingSnapshot
            {
                ItemId = item.Id,
                CapturedAt = now,
                Price = item.Price,
                Status = listing.Status,
                AvailableQuantity = item.AvailableQuantity,
                SoldQuantity = item.SoldQuantity,
                Visits30d = visits,
                Sales30d = listing.Sales30d,
            });

            synced++;
        }

        // Regras e atributos obrigatórios de cada categoria usada pelos anúncios.
        await SyncCategoriesAsync(
            rawItems.Select(r => GetString(r, "category_id")).OfType<string>().Distinct().ToList(), now, ct);

        // Grava tudo no banco de uma vez (uma transação).
        await db.SaveChangesAsync(ct);
        return synced;
    }

    // Busca (ou atualiza, se o cache venceu) os dados de cada categoria. Falha numa categoria não derruba o resto.
    private async Task SyncCategoriesAsync(List<string> categoryIds, DateTimeOffset now, CancellationToken ct)
    {
        var cached = await db.Categories.ToDictionaryAsync(c => c.CategoryId, ct);

        foreach (var id in categoryIds)
        {
            if (cached.TryGetValue(id, out var current) && now - current.FetchedAt < CategoryCacheDuration)
                continue;

            try
            {
                var category = await ml.GetCategoryAsync(id, ct);
                var attributes = await ml.GetCategoryAttributesAsync(id, ct);
                var saleTerms = await ml.GetCategorySaleTermsAsync(id, ct);

                if (current is null)
                {
                    current = new CategoryInfo { CategoryId = id };
                    db.Categories.Add(current);
                }

                var settings = category.TryGetProperty("settings", out var s) ? s : default;

                current.Name = GetString(category, "name") ?? id;
                current.PathFromRoot = category.TryGetProperty("path_from_root", out var path) && path.ValueKind == JsonValueKind.Array
                    ? string.Join(" > ", path.EnumerateArray().Select(p => GetString(p, "name")))
                    : "";
                current.MaxTitleLength = GetInt(settings, "max_title_length") ?? 60;
                current.MaxDescriptionLength = GetInt(settings, "max_description_length") ?? 0;
                current.MaxPicturesPerItem = GetInt(settings, "max_pictures_per_item") ?? 0;
                current.SettingsJson = settings.ValueKind == JsonValueKind.Object ? settings.GetRawText() : "{}";
                current.AttributesJson = attributes.GetRawText();
                current.SaleTermsJson = saleTerms.GetRawText();
                current.FetchedAt = now;
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning("Não foi possível obter a categoria {CategoryId}: {Status}", id, ex.StatusCode);
            }
        }
    }

    // Helpers para ler campos de um JSON sem lançar exceção quando o campo não existe ou é null.
    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    // Ids como o family_id vêm como número no JSON (grande demais para perder precisão); guardamos como texto.
    private static string? GetId(JsonElement element, string name)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out var value)) return null;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static int? GetInt(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : null;
}
