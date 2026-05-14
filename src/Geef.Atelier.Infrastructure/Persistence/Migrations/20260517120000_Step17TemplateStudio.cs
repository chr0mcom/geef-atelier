using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260517120000_Step17TemplateStudio")]
    public partial class Step17TemplateStudio : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""TemplateStudioAnalyses"" (
                    ""Id""                       uuid           PRIMARY KEY DEFAULT gen_random_uuid(),
                    ""TaskDescription""          text           NOT NULL,
                    ""AnalysisResultJson""       jsonb          NOT NULL DEFAULT '{}'::jsonb,
                    ""InputTokens""              integer        NOT NULL,
                    ""OutputTokens""             integer        NOT NULL,
                    ""CostEur""                  numeric(10,6)  NULL,
                    ""MaterializedTemplateName"" text           NULL,
                    ""CreatedAt""                timestamptz    NOT NULL DEFAULT now()
                );

                CREATE INDEX IF NOT EXISTS ""IX_TemplateStudioAnalyses_CreatedAt""
                    ON ""TemplateStudioAnalyses""(""CreatedAt"" DESC);
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                DROP INDEX IF EXISTS ""IX_TemplateStudioAnalyses_CreatedAt"";
                DROP TABLE IF EXISTS ""TemplateStudioAnalyses"";
            ");
        }
    }
}
