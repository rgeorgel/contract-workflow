using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Contratos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SuperAdmin : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "last_seen_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "suspended_at",
                table: "organizations",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "suspension_reason",
                table: "organizations",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "platform_audit_log",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    actor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    action = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    details = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_platform_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_platform_audit_log_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_platform_audit_log_organization_id_created_at",
                table: "platform_audit_log",
                columns: new[] { "organization_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "platform_audit_log");

            migrationBuilder.DropColumn(
                name: "last_seen_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "suspended_at",
                table: "organizations");

            migrationBuilder.DropColumn(
                name: "suspension_reason",
                table: "organizations");
        }
    }
}
