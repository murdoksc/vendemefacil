using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VendemeFacil.Api.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkLayawayPaymentsToCashSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CashSessionId",
                table: "LayawayPayments",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_CashSessions_TenantId_Id",
                table: "CashSessions",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_LayawayPayments_TenantId_CashSessionId",
                table: "LayawayPayments",
                columns: new[] { "TenantId", "CashSessionId" });

            migrationBuilder.AddForeignKey(
                name: "FK_LayawayPayments_CashSessions_TenantId_CashSessionId",
                table: "LayawayPayments",
                columns: new[] { "TenantId", "CashSessionId" },
                principalTable: "CashSessions",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LayawayPayments_CashSessions_TenantId_CashSessionId",
                table: "LayawayPayments");

            migrationBuilder.DropIndex(
                name: "IX_LayawayPayments_TenantId_CashSessionId",
                table: "LayawayPayments");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CashSessions_TenantId_Id",
                table: "CashSessions");

            migrationBuilder.DropColumn(
                name: "CashSessionId",
                table: "LayawayPayments");
        }
    }
}
