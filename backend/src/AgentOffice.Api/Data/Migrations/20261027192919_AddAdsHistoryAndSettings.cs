using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOffice.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdsHistoryAndSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdCampaignSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CampaignId = table.Column<long>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Strategy = table.Column<string>(type: "TEXT", nullable: true),
                    Budget = table.Column<decimal>(type: "TEXT", nullable: false),
                    RoasTarget = table.Column<double>(type: "REAL", nullable: true),
                    AutomaticBudget = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdCampaignSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdChanges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CampaignId = table.Column<long>(type: "INTEGER", nullable: false),
                    DetectedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Field = table.Column<string>(type: "TEXT", nullable: false),
                    OldValue = table.Column<string>(type: "TEXT", nullable: false),
                    NewValue = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdChanges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    ValueJson = table.Column<string>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdCampaignSnapshots_CampaignId_Date",
                table: "AdCampaignSnapshots",
                columns: new[] { "CampaignId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdChanges_CampaignId_DetectedAt",
                table: "AdChanges",
                columns: new[] { "CampaignId", "DetectedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdCampaignSnapshots");

            migrationBuilder.DropTable(
                name: "AdChanges");

            migrationBuilder.DropTable(
                name: "AppSettings");
        }
    }
}
