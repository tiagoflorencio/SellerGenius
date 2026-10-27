using System.Text.Json;
using System.Text.Json.Nodes;
using AgentOffice.Api.Data;

namespace AgentOffice.Api.Analysis;

// Resultado: o rascunho de anúncio e o que falta para ele poder ser criado.
public record DraftReport(
    string ItemId,
    bool Ready,                  // true = nenhum campo obrigatório faltando
    List<string> Missing,        // impede a criação
    List<string> Warnings,       // não impede, mas merece atenção
    int MaxTitleLength,
    int MaxDescriptionLength,
    JsonObject Draft);           // corpo no formato do POST /items

// Monta, a partir dos dados salvos de um anúncio, o corpo que o Mercado Livre exige para CRIAR um anúncio
// (POST /items) e confere se todos os campos obrigatórios estão disponíveis.
// Uma variação futura parte deste rascunho e troca só o que muda (título, descrição, preço...).
// ESTA CLASSE NÃO ENVIA NADA: só monta o JSON e lista pendências.
public static class ListingDraftBuilder
{
    public static DraftReport Build(ListingDetail detail, CategoryInfo? category)
    {
        var raw = JsonNode.Parse(detail.RawItemJson)!.AsObject();
        var draft = new JsonObject();
        var missing = new List<string>();
        var warnings = new List<string>();

        // ---- Campos simples obrigatórios ----
        foreach (var field in new[] { "category_id", "price", "currency_id", "available_quantity", "buying_mode", "listing_type_id" })
        {
            if (raw[field] is { } value) draft[field] = value.DeepClone();
            else missing.Add(field);
        }

        // Condição: pode vir no campo "condition" ou no atributo ITEM_CONDITION.
        var hasConditionAttribute = (raw["attributes"] as JsonArray)?.Any(a => (string?)a?["id"] == "ITEM_CONDITION") ?? false;
        if (raw["condition"] is { } condition) draft["condition"] = condition.DeepClone();
        else if (!hasConditionAttribute) missing.Add("condition");

        // ---- Título ou family_name ----
        // No modelo "User Products" o título não é enviado; envia-se o family_name.
        var familyName = (string?)raw["family_name"];
        var title = (string?)raw["title"];
        if (!string.IsNullOrWhiteSpace(familyName)) draft["family_name"] = familyName;
        else if (!string.IsNullOrWhiteSpace(title)) draft["title"] = title;
        else missing.Add("title");

        if (raw["user_product_id"] is not null && string.IsNullOrWhiteSpace(familyName))
            warnings.Add("Anúncio no modelo User Products sem family_name: confirme se o título pode ser enviado.");

        if (raw["catalog_listing"] is JsonValue catalogFlag && catalogFlag.GetValueKind() == JsonValueKind.True)
            warnings.Add("Anúncio de catálogo: título e ficha técnica seguem o produto de catálogo; a variação pode ficar limitada a descrição, preço, fotos e frete.");

        // ---- Opcionais que, se existirem, precisam ser preservados ----
        foreach (var field in new[] { "official_store_id", "video_id", "catalog_product_id" })
        {
            if (raw[field] is { } value && value.GetValueKind() != JsonValueKind.Null) draft[field] = value.DeepClone();
        }
        if (raw["catalog_product_id"] is not null) draft["catalog_listing"] = raw["catalog_listing"]?.DeepClone() ?? false;

        // ---- Fotos: o POST usa a URL ("source") de cada imagem ----
        var pictures = new JsonArray();
        foreach (var picture in raw["pictures"] as JsonArray ?? [])
        {
            var url = (string?)picture?["secure_url"] ?? (string?)picture?["url"];
            if (!string.IsNullOrEmpty(url)) pictures.Add(new JsonObject { ["source"] = url });
        }
        draft["pictures"] = pictures;
        if (pictures.Count == 0) missing.Add("pictures");
        if (category is { MaxPicturesPerItem: > 0 } && pictures.Count > category.MaxPicturesPerItem)
            warnings.Add($"Mais fotos ({pictures.Count}) que o limite da categoria ({category.MaxPicturesPerItem}).");

        // ---- Atributos (ficha técnica) ----
        var attributes = new JsonArray();
        var presentAttributeIds = new HashSet<string>();
        foreach (var attribute in raw["attributes"] as JsonArray ?? [])
        {
            var id = (string?)attribute?["id"];
            var valueId = (string?)attribute?["value_id"];
            var valueName = (string?)attribute?["value_name"];
            if (id is null || (valueId is null && valueName is null)) continue;

            var entry = new JsonObject { ["id"] = id };
            if (valueId is not null) entry["value_id"] = valueId;
            if (valueName is not null) entry["value_name"] = valueName;
            attributes.Add(entry);
            presentAttributeIds.Add(id);
        }
        draft["attributes"] = attributes;

        // ---- Termos de venda (garantia) ----
        var saleTerms = new JsonArray();
        foreach (var term in raw["sale_terms"] as JsonArray ?? [])
        {
            var id = (string?)term?["id"];
            if (id is null) continue;

            var entry = new JsonObject { ["id"] = id };
            if ((string?)term?["value_id"] is { } valueId) entry["value_id"] = valueId;
            if ((string?)term?["value_name"] is { } valueName) entry["value_name"] = valueName;
            saleTerms.Add(entry);
        }
        draft["sale_terms"] = saleTerms;
        if (!saleTerms.Any(t => (string?)t?["id"] == "WARRANTY_TYPE"))
            warnings.Add("Sem garantia (WARRANTY_TYPE) nos termos de venda.");

        // ---- Envio ----
        if (raw["shipping"] is JsonObject shipping)
        {
            var draftShipping = new JsonObject();
            foreach (var field in new[] { "mode", "local_pick_up", "free_shipping" })
            {
                if (shipping[field] is { } value) draftShipping[field] = value.DeepClone();
            }
            if (shipping["dimensions"] is { } dimensions && dimensions.GetValueKind() != JsonValueKind.Null)
                draftShipping["dimensions"] = dimensions.DeepClone();
            draft["shipping"] = draftShipping;

            if (shipping["mode"] is null) missing.Add("shipping.mode");
            if ((string?)shipping["logistic_type"] == "fulfillment")
                warnings.Add("Anúncio Full (fulfillment): uma cópia precisa de estoque próprio no Full.");
        }
        else
        {
            missing.Add("shipping");
        }

        // ---- Variações existentes (cor, tamanho...) ----
        if (raw["variations"] is JsonArray { Count: > 0 } variations)
        {
            var draftVariations = new JsonArray();
            foreach (var variation in variations)
            {
                var entry = new JsonObject();
                foreach (var field in new[] { "price", "available_quantity", "picture_ids", "attribute_combinations", "attributes", "sale_terms" })
                {
                    if (variation?[field] is { } value) entry[field] = value.DeepClone();
                }
                draftVariations.Add(entry);
            }
            draft["variations"] = draftVariations;
        }

        // ---- Descrição (enviada em /items/{id}/description; no POST pode ir junto como plain_text) ----
        if (!string.IsNullOrWhiteSpace(detail.Description))
            draft["description"] = new JsonObject { ["plain_text"] = detail.Description };
        else
            warnings.Add("Anúncio sem descrição.");

        // ---- Regras da categoria: atributos obrigatórios ----
        if (category is null)
        {
            missing.Add("dados_da_categoria (rode a sincronização)");
        }
        else
        {
            CheckRequiredAttributes(category, presentAttributeIds, missing, warnings);
        }

        return new DraftReport(
            detail.ItemId,
            missing.Count == 0,
            missing,
            warnings,
            category?.MaxTitleLength ?? 60,
            category?.MaxDescriptionLength ?? 0,
            draft);
    }

    // Compara os atributos que a categoria exige (tag "required") com os que o anúncio já tem.
    private static void CheckRequiredAttributes(
        CategoryInfo category, HashSet<string> present, List<string> missing, List<string> warnings)
    {
        using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(category.AttributesJson) ? "[]" : category.AttributesJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return;

        foreach (var attribute in document.RootElement.EnumerateArray())
        {
            var id = attribute.TryGetProperty("id", out var idElement) ? idElement.GetString() : null;
            if (id is null || id == "ITEM_CONDITION" || present.Contains(id)) continue;

            // Atributos ocultos ou somente leitura são preenchidos pela própria plataforma.
            if (HasTag(attribute, "hidden") || HasTag(attribute, "read_only")) continue;

            var name = attribute.TryGetProperty("name", out var nameElement) ? nameElement.GetString() : id;

            if (HasTag(attribute, "required")) missing.Add($"atributo:{id} ({name})");
            else if (HasTag(attribute, "conditional_required")) warnings.Add($"Atributo condicional pode ser exigido: {id} ({name}).");
        }
    }

    // A API devolve "tags" ora como objeto {"required": true}, ora como lista ["required"]. Aceitamos os dois.
    private static bool HasTag(JsonElement attribute, string tag)
    {
        if (!attribute.TryGetProperty("tags", out var tags)) return false;

        return tags.ValueKind switch
        {
            JsonValueKind.Object => tags.TryGetProperty(tag, out var flag) && flag.ValueKind == JsonValueKind.True,
            JsonValueKind.Array => tags.EnumerateArray().Any(t => t.ValueKind == JsonValueKind.String && t.GetString() == tag),
            _ => false,
        };
    }
}
