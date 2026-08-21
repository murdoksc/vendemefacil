using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendemeFacil.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CurrentPeriodEndsAtUtc",
                table: "Tenants",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PlanCode",
                table: "Tenants",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "negocio");

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionNotes",
                table: "Tenants",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SubscriptionStatus",
                table: "Tenants",
                type: "nvarchar(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Trial");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TrialEndsAtUtc",
                table: "Tenants",
                type: "datetimeoffset",
                nullable: false,
                defaultValueSql: "DATEADD(day, 30, SYSDATETIMEOFFSET())");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CurrentPeriodEndsAtUtc",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "PlanCode",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SubscriptionNotes",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "SubscriptionStatus",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TrialEndsAtUtc",
                table: "Tenants");
        }
    }
}
