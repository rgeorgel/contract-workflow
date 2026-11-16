using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contratos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PayAsYouGoPlan : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "plan_id",
                table: "organizations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "avulso",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "gratis");

            // O Grátis (3 contratos por mês) virou o Avulso (só créditos). Quem estava nele ganha os mesmos
            // 3 contratos de boas-vindas, por 30 dias, para não ficar sem enviar de uma hora para outra.
            migrationBuilder.Sql("""
                INSERT INTO credit_grants (id, organization_id, quantity, remaining, created_at, expires_at, source, reference, note)
                SELECT gen_random_uuid(), id, 3, 3, now(), now() + interval '30 days', 'welcome', 'welcome', 'Boas-vindas'
                FROM organizations WHERE plan_id = 'gratis'
                ON CONFLICT DO NOTHING;
                UPDATE organizations SET plan_id = 'avulso' WHERE plan_id = 'gratis';
                UPDATE usage_records SET plan_id = 'avulso' WHERE plan_id = 'gratis';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("UPDATE organizations SET plan_id = 'gratis' WHERE plan_id = 'avulso';");
            migrationBuilder.AlterColumn<string>(
                name: "plan_id",
                table: "organizations",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "gratis",
                oldClrType: typeof(string),
                oldType: "character varying(30)",
                oldMaxLength: 30,
                oldDefaultValue: "avulso");
        }
    }
}
