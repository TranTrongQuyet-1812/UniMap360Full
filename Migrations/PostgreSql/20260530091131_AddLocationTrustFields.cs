using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMap360.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddLocationTrustFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "GeocodedLatitude",
                table: "DiaDiem",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "GeocodedLongitude",
                table: "DiaDiem",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationConfidence",
                table: "DiaDiem",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LocationDistanceMeters",
                table: "DiaDiem",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LocationSuspicious",
                table: "DiaDiem",
                type: "boolean",
                nullable: true,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GeocodedLatitude",
                table: "DiaDiem");

            migrationBuilder.DropColumn(
                name: "GeocodedLongitude",
                table: "DiaDiem");

            migrationBuilder.DropColumn(
                name: "LocationConfidence",
                table: "DiaDiem");

            migrationBuilder.DropColumn(
                name: "LocationDistanceMeters",
                table: "DiaDiem");

            migrationBuilder.DropColumn(
                name: "LocationSuspicious",
                table: "DiaDiem");
        }
    }
}
