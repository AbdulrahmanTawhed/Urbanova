using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbanova.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ConcurrencyGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AnalysisRuns_ProjectId_InputHash",
                table: "AnalysisRuns");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ProjectId_Version",
                table: "Reports",
                columns: new[] { "ProjectId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRuns_ProjectId_InputHash",
                table: "AnalysisRuns",
                columns: new[] { "ProjectId", "InputHash" },
                unique: true,
                filter: "[Status] = 2");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Reports_ProjectId_Version",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_AnalysisRuns_ProjectId_InputHash",
                table: "AnalysisRuns");

            migrationBuilder.CreateIndex(
                name: "IX_AnalysisRuns_ProjectId_InputHash",
                table: "AnalysisRuns",
                columns: new[] { "ProjectId", "InputHash" });
        }
    }
}
