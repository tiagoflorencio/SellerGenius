using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOffice.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdsAgent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdAnalysisRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    PeriodFrom = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodTo = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    ManualHash = table.Column<string>(type: "TEXT", nullable: false),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: false),
                    InputJson = table.Column<string>(type: "TEXT", nullable: false),
                    OutputJson = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    DataQualityJson = table.Column<string>(type: "TEXT", nullable: false),
                    DiscardedJson = table.Column<string>(type: "TEXT", nullable: false),
                    PromptTokens = table.Column<int>(type: "INTEGER", nullable: true),
                    CompletionTokens = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdAnalysisRuns", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AdRecommendations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RunId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Scope = table.Column<string>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<long>(type: "INTEGER", nullable: true),
                    AdGroupId = table.Column<long>(type: "INTEGER", nullable: true),
                    Action = table.Column<string>(type: "TEXT", nullable: false),
                    Where = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Priority = table.Column<double>(type: "REAL", nullable: false),
                    Confidence = table.Column<string>(type: "TEXT", nullable: false),
                    ImpactBrl = table.Column<decimal>(type: "TEXT", nullable: true),
                    ImpactUnknown = table.Column<bool>(type: "INTEGER", nullable: false),
                    FindingCodes = table.Column<string>(type: "TEXT", nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: false),
                    DecidedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    DecisionNote = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdRecommendations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdRecommendations_RunId",
                table: "AdRecommendations",
                column: "RunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdAnalysisRuns");

            migrationBuilder.DropTable(
                name: "AdRecommendations");
        }
    }
}
