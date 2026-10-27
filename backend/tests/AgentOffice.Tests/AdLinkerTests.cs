using AgentOffice.Api.Ads;
using AgentOffice.Api.Data;

namespace AgentOffice.Tests;

public class AdLinkerTests
{
    private static AdGroupMetric Group(string type, string externalId) => new() { AdGroupType = type, ExternalId = externalId };

    private static readonly ListingKeys Listing = new("MLB1", "FAM9", "CAT7");

    [Fact]
    public void Links_item_group_by_item_id()
    {
        Assert.True(AdLinker.Matches(Group("ITEM", "MLB1"), Listing));
        Assert.False(AdLinker.Matches(Group("ITEM", "MLB2"), Listing));
    }

    [Fact]
    public void Links_family_group_by_family_id()
    {
        Assert.True(AdLinker.Matches(Group("FAMILY", "FAM9"), Listing));
        Assert.False(AdLinker.Matches(Group("FAMILY", "OUTRA"), Listing));
    }

    [Fact]
    public void Links_catalog_group_by_catalog_product_id()
    {
        Assert.True(AdLinker.Matches(Group("CATALOG", "CAT7"), Listing));
        Assert.False(AdLinker.Matches(Group("CATALOG", "OUTRO"), Listing));
    }

    [Fact]
    public void Does_not_link_when_listing_has_no_family_or_catalog()
    {
        var plain = new ListingKeys("MLB1", null, null);

        Assert.False(AdLinker.Matches(Group("FAMILY", ""), plain));
        Assert.False(AdLinker.Matches(Group("CATALOG", ""), plain));
    }

    [Fact]
    public void Does_not_mix_id_spaces_between_group_types()
    {
        // Um id de família igual ao id de um item não pode ligar um grupo de outro tipo.
        Assert.False(AdLinker.Matches(Group("FAMILY", "MLB1"), Listing));
        Assert.False(AdLinker.Matches(Group("UNKNOWN", "MLB1"), Listing));
    }
}
