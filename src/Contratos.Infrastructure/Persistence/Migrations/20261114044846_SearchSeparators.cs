using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Contratos.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SearchSeparators : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // O parser do Postgres lê "AP-42" como palavra composta e "-42" como número negativo,
            // e aí "42" não é encontrado. Separadores viram espaço antes de indexar título e valores.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION contracts_search_refresh(p_id uuid) RETURNS void LANGUAGE sql AS $$
                    UPDATE contracts c SET search_vector =
                        setweight(to_tsvector('pt_unaccent', translate(coalesce(c.title, ''), '-/_.:#', '      ')), 'A') ||
                        setweight(to_tsvector('simple', c.number::text || ' ' || lpad(c.number::text, 4, '0')), 'A') ||
                        setweight(to_tsvector('simple', coalesce((
                            SELECT string_agg(immutable_unaccent(s.name) || ' ' || coalesce(s.email, ''), ' ')
                            FROM contract_signers s WHERE s.contract_id = c.id), '')), 'B') ||
                        setweight(to_tsvector('pt_unaccent', translate(coalesce((SELECT string_agg(v.value, ' ') FROM jsonb_each_text(c.variable_values) v), ''), '-/_.:#', '      ')), 'C')
                    WHERE c.id = p_id;
                $$;

                SELECT contracts_search_refresh(id) FROM contracts;
                """);

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION contracts_search_refresh(p_id uuid) RETURNS void LANGUAGE sql AS $$
                    UPDATE contracts c SET search_vector =
                        setweight(to_tsvector('pt_unaccent', coalesce(c.title, '')), 'A') ||
                        setweight(to_tsvector('simple', c.number::text || ' ' || lpad(c.number::text, 4, '0')), 'A') ||
                        setweight(to_tsvector('simple', coalesce((
                            SELECT string_agg(immutable_unaccent(s.name) || ' ' || coalesce(s.email, ''), ' ')
                            FROM contract_signers s WHERE s.contract_id = c.id), '')), 'B') ||
                        setweight(to_tsvector('pt_unaccent', coalesce((SELECT string_agg(v.value, ' ') FROM jsonb_each_text(c.variable_values) v), '')), 'C')
                    WHERE c.id = p_id;
                $$;

                SELECT contracts_search_refresh(id) FROM contracts;
                """);

        }
    }
}
