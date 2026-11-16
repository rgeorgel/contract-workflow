using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Contratos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BillingPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "plan_id",
                table: "organizations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "gratis");

            // Contas que já existiam antes dos planos continuam sem limite prático; contas novas
            // entram no plano de Billing:DefaultPlan.
            migrationBuilder.Sql("UPDATE organizations SET plan_id = 'empresa';");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "plan_period_end",
                table: "organizations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "plan_period_start",
                table: "organizations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "credit_grants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    remaining = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_credit_grants", x => x.id);
                    table.CheckConstraint("ck_credit_grants_quantity", "quantity > 0");
                    table.CheckConstraint("ck_credit_grants_remaining", "remaining >= 0 AND remaining <= quantity");
                    table.ForeignKey(
                        name: "fk_credit_grants_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "usage_records",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: false),
                    plan_id = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    credit_grant_id = table.Column<Guid>(type: "uuid", nullable: true),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    refunded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_usage_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_usage_records_credit_grants_credit_grant_id",
                        column: x => x.credit_grant_id,
                        principalTable: "credit_grants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_usage_records_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_credit_grants_organization_id_expires_at",
                table: "credit_grants",
                columns: new[] { "organization_id", "expires_at" });

            migrationBuilder.CreateIndex(
                name: "ix_credit_grants_organization_id_reference",
                table: "credit_grants",
                columns: new[] { "organization_id", "reference" },
                unique: true,
                filter: "reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_usage_records_contract_id",
                table: "usage_records",
                column: "contract_id");

            migrationBuilder.CreateIndex(
                name: "ix_usage_records_credit_grant_id",
                table: "usage_records",
                column: "credit_grant_id");

            migrationBuilder.CreateIndex(
                name: "ix_usage_records_organization_id_consumed_at",
                table: "usage_records",
                columns: new[] { "organization_id", "consumed_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "usage_records");

            migrationBuilder.DropTable(
                name: "credit_grants");

            migrationBuilder.DropColumn(
                name: "plan_id",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "plan_period_end",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "plan_period_start",
                table: "organizations");
        }
    }
}
