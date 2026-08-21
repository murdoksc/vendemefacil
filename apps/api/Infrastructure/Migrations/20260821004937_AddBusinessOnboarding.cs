using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendemeFacil.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinessOnboarding : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BusinessType",
                table: "Tenants",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "OnboardingDismissed",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "PrintingConfigured",
                table: "Tenants",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsDemoData",
                table: "Products",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BusinessType",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "OnboardingDismissed",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "PrintingConfigured",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "IsDemoData",
                table: "Products");
        }
    }
}
