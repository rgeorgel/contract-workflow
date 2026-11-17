using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contratos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PublicApiIntegrationSetup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "created_by_api_key_id",
                table: "webhook_endpoints",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "created_by_api_key_id",
                table: "embed_origins",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_webhook_endpoints_created_by_api_key_id",
                table: "webhook_endpoints",
                column: "created_by_api_key_id");

            migrationBuilder.CreateIndex(
                name: "ix_embed_origins_created_by_api_key_id",
                table: "embed_origins",
                column: "created_by_api_key_id");

            migrationBuilder.CreateIndex(
                name: "ix_contracts_organization_id_updated_at",
                table: "contracts",
                columns: new[] { "organization_id", "updated_at" });

            migrationBuilder.AddForeignKey(
                name: "fk_embed_origins_api_keys_created_by_api_key_id",
                table: "embed_origins",
                column: "created_by_api_key_id",
                principalTable: "api_keys",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "fk_webhook_endpoints_api_keys_created_by_api_key_id",
                table: "webhook_endpoints",
                column: "created_by_api_key_id",
                principalTable: "api_keys",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_embed_origins_api_keys_created_by_api_key_id",
                table: "embed_origins");

            migrationBuilder.DropForeignKey(
                name: "fk_webhook_endpoints_api_keys_created_by_api_key_id",
                table: "webhook_endpoints");

            migrationBuilder.DropIndex(
                name: "ix_webhook_endpoints_created_by_api_key_id",
                table: "webhook_endpoints");

            migrationBuilder.DropIndex(
                name: "ix_embed_origins_created_by_api_key_id",
                table: "embed_origins");

            migrationBuilder.DropIndex(
                name: "ix_contracts_organization_id_updated_at",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "created_by_api_key_id",
                table: "webhook_endpoints");

            migrationBuilder.DropColumn(
                name: "created_by_api_key_id",
                table: "embed_origins");
        }
    }
}
