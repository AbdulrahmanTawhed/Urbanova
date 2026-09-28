using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Urbanova.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrdV02_EvidenceAndQuantity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "RecommendationRuleId",
                table: "Recommendations",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "QuantitySource",
                table: "CostEstimates",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "UserProvided");

            migrationBuilder.CreateTable(
                name: "RecommendationRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ScientificReferencesJson = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ImpactBasis = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    EngineName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    EngineVersion = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecommendationRules", x => x.Id);
                });

            migrationBuilder.InsertData(
                table: "RecommendationRules",
                columns: new[] { "Id", "Code", "CreatedAt", "CreatedBy", "Description", "EngineName", "EngineVersion", "ImpactBasis", "IsActive", "ScientificReferencesJson", "Title", "UpdatedAt" },
                values: new object[,]
                {
                    { new Guid("b1e0f101-0000-4000-8000-000000000001"), "HEAT-VEG-001", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Sizes a vegetation-cover increase from HeatV01 cooling sensitivities so the modeled area value falls strictly below the Moderate band edge.", "HeatV01", "0.1.0-mvp", "HeatV01 per-percent vegetation cooling sensitivity (configured).", true, "[\"MVP placeholder reference — pending engineering validation\"]", "Increase vegetation cover to exit the heat problem band", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) },
                    { new Guid("b1e0f101-0000-4000-8000-000000000002"), "HEAT-PREVENT-001", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, "Preventive guidance for areas in the moderate band: hold cover, monitor.", "HeatV01", "0.1.0-mvp", "Preventive — no change modeled.", true, "[\"MVP placeholder reference — pending engineering validation\"]", "Maintain vegetation cover in moderately warm areas", new DateTimeOffset(new DateTime(2026, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)) }
                });

            migrationBuilder.CreateIndex(
                name: "IX_Recommendations_RecommendationRuleId",
                table: "Recommendations",
                column: "RecommendationRuleId");

            migrationBuilder.CreateIndex(
                name: "IX_RecommendationRules_Code",
                table: "RecommendationRules",
                column: "Code",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Recommendations_RecommendationRules_RecommendationRuleId",
                table: "Recommendations",
                column: "RecommendationRuleId",
                principalTable: "RecommendationRules",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Recommendations_RecommendationRules_RecommendationRuleId",
                table: "Recommendations");

            migrationBuilder.DropTable(
                name: "RecommendationRules");

            migrationBuilder.DropIndex(
                name: "IX_Recommendations_RecommendationRuleId",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "RecommendationRuleId",
                table: "Recommendations");

            migrationBuilder.DropColumn(
                name: "QuantitySource",
                table: "CostEstimates");
        }
    }
}
