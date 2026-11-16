using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contratos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class StripeSubscriptions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "stripe_customer_id",
                table: "organizations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "stripe_subscription_id",
                table: "organizations",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "subscription_cancel_at_period_end",
                table: "organizations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "subscription_interval",
                table: "organizations",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "subscription_status",
                table: "organizations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_organizations_stripe_customer_id",
                table: "organizations",
                column: "stripe_customer_id",
                unique: true,
                filter: "stripe_customer_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_organizations_stripe_customer_id",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "stripe_customer_id",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "stripe_subscription_id",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "subscription_cancel_at_period_end",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "subscription_interval",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "subscription_status",
                table: "organizations");
        }
    }
}
