using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbanova.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecommendationPolygonIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "PolygonIndex",
                table: "Recommendations",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PolygonIndex",
                table: "Recommendations");
        }
    }
}
