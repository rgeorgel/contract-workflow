using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contratos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionCancelAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "subscription_cancel_at_period_end",
                table: "organizations");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "subscription_cancel_at",
                table: "organizations",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "subscription_cancel_at",
                table: "organizations");

            migrationBuilder.AddColumn<bool>(
                name: "subscription_cancel_at_period_end",
                table: "organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }
    }
}
