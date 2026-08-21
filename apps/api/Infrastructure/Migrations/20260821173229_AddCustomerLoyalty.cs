using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendemeFacil.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerLoyalty : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "LoyaltyActive",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "LoyaltyCashbackPercent",
                table: "Tenants",
                type: "decimal(5,2)",
                precision: 5,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WalletBalance",
                table: "Customers",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LoyaltyActive",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "LoyaltyCashbackPercent",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "WalletBalance",
                table: "Customers");
        }
    }
}
