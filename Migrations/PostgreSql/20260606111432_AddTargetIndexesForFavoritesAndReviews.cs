using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMap360.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddTargetIndexesForFavoritesAndReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_YeuThich_Target",
                table: "YeuThich",
                columns: new[] { "TargetType", "TargetID" });

            migrationBuilder.CreateIndex(
                name: "IX_Review_Target",
                table: "DanhGia",
                columns: new[] { "TargetType", "TargetID" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_YeuThich_Target",
                table: "YeuThich");

            migrationBuilder.DropIndex(
                name: "IX_Review_Target",
                table: "DanhGia");
        }
    }
}
