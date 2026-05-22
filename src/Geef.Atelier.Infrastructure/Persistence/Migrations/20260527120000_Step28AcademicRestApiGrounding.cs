using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260527120000_Step28AcademicRestApiGrounding")]
    public partial class Step28AcademicRestApiGrounding : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
INSERT INTO ""GroundingProviderProfiles"" (
    ""Name"", ""DisplayName"", ""Description"", ""ProviderType"", ""ProviderSettings"",
    ""MaxQueriesPerRun"", ""IsSystem""
) VALUES (
    'academic-default',
    'Academic Search (Semantic Scholar + AI-filtered)',
    'Semantic Scholar search for scientific papers, filtered by an LLM refiner to retain only the most relevant results.',
    'academic-search',
    '{
        ""source"": ""semantic-scholar"",
        ""maxPapers"": ""5"",
        ""refinementProvider"": ""openrouter"",
        ""refinementModel"": ""google/gemini-2.0-flash-lite"",
        ""refinementMaxTokens"": ""2048"",
        ""refinementMode"": ""0""
    }'::jsonb,
    1,
    true
) ON CONFLICT (""Name"") DO NOTHING;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM ""GroundingProviderProfiles"" WHERE ""Name"" = 'academic-default';");
        }
    }
}
