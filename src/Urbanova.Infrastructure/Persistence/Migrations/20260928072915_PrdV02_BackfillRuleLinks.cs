using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Urbanova.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PrdV02_BackfillRuleLinks : Migration
    {
        // Pre-v0.2 recommendations were stored without rule links. Backfill from the
        // engine's deterministic mapping: Calculated interventions came from HEAT-VEG-001,
        // Estimated preventive rows from HEAT-PREVENT-001. Anything else keeps NULL and
        // is explicitly documented as pre-evidence (see docs/prd-v0.2.md).
        private const string VegRuleId = "b1e0f101-0000-4000-8000-000000000001";
        private const string PreventRuleId = "b1e0f101-0000-4000-8000-000000000002";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"""
                UPDATE dbo.Recommendations
                SET RecommendationRuleId = '{VegRuleId}'
                WHERE RecommendationRuleId IS NULL AND EvidenceLevel = 1;
                UPDATE dbo.Recommendations
                SET RecommendationRuleId = '{PreventRuleId}'
                WHERE RecommendationRuleId IS NULL AND EvidenceLevel = 2;
                """);
        }

        // Down is best-effort: it cannot distinguish backfilled rows from rows linked
        // after v0.2, so it clears both. Data itself is never deleted.
        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE dbo.Recommendations
                SET RecommendationRuleId = NULL
                WHERE RecommendationRuleId IN (
                    'b1e0f101-0000-4000-8000-000000000001',
                    'b1e0f101-0000-4000-8000-000000000002');
                """);
        }
    }
}
