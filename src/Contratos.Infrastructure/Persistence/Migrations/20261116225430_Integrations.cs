using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Contratos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Integrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "api_idempotency_key",
                table: "contracts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "external_reference",
                table: "contracts",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "api_keys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    prefix = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_api_keys", x => x.id);
                    table.ForeignKey(
                        name: "fk_api_keys_asp_net_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_api_keys_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "webhook_endpoints",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    events = table.Column<List<string>>(type: "text[]", nullable: false),
                    secret_protected = table.Column<string>(type: "text", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_failure_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_streak = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_endpoints", x => x.id);
                    table.ForeignKey(
                        name: "fk_webhook_endpoints_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_webhook_endpoints_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "webhook_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    contract_id = table.Column<Guid>(type: "uuid", nullable: true),
                    payload = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_webhook_events_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "webhook_deliveries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    endpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    next_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_status_code = table.Column<int>(type: "integer", nullable: true),
                    last_error = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    response_body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    delivered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_webhook_deliveries", x => x.id);
                    table.ForeignKey(
                        name: "fk_webhook_deliveries_webhook_endpoints_endpoint_id",
                        column: x => x.endpoint_id,
                        principalTable: "webhook_endpoints",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_webhook_deliveries_webhook_events_event_id",
                        column: x => x.event_id,
                        principalTable: "webhook_events",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_contracts_organization_id_api_idempotency_key",
                table: "contracts",
                columns: new[] { "organization_id", "api_idempotency_key" },
                unique: true,
                filter: "api_idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_organization_id_external_reference",
                table: "contracts",
                columns: new[] { "organization_id", "external_reference" },
                filter: "external_reference IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_created_by",
                table: "api_keys",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_hash",
                table: "api_keys",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_api_keys_organization_id",
                table: "api_keys",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_endpoint_id",
                table: "webhook_deliveries",
                column: "endpoint_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_event_id",
                table: "webhook_deliveries",
                column: "event_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_next_attempt_at",
                table: "webhook_deliveries",
                column: "next_attempt_at",
                filter: "status = 'pending'");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_deliveries_organization_id_created_at",
                table: "webhook_deliveries",
                columns: new[] { "organization_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_created_by",
                table: "webhook_endpoints",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_organization_id",
                table: "webhook_endpoints",
                column: "organization_id");

            migrationBuilder.CreateIndex(
                name: "ix_webhook_events_organization_id_created_at",
                table: "webhook_events",
                columns: new[] { "organization_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "api_keys");

            migrationBuilder.DropTable(
                name: "webhook_deliveries");

            migrationBuilder.DropTable(
                name: "webhook_endpoints");

            migrationBuilder.DropTable(
                name: "webhook_events");

            migrationBuilder.DropIndex(
                name: "ix_contracts_organization_id_api_idempotency_key",
                table: "contracts");

            migrationBuilder.DropIndex(
                name: "ix_contracts_organization_id_external_reference",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "api_idempotency_key",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "external_reference",
                table: "contracts");
        }
    }
}
