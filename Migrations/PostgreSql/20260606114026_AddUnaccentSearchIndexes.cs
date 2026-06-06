using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMap360.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddUnaccentSearchIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create the immutable wrapper function
            migrationBuilder.Sql(@"
                CREATE OR REPLACE FUNCTION public.f_unaccent(text)
                RETURNS text AS $$
                SELECT public.unaccent('public.unaccent', $1)
                $$ LANGUAGE sql IMMUTABLE PARALLEL SAFE;
            ");

            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_PhongTro_Title_Unaccent_Trgm\" ON \"PhongTro\" USING gin (public.f_unaccent(\"Title\") gin_trgm_ops);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_ViecLam_JobTitle_Unaccent_Trgm\" ON \"ViecLam\" USING gin (public.f_unaccent(\"JobTitle\") gin_trgm_ops);");
            migrationBuilder.Sql("CREATE INDEX IF NOT EXISTS \"IX_Location_AddressText_Unaccent_Trgm\" ON \"DiaDiem\" USING gin (public.f_unaccent(\"AddressText\") gin_trgm_ops);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_PhongTro_Title_Unaccent_Trgm\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_ViecLam_JobTitle_Unaccent_Trgm\";");
            migrationBuilder.Sql("DROP INDEX IF EXISTS \"IX_Location_AddressText_Unaccent_Trgm\";");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS public.f_unaccent(text);");
        }
    }
}
