using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contratos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Embed : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "created_by_external",
                table: "template_versions",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "embed_origins",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_embed_origins", x => x.id);
                    table.ForeignKey(
                        name: "fk_embed_origins_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_embed_origins_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "embed_sessions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organization_id = table.Column<Guid>(type: "uuid", nullable: false),
                    api_key_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permissions = table.Column<List<string>>(type: "text[]", nullable: false),
                    external_user_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    external_user_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    parent_origin = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    launch_token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                    access_token_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    launch_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_embed_sessions", x => x.id);
                    table.ForeignKey(
                        name: "fk_embed_sessions_api_keys_api_key_id",
                        column: x => x.api_key_id,
                        principalTable: "api_keys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_embed_sessions_organizations_organization_id",
                        column: x => x.organization_id,
                        principalTable: "organizations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_embed_origins_created_by",
                table: "embed_origins",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_embed_origins_organization_id_origin",
                table: "embed_origins",
                columns: new[] { "organization_id", "origin" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_embed_sessions_access_token_hash",
                table: "embed_sessions",
                column: "access_token_hash",
                unique: true,
                filter: "access_token_hash IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_embed_sessions_api_key_id",
                table: "embed_sessions",
                column: "api_key_id");

            migrationBuilder.CreateIndex(
                name: "ix_embed_sessions_expires_at",
                table: "embed_sessions",
                column: "expires_at");

            migrationBuilder.CreateIndex(
                name: "ix_embed_sessions_organization_id",
                table: "embed_sessions",
                column: "organization_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "embed_origins");

            migrationBuilder.DropTable(
                name: "embed_sessions");

            migrationBuilder.DropColumn(
                name: "created_by_external",
                table: "template_versions");
        }
    }
}
