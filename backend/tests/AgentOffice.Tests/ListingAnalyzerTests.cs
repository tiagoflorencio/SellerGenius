using AgentOffice.Api.Analysis;
using AgentOffice.Api.Data;

namespace AgentOffice.Tests;

public class ListingAnalyzerTests
{
    private static Listing Make(string id, int visits, int sales, int pictures = 6,
        string title = "Titulo de teste com mais de quarenta caracteres ok", string status = "active",
        decimal price = 50m, bool freeShipping = false, string category = "MLB1") => new()
    {
        ItemId = id, Title = title, Status = status, Visits30d = visits, Sales30d = sales,
        PicturesCount = pictures, Price = price, FreeShipping = freeShipping, CategoryId = category,
    };

    private static List<string> FlagsOf(List<ListingDiagnosis> diagnoses, string id) =>
        diagnoses.Single(d => d.ItemId == id).Flags;

    [Fact]
    public void Flags_listing_with_traffic_but_no_sales()
    {
        var result = ListingAnalyzer.Analyze([Make("A", 100, 0), Make("B", 100, 10), Make("C", 100, 10)]);

        Assert.Contains(ListingAnalyzer.NoSalesWithTraffic, FlagsOf(result, "A"));
        Assert.DoesNotContain(ListingAnalyzer.NoSalesWithTraffic, FlagsOf(result, "B"));
    }

    [Fact]
    public void Flags_low_conversion_relative_to_portfolio_median()
    {
        // Mediana da carteira = 10%; "A" converte 2% (< metade da mediana).
        var result = ListingAnalyzer.Analyze([Make("A", 100, 2), Make("B", 100, 10), Make("C", 100, 10)]);

        Assert.Contains(ListingAnalyzer.LowConversion, FlagsOf(result, "A"));
    }

    [Fact]
    public void Does_not_judge_conversion_with_too_few_visits()
    {
        var result = ListingAnalyzer.Analyze([Make("A", 5, 0), Make("B", 100, 10), Make("C", 100, 10)]);

        Assert.DoesNotContain(ListingAnalyzer.NoSalesWithTraffic, FlagsOf(result, "A"));
        Assert.DoesNotContain(ListingAnalyzer.LowConversion, FlagsOf(result, "A"));
    }

    [Fact]
    public void Flags_few_pictures_and_short_title()
    {
        var result = ListingAnalyzer.Analyze([Make("A", 100, 10, pictures: 3, title: "Curto")]);

        Assert.Contains(ListingAnalyzer.FewPictures, FlagsOf(result, "A"));
        Assert.Contains(ListingAnalyzer.ShortTitle, FlagsOf(result, "A"));
    }

    [Fact]
    public void Flags_price_above_similar_and_missing_free_shipping()
    {
        var result = ListingAnalyzer.Analyze([
            Make("A", 100, 10, price: 80m, freeShipping: false),
            Make("B", 100, 10, price: 50m, freeShipping: true),
            Make("C", 100, 10, price: 50m, freeShipping: true)]);

        Assert.Contains(ListingAnalyzer.PriceAboveSimilar, FlagsOf(result, "A"));
        Assert.Contains(ListingAnalyzer.NoFreeShipping, FlagsOf(result, "A"));
    }

    [Fact]
    public void Paused_listing_only_gets_paused_flag()
    {
        var result = ListingAnalyzer.Analyze([Make("A", 100, 0, status: "paused", pictures: 1)]);

        Assert.Equal([ListingAnalyzer.Paused], FlagsOf(result, "A"));
    }
}
