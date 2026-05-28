using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMap360.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddUniqueActiveReportIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "UQ_ContentReports_Reporter_Target_Active",
                table: "ContentReports",
                columns: new[] { "ReporterAccountID", "TargetType", "TargetID" },
                unique: true,
                filter: "\"Status\" IN ('Pending', 'Reviewing')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "UQ_ContentReports_Reporter_Target_Active",
                table: "ContentReports");
        }
    }
}
