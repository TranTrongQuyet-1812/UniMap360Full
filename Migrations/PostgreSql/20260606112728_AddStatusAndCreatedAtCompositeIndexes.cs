using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMap360.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddStatusAndCreatedAtCompositeIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_ViecLam_JobStatus_CreatedAt",
                table: "ViecLam",
                columns: new[] { "JobStatus", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoommatePost_Status_CreatedAt",
                table: "RoommatePost",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PhongTro_RoomStatus_CreatedAt",
                table: "PhongTro",
                columns: new[] { "RoomStatus", "CreatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ViecLam_JobStatus_CreatedAt",
                table: "ViecLam");

            migrationBuilder.DropIndex(
                name: "IX_RoommatePost_Status_CreatedAt",
                table: "RoommatePost");

            migrationBuilder.DropIndex(
                name: "IX_PhongTro_RoomStatus_CreatedAt",
                table: "PhongTro");
        }
    }
}
