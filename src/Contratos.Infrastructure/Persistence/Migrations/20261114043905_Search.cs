using Microsoft.EntityFrameworkCore.Migrations;
using NpgsqlTypes;

#nullable disable

namespace Contratos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Search : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,");

            migrationBuilder.AddColumn<NpgsqlTsVector>(
                name: "search_vector",
                table: "contracts",
                type: "tsvector",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_contracts_search_vector",
                table: "contracts",
                column: "search_vector")
                .Annotation("Npgsql:IndexMethod", "GIN");

            // Busca em português sem acento: "acao" encontra "Ação".
            migrationBuilder.Sql("""
                CREATE TEXT SEARCH CONFIGURATION pt_unaccent (COPY = portuguese);
                ALTER TEXT SEARCH CONFIGURATION pt_unaccent
                    ALTER MAPPING FOR hword, hword_part, word WITH unaccent, portuguese_stem;

                -- unaccent() não é IMMUTABLE e por isso não entra em índice; este invólucro fixa o dicionário.
                CREATE FUNCTION immutable_unaccent(text) RETURNS text
                    LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
                    AS $$ SELECT public.unaccent('public.unaccent'::regdictionary, $1) $$;

                -- Busca parcial por nome e e-mail do signatário (ILIKE usa estes índices).
                CREATE INDEX ix_contract_signers_name_trgm ON contract_signers USING gin (immutable_unaccent(name) gin_trgm_ops);
                CREATE INDEX ix_contract_signers_email_trgm ON contract_signers USING gin (email gin_trgm_ops);

                -- Recalcula o search_vector de um contrato. A aplicação chama depois de salvar contrato ou signatários.
                CREATE FUNCTION contracts_search_refresh(p_id uuid) RETURNS void LANGUAGE sql AS $$
                    UPDATE contracts c SET search_vector =
                        setweight(to_tsvector('pt_unaccent', coalesce(c.title, '')), 'A') ||
                        setweight(to_tsvector('simple', c.number::text || ' ' || lpad(c.number::text, 4, '0')), 'A') ||
                        setweight(to_tsvector('simple', coalesce((
                            SELECT string_agg(immutable_unaccent(s.name) || ' ' || coalesce(s.email, ''), ' ')
                            FROM contract_signers s WHERE s.contract_id = c.id), '')), 'B') ||
                        setweight(to_tsvector('pt_unaccent', coalesce((
                            SELECT string_agg(v.value, ' ') FROM jsonb_each_text(c.variable_values) v), '')), 'C')
                    WHERE c.id = p_id;
                $$;

                SELECT contracts_search_refresh(id) FROM contracts;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP FUNCTION IF EXISTS contracts_search_refresh(uuid);
                DROP INDEX IF EXISTS ix_contract_signers_email_trgm;
                DROP INDEX IF EXISTS ix_contract_signers_name_trgm;
                DROP FUNCTION IF EXISTS immutable_unaccent(text);
                DROP TEXT SEARCH CONFIGURATION IF EXISTS pt_unaccent;
                """);

            migrationBuilder.DropIndex(
                name: "ix_contracts_search_vector",
                table: "contracts");

            migrationBuilder.DropColumn(
                name: "search_vector",
                table: "contracts");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .OldAnnotation("Npgsql:PostgresExtension:unaccent", ",,");
        }
    }
}
