using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMap360.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddSourceUrlUniqueIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UX_ViecLam_SourceURL",
                table: "ViecLam",
                column: "SourceURL",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_PhongTro_SourceURL",
                table: "PhongTro",
                column: "SourceURL",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_ViecLam_SourceURL",
                table: "ViecLam");

            migrationBuilder.DropIndex(
                name: "UX_PhongTro_SourceURL",
                table: "PhongTro");
        }
    }
}
