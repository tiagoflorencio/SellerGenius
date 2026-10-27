using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOffice.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddProductAds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdCampaignDaily",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CampaignId = table.Column<long>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Clicks = table.Column<int>(type: "INTEGER", nullable: false),
                    Prints = table.Column<int>(type: "INTEGER", nullable: false),
                    Cost = table.Column<decimal>(type: "TEXT", nullable: false),
                    Revenue = table.Column<decimal>(type: "TEXT", nullable: false),
                    DirectRevenue = table.Column<decimal>(type: "TEXT", nullable: false),
                    IndirectRevenue = table.Column<decimal>(type: "TEXT", nullable: false),
                    Units = table.Column<int>(type: "INTEGER", nullable: false),
                    AdvertisingSales = table.Column<int>(type: "INTEGER", nullable: false),
                    OrganicUnits = table.Column<int>(type: "INTEGER", nullable: false),
                    OrganicRevenue = table.Column<decimal>(type: "TEXT", nullable: false),
                    ImpressionShare = table.Column<double>(type: "REAL", nullable: true),
                    LostImpressionShareByBudget = table.Column<double>(type: "REAL", nullable: true),
                    LostImpressionShareByRank = table.Column<double>(type: "REAL", nullable: true),
                    AcosBenchmark = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdCampaignDaily", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdCampaigns",
                columns: table => new
                {
                    CampaignId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    AdvertiserId = table.Column<long>(type: "INTEGER", nullable: false),
                    SiteId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Strategy = table.Column<string>(type: "TEXT", nullable: true),
                    Budget = table.Column<decimal>(type: "TEXT", nullable: false),
                    DailyBudget = table.Column<decimal>(type: "TEXT", nullable: false),
                    AutomaticBudget = table.Column<bool>(type: "INTEGER", nullable: false),
                    RoasTarget = table.Column<double>(type: "REAL", nullable: true),
                    AcosTarget = table.Column<double>(type: "REAL", nullable: true),
                    CampaignCreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    CampaignUpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    SyncedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdCampaigns", x => x.CampaignId);
                });

            migrationBuilder.CreateTable(
                name: "AdGroupMetrics",
                columns: table => new
                {
                    AdGroupId = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CampaignId = table.Column<long>(type: "INTEGER", nullable: false),
                    AdGroupType = table.Column<string>(type: "TEXT", nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Thumbnail = table.Column<string>(type: "TEXT", nullable: true),
                    DomainId = table.Column<string>(type: "TEXT", nullable: true),
                    CatalogListing = table.Column<bool>(type: "INTEGER", nullable: false),
                    Tags = table.Column<string>(type: "TEXT", nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Clicks = table.Column<int>(type: "INTEGER", nullable: false),
                    Prints = table.Column<int>(type: "INTEGER", nullable: false),
                    Cost = table.Column<decimal>(type: "TEXT", nullable: false),
                    Revenue = table.Column<decimal>(type: "TEXT", nullable: false),
                    Units = table.Column<int>(type: "INTEGER", nullable: false),
                    AdvertisingSales = table.Column<int>(type: "INTEGER", nullable: false),
                    OrganicUnits = table.Column<int>(type: "INTEGER", nullable: false),
                    OrganicRevenue = table.Column<decimal>(type: "TEXT", nullable: false),
                    Sov = table.Column<double>(type: "REAL", nullable: true),
                    SyncedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdGroupMetrics", x => x.AdGroupId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdCampaignDaily_CampaignId_Date",
                table: "AdCampaignDaily",
                columns: new[] { "CampaignId", "Date" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdCampaignDaily");

            migrationBuilder.DropTable(
                name: "AdCampaigns");

            migrationBuilder.DropTable(
                name: "AdGroupMetrics");
        }
    }
}
