using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Lifts seeded system profiles bound to <c>claude-cli</c> from a frozen model generation onto the
    /// always-latest family alias, so system crews follow new Claude releases instead of staying on the
    /// generation that happened to be current when they were seeded. Changing the code constants alone
    /// would not reach existing installations: system profiles live as rows, not as constants.
    /// <para>
    /// Scope is deliberately narrow. Only <c>IsSystem = true</c> rows on the <c>claude-cli</c> provider
    /// are touched — user profiles stay as configured, and a system profile pointing at Claude through
    /// OpenRouter must keep its OpenRouter id, which has no alias form. The mapping is family
    /// preserving and uses explicit id lists rather than a prefix match, because lifting a Sonnet or
    /// Haiku row onto Opus would multiply the cost of every run that uses it. The dotted bare forms are
    /// included because Step30 seeds one reviewer as <c>claude-haiku-4.5</c>, and the
    /// vendor-prefixed forms because older seeds wrote <c>anthropic/</c>-prefixed ids even on the
    /// CLI provider. The <c>"Provider" = 'claude-cli'</c> clause is what keeps those prefixed ids
    /// safe to lift here while leaving genuine OpenRouter rows alone.
    /// </para>
    /// </summary>
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260804120000_Step45CliSystemProfileModelAliases")]
    public partial class Step45CliSystemProfileModelAliases : Migration
    {
        private static readonly string[] Tables =
            ["ReviewerProfiles", "ExecutorProfiles", "AdvisorProfiles"];

        private static readonly (string Alias, string LegacyIds)[] Families =
        [
            ("claude-opus-latest",
             "'claude-opus-4-8','claude-opus-4-7','claude-opus-4-5',"
             + "'claude-opus-4.8','claude-opus-4.7','claude-opus-4.5',"
             + "'anthropic/claude-opus-4-8','anthropic/claude-opus-4-7','anthropic/claude-opus-4-5',"
             + "'anthropic/claude-opus-4.8','anthropic/claude-opus-4.7','anthropic/claude-opus-4.5'"),
            ("claude-sonnet-latest",
             "'claude-sonnet-5','claude-sonnet-4-6','claude-sonnet-4-5',"
             + "'claude-sonnet-5.0','claude-sonnet-4.6','claude-sonnet-4.5',"
             + "'anthropic/claude-sonnet-5','anthropic/claude-sonnet-4-6','anthropic/claude-sonnet-4-5',"
             + "'anthropic/claude-sonnet-4.6','anthropic/claude-sonnet-4.5'"),
            ("claude-haiku-latest",
             "'claude-haiku-4-5','claude-haiku-4-0','claude-haiku-4.5','claude-haiku-4.0',"
             + "'anthropic/claude-haiku-4-5','anthropic/claude-haiku-4-0',"
             + "'anthropic/claude-haiku-4.5','anthropic/claude-haiku-4.0'"),
        ];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in Tables)
            {
                foreach (var (alias, legacyIds) in Families)
                {
                    migrationBuilder.Sql($"""
UPDATE "{table}" SET "Model" = '{alias}'
WHERE "IsSystem" = true AND "Provider" = 'claude-cli' AND "Model" IN ({legacyIds});
""");
                }
            }
        }

        /// <summary>
        /// Intentional no-op: which row carried which frozen generation before the update is not
        /// recoverable, and re-freezing every row onto one id would be worse than leaving the aliases.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
