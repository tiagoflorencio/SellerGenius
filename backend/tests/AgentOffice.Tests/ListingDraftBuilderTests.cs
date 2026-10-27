using AgentOffice.Api.Analysis;
using AgentOffice.Api.Data;

namespace AgentOffice.Tests;

public class ListingDraftBuilderTests
{
    private const string ItemJson = """
        {
          "id": "MLB1", "title": "Kit Barba Raiz", "category_id": "MLB278197", "price": 73.03,
          "currency_id": "BRL", "available_quantity": 9, "buying_mode": "buy_it_now",
          "listing_type_id": "gold_special", "condition": "new",
          "pictures": [ { "id": "1", "secure_url": "https://img/1.jpg" } ],
          "attributes": [
            { "id": "BRAND", "value_id": "1", "value_name": "Barba Raiz" },
            { "id": "GTIN", "value_id": null, "value_name": null }
          ],
          "sale_terms": [ { "id": "WARRANTY_TYPE", "value_id": "2230280", "value_name": "Garantia do vendedor" } ],
          "shipping": { "mode": "me2", "free_shipping": false, "local_pick_up": false, "logistic_type": "xd_drop_off" }
        }
        """;

    // tags como objeto (formato real da API) e como lista (formato visto na documentação)
    private const string CategoryAttributes = """
        [
          { "id": "BRAND", "name": "Marca", "tags": { "required": true } },
          { "id": "MODEL", "name": "Modelo", "tags": ["required"] },
          { "id": "GTIN", "name": "GTIN", "tags": { "conditional_required": true } },
          { "id": "ITEM_CONDITION", "name": "Condição", "tags": { "required": true, "hidden": true } }
        ]
        """;

    private static ListingDetail Detail(string json = ItemJson, string? description = "Descrição") =>
        new() { ItemId = "MLB1", RawItemJson = json, Description = description };

    private static CategoryInfo Category(string attributes = CategoryAttributes) =>
        new() { CategoryId = "MLB278197", AttributesJson = attributes, MaxTitleLength = 60, MaxDescriptionLength = 50000, MaxPicturesPerItem = 12 };

    [Fact]
    public void Reports_missing_required_category_attribute()
    {
        var report = ListingDraftBuilder.Build(Detail(), Category());

        Assert.False(report.Ready);
        Assert.Contains(report.Missing, m => m.StartsWith("atributo:MODEL"));
        Assert.DoesNotContain(report.Missing, m => m.Contains("BRAND"));
        Assert.DoesNotContain(report.Missing, m => m.Contains("ITEM_CONDITION"));
        Assert.Contains(report.Warnings, w => w.Contains("GTIN"));
    }

    [Fact]
    public void Is_ready_when_all_required_fields_are_present()
    {
        var onlyBrand = """[ { "id": "BRAND", "name": "Marca", "tags": { "required": true } } ]""";

        var report = ListingDraftBuilder.Build(Detail(), Category(onlyBrand));

        Assert.True(report.Ready);
        Assert.Empty(report.Missing);
    }

    [Fact]
    public void Draft_has_post_items_shape_without_read_only_fields()
    {
        var draft = ListingDraftBuilder.Build(Detail(), Category()).Draft;

        Assert.Equal("Kit Barba Raiz", (string?)draft["title"]);
        Assert.Equal("https://img/1.jpg", (string?)draft["pictures"]![0]!["source"]);
        Assert.Equal("Descrição", (string?)draft["description"]!["plain_text"]);
        Assert.Null(draft["id"]);
        // atributos sem valor (GTIN) não entram no corpo
        Assert.Single(draft["attributes"]!.AsArray());
    }

    [Fact]
    public void Uses_family_name_instead_of_title_for_user_products()
    {
        var json = ItemJson.Replace("\"id\": \"MLB1\",", "\"id\": \"MLB1\", \"family_name\": \"Familia X\", \"user_product_id\": \"MLBU1\",");

        var draft = ListingDraftBuilder.Build(Detail(json), Category()).Draft;

        Assert.Equal("Familia X", (string?)draft["family_name"]);
        Assert.Null(draft["title"]);
    }

    [Fact]
    public void Reports_missing_core_fields_and_pictures()
    {
        var json = """{ "id": "MLB1", "title": "X", "pictures": [], "shipping": { "mode": "me2" } }""";

        var report = ListingDraftBuilder.Build(Detail(json), null);

        Assert.Contains("category_id", report.Missing);
        Assert.Contains("price", report.Missing);
        Assert.Contains("pictures", report.Missing);
        Assert.Contains(report.Missing, m => m.StartsWith("dados_da_categoria"));
    }
}
