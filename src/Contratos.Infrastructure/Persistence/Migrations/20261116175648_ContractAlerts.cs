using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contratos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ContractAlerts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "expiry_alerted_at",
                table: "contracts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "stale_alerted_at",
                table: "contracts",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "expiry_alerted_at",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "stale_alerted_at",
                table: "contracts");
        }
    }
}
