using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgentOffice.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddListingFamilyId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FamilyId",
                table: "ListingDetails",
                type: "TEXT",
                nullable: true);

            // Preenche a coluna a partir do JSON já salvo, para a ligação com Ads funcionar sem nova sincronização.
            migrationBuilder.Sql(
                "UPDATE ListingDetails SET FamilyId = CAST(json_extract(RawItemJson, '$.family_id') AS TEXT) " +
                "WHERE json_extract(RawItemJson, '$.family_id') IS NOT NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FamilyId",
                table: "ListingDetails");
        }
    }
}
