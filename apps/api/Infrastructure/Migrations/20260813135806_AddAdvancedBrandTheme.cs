using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendemeFacil.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAdvancedBrandTheme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BackgroundColor",
                table: "Tenants",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#f4f5ef");

            migrationBuilder.AddColumn<string>(
                name: "ButtonColor",
                table: "Tenants",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#196651");

            migrationBuilder.AddColumn<int>(
                name: "CornerRadius",
                table: "Tenants",
                type: "int",
                nullable: false,
                defaultValue: 12);

            migrationBuilder.AddColumn<string>(
                name: "SurfaceColor",
                table: "Tenants",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#ffffff");

            migrationBuilder.AddColumn<string>(
                name: "TextColor",
                table: "Tenants",
                type: "nvarchar(7)",
                maxLength: 7,
                nullable: false,
                defaultValue: "#17251f");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BackgroundColor",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "ButtonColor",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "CornerRadius",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SurfaceColor",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TextColor",
                table: "Tenants");
        }
    }
}
