using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Adds cached-input / reasoning token-breakdown columns surfaced by the OpenAI-conform cli-proxy
    /// (prompt_tokens_details.cached_tokens / completion_tokens_details.reasoning_tokens):
    /// per-actor on <c>IterationActorCosts</c> and run-level aggregates on <c>Runs</c>.
    /// Additive and nullable — existing rows stay NULL ("not reported").
    /// </summary>
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260623120000_Step43TokenBreakdown")]
    public partial class Step43TokenBreakdown : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""IterationActorCosts""
    ADD COLUMN IF NOT EXISTS ""CachedInputTokens"" integer NULL,
    ADD COLUMN IF NOT EXISTS ""ReasoningTokens""   integer NULL;
");

            migrationBuilder.Sql(@"
ALTER TABLE ""Runs""
    ADD COLUMN IF NOT EXISTS ""CachedInputTokens"" integer NULL,
    ADD COLUMN IF NOT EXISTS ""ReasoningTokens""   integer NULL;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""IterationActorCosts""
    DROP COLUMN IF EXISTS ""CachedInputTokens"",
    DROP COLUMN IF EXISTS ""ReasoningTokens"";
");

            migrationBuilder.Sql(@"
ALTER TABLE ""Runs""
    DROP COLUMN IF EXISTS ""CachedInputTokens"",
    DROP COLUMN IF EXISTS ""ReasoningTokens"";
");
        }
    }
}
