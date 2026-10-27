using AgentOffice.Api.Ads;
using AgentOffice.Api.Data;

namespace AgentOffice.Tests;

public class AdMetricsCalculatorTests
{
    private static AdCampaignDaily Day(int clicks, int prints, decimal cost, decimal revenue, int units, decimal organicRevenue = 0) =>
        new() { Clicks = clicks, Prints = prints, Cost = cost, Revenue = revenue, Units = units, OrganicRevenue = organicRevenue };

    [Fact]
    public void Calculates_rates_from_summed_values_not_from_averaged_rates()
    {
        // Dia 1: 100 cliques de 10.000 impressões; dia 2: 300 cliques de 10.000 impressões.
        var metrics = AdMetricsCalculator.From([Day(100, 10_000, 100m, 1000m, 10), Day(300, 10_000, 300m, 1000m, 20)]);

        Assert.Equal(400, metrics.Clicks);
        Assert.Equal(2.0, metrics.Ctr);          // 400 / 20.000
        Assert.Equal(1.00m, metrics.Cpc);        // 400 / 400
        Assert.Equal(20.0, metrics.Acos);        // 400 / 2000
        Assert.Equal(5.0, metrics.Roas);         // 2000 / 400
        Assert.Equal(7.5, metrics.Cvr);          // 30 / 400
    }

    [Fact]
    public void Tacos_uses_total_revenue_including_organic()
    {
        var metrics = AdMetricsCalculator.From([Day(10, 100, 50m, 400m, 4, organicRevenue: 100m)]);

        Assert.Equal(10.0, metrics.Tacos); // 50 / (400 + 100)
    }

    [Fact]
    public void Rates_are_null_when_denominator_is_zero()
    {
        var metrics = AdMetricsCalculator.From([Day(0, 0, 0m, 0m, 0)]);

        Assert.Null(metrics.Ctr);
        Assert.Null(metrics.Cpc);
        Assert.Null(metrics.Acos);
        Assert.Null(metrics.Roas);
        Assert.Null(metrics.Cvr);
        Assert.Null(metrics.Tacos);
    }

    [Fact]
    public void Sum_of_campaign_metrics_matches_metrics_of_all_days_together()
    {
        var a = AdMetricsCalculator.From([Day(100, 5000, 80m, 800m, 8)]);
        var b = AdMetricsCalculator.From([Day(50, 2000, 20m, 100m, 1)]);

        var total = AdMetricsCalculator.Sum([a, b]);

        Assert.Equal(150, total.Clicks);
        Assert.Equal(100m, total.Cost);
        Assert.Equal(9.0, total.Roas);
    }

    [Fact]
    public void Window_ends_yesterday_and_respects_limits()
    {
        var yesterday = DateOnly.FromDateTime(DateTime.Now).AddDays(-1);

        var (from, to) = AdsSyncService.Window(30);
        Assert.Equal(yesterday, to);
        Assert.Equal(30, to.DayNumber - from.DayNumber + 1);

        var (fromMax, _) = AdsSyncService.Window(500);
        Assert.Equal(AdsSyncService.MaxDays, to.DayNumber - fromMax.DayNumber + 1);
    }
}
