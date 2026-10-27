using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOffice.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAdsDiary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "AppliedOn",
                table: "AdRecommendations",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "RecommendationId",
                table: "AdChanges",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AdDiaryNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<long>(type: "INTEGER", nullable: true),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdDiaryNotes", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdDiaryNotes");

            migrationBuilder.DropColumn(
                name: "AppliedOn",
                table: "AdRecommendations");

            migrationBuilder.DropColumn(
                name: "RecommendationId",
                table: "AdChanges");
        }
    }
}
