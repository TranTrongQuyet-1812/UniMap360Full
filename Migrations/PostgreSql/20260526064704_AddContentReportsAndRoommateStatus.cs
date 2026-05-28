using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace UniMap360.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddContentReportsAndRoommateStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "RoommatePost",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.Sql("UPDATE \"RoommatePost\" SET \"Status\" = 'Hidden' WHERE \"IsActive\" = false;");

            migrationBuilder.CreateTable(
                name: "ContentReports",
                columns: table => new
                {
                    ReportID = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TargetType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    TargetID = table.Column<int>(type: "integer", nullable: false),
                    ReporterAccountID = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    ResolutionAction = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReviewedByAdminAccountID = table.Column<int>(type: "integer", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    TargetTitleSnapshot = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    OwnerAccountIdSnapshot = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContentReports", x => x.ReportID);
                    table.ForeignKey(
                        name: "FK_ContentReports_ReporterAccount",
                        column: x => x.ReporterAccountID,
                        principalTable: "TaiKhoan",
                        principalColumn: "AccountID",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContentReports_ReviewedByAdminAccount",
                        column: x => x.ReviewedByAdminAccountID,
                        principalTable: "TaiKhoan",
                        principalColumn: "AccountID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_ReporterAccountId_CreatedAt",
                table: "ContentReports",
                columns: new[] { "ReporterAccountID", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_ReviewedByAdminAccountID",
                table: "ContentReports",
                column: "ReviewedByAdminAccountID");

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_Status_CreatedAt",
                table: "ContentReports",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_TargetType_TargetId",
                table: "ContentReports",
                columns: new[] { "TargetType", "TargetID" });

            migrationBuilder.CreateIndex(
                name: "IX_ContentReports_TargetType_TargetId_Status",
                table: "ContentReports",
                columns: new[] { "TargetType", "TargetID", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContentReports");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "RoommatePost");
        }
    }
}
