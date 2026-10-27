using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOffice.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    CategoryId = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    PathFromRoot = table.Column<string>(type: "TEXT", nullable: false),
                    MaxTitleLength = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxDescriptionLength = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxPicturesPerItem = table.Column<int>(type: "INTEGER", nullable: false),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: false),
                    AttributesJson = table.Column<string>(type: "TEXT", nullable: false),
                    SaleTermsJson = table.Column<string>(type: "TEXT", nullable: false),
                    FetchedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.CategoryId);
                });

            migrationBuilder.CreateTable(
                name: "ListingDetails",
                columns: table => new
                {
                    ItemId = table.Column<string>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    RawItemJson = table.Column<string>(type: "TEXT", nullable: false),
                    Condition = table.Column<string>(type: "TEXT", nullable: true),
                    BuyingMode = table.Column<string>(type: "TEXT", nullable: true),
                    FamilyName = table.Column<string>(type: "TEXT", nullable: true),
                    UserProductId = table.Column<string>(type: "TEXT", nullable: true),
                    CatalogProductId = table.Column<string>(type: "TEXT", nullable: true),
                    DomainId = table.Column<string>(type: "TEXT", nullable: true),
                    SyncedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingDetails", x => x.ItemId);
                });

            migrationBuilder.CreateTable(
                name: "Listings",
                columns: table => new
                {
                    ItemId = table.Column<string>(type: "TEXT", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false),
                    CurrencyId = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    AvailableQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    SoldQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Visits30d = table.Column<int>(type: "INTEGER", nullable: true),
                    Sales30d = table.Column<int>(type: "INTEGER", nullable: true),
                    PicturesCount = table.Column<int>(type: "INTEGER", nullable: false),
                    FreeShipping = table.Column<bool>(type: "INTEGER", nullable: false),
                    CategoryId = table.Column<string>(type: "TEXT", nullable: true),
                    ListingTypeId = table.Column<string>(type: "TEXT", nullable: true),
                    Permalink = table.Column<string>(type: "TEXT", nullable: true),
                    Health = table.Column<double>(type: "REAL", nullable: true),
                    SyncedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Listings", x => x.ItemId);
                });

            migrationBuilder.CreateTable(
                name: "ListingSnapshots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<string>(type: "TEXT", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Price = table.Column<decimal>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    AvailableQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    SoldQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    Visits30d = table.Column<int>(type: "INTEGER", nullable: true),
                    Sales30d = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ListingSuggestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    Model = table.Column<string>(type: "TEXT", nullable: false),
                    Status = table.Column<string>(type: "TEXT", nullable: false),
                    Flags = table.Column<string>(type: "TEXT", nullable: false),
                    ContentJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ListingSuggestions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ListingSnapshots_ItemId_CapturedAt",
                table: "ListingSnapshots",
                columns: new[] { "ItemId", "CapturedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropTable(
                name: "ListingDetails");

            migrationBuilder.DropTable(
                name: "Listings");

            migrationBuilder.DropTable(
                name: "ListingSnapshots");

            migrationBuilder.DropTable(
                name: "ListingSuggestions");
        }
    }
}
