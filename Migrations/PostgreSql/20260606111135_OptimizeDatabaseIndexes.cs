using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMap360.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class OptimizeDatabaseIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_YeuThich_StudentID",
                table: "YeuThich");

            migrationBuilder.DropIndex(
                name: "IX_Location_Coordinates",
                table: "DiaDiem");

            migrationBuilder.CreateIndex(
                name: "UX_YeuThich_Student_Target",
                table: "YeuThich",
                columns: new[] { "StudentID", "TargetType", "TargetID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TepDaPhuongTien_Target",
                table: "TepDaPhuongTien",
                columns: new[] { "TargetType", "TargetID" });

            migrationBuilder.CreateIndex(
                name: "IX_Location_Coordinates",
                table: "DiaDiem",
                column: "Coordinates")
                .Annotation("Npgsql:IndexMethod", "gist");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UX_YeuThich_Student_Target",
                table: "YeuThich");

            migrationBuilder.DropIndex(
                name: "IX_TepDaPhuongTien_Target",
                table: "TepDaPhuongTien");

            migrationBuilder.DropIndex(
                name: "IX_Location_Coordinates",
                table: "DiaDiem");

            migrationBuilder.CreateIndex(
                name: "IX_YeuThich_StudentID",
                table: "YeuThich",
                column: "StudentID");

            migrationBuilder.CreateIndex(
                name: "IX_Location_Coordinates",
                table: "DiaDiem",
                column: "Coordinates");
        }
    }
}
