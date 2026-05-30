using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UniMap360.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class AddGeocodeSourceColumn : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GeocodeSource",
                table: "DiaDiem",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GeocodeSource",
                table: "DiaDiem");
        }
    }
}
