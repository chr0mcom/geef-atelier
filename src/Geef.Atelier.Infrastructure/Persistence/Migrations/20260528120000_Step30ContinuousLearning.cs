using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260528120000_Step30ContinuousLearning")]
    public partial class Step30ContinuousLearning : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""Runs"" ADD COLUMN IF NOT EXISTS ""Kind"" integer NOT NULL DEFAULT 0;
");

            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""LearningEntries"" (
    ""Id""                  uuid         NOT NULL PRIMARY KEY,
    ""Text""                text         NOT NULL,
    ""SourceRunId""         uuid         REFERENCES ""Runs""(""Id"") ON DELETE SET NULL,
    ""LearningRunId""       uuid         REFERENCES ""Runs""(""Id"") ON DELETE SET NULL,
    ""Domain""              text         NOT NULL,
    ""Status""              integer      NOT NULL DEFAULT 0,
    ""StructuredFactsJson"" text         NOT NULL,
    ""OwnerUsername""       text         NOT NULL,
    ""CreatedAt""           timestamptz  NOT NULL,
    ""ApprovedAt""          timestamptz  NULL,
    ""Embedding""           vector(1536) NULL
);

CREATE INDEX IF NOT EXISTS ""IX_LearningEntries_Domain_Status""
    ON ""LearningEntries""(""Domain"", ""Status"");

CREATE INDEX IF NOT EXISTS ""IX_LearningEntries_Embedding_HNSW""
    ON ""LearningEntries"" USING hnsw(""Embedding"" vector_cosine_ops);
");

            // Seed: 3 strict learning reviewers
            migrationBuilder.Sql(@"
INSERT INTO ""ReviewerProfiles"" (""Name"", ""DisplayName"", ""Description"", ""SystemPrompt"", ""Provider"", ""Model"", ""MaxTokens"", ""IsSystem"")
VALUES
(
    'learning-factual-grounding',
    'Learning: Factual Grounding',
    'Verifies that every claim in the learning candidate is directly supported by the run artefacts. Hallucinated or unsupported statements are Critical.',
    'You are an expert fact-checker reviewing a proposed learning entry that was extracted from an AI-writing run.

Your only job: verify that every claim in the candidate is explicitly supported by the structured run facts provided in the briefing. A claim is supported when it appears verbatim or is a direct logical consequence of the provided facts.

SEVERITY TAXONOMY
• critical   — fabricated claim with no support in the run facts; contradicts provided evidence
• major      — claim significantly overstates or generalises beyond what the facts allow
• minor      — phrasing imprecision that could mislead; easily corrected without restructuring
• info       — observation or wording suggestion with no correctness impact

ANTI-PATTERNS (do NOT flag as critical or major):
• A statement that is technically correct but not phrased optimally → info at most
• A generalisation that is plausible even if not explicitly in the facts → minor at most
• Domain terminology that you personally would phrase differently → info

Respond in the language of the briefing. List each finding as: [SEVERITY] <short title> — <one-sentence explanation>.',
    'openrouter',
    'openai/gpt-4.1',
    2048,
    true
),
(
    'learning-value',
    'Learning: Value Assessment',
    'Rejects trivial, banal, or already-obvious observations. Only generalisable, non-obvious insights pass.',
    'You are a senior editorial judge evaluating whether a proposed learning entry adds real value.

Your only job: assess whether the learning is non-obvious, generalisable beyond the single run that produced it, and worth storing for future retrieval.

SEVERITY TAXONOMY
• critical   — trivially obvious; any practitioner already knows this; adds zero value
• major      — too specific to this one run to generalise; will not help future runs
• minor      — partially useful but the scope is unclear or the phrasing is too vague
• info       — minor suggestion to improve the learning without affecting its validity

ANTI-PATTERNS (do NOT flag as critical or major):
• A learning that is well-known in academic literature but genuinely useful as a reminder → minor at most
• A domain-specific insight that is obvious within that domain but not across domains → info

Respond in the language of the briefing. List each finding as: [SEVERITY] <short title> — <one-sentence explanation>.',
    'openrouter',
    'google/gemini-2.5-pro',
    2048,
    true
),
(
    'learning-generalizability',
    'Learning: Generalizability',
    'Rejects learnings that are artefacts of one specific run rather than repeatable patterns across runs.',
    'You are a meta-learning researcher evaluating whether a proposed learning is a repeatable pattern or a one-off artefact.

Your only job: assess whether the learning describes a pattern that would apply to future runs in the same domain, or whether it is an accident of this particular run.

SEVERITY TAXONOMY
• critical   — clearly a one-run artefact; no reason to expect it to generalise
• major      — pattern is plausible but the evidence from a single run is insufficient to assert it
• minor      — the learning generalises but its scope should be narrowed or caveated
• info       — phrasing suggestion that does not affect generalisability

ANTI-PATTERNS (do NOT flag as critical or major):
• A learning that applies to a narrow sub-domain — narrow scope is fine if it is consistent
• A learning that is probabilistic rather than deterministic → minor at most

Respond in the language of the briefing. List each finding as: [SEVERITY] <short title> — <one-sentence explanation>.',
    'anthropic',
    'claude-opus-4-7',
    2048,
    true
)
ON CONFLICT (""Name"") DO NOTHING;
");

            // Seed: learning-evaluation crew template
            migrationBuilder.Sql(@"
INSERT INTO ""CrewTemplates"" (
    ""Name"", ""DisplayName"", ""Description"",
    ""ExecutorProfileName"", ""ReviewerProfileNames"",
    ""EvaluationStrategy"", ""ConvergenceOverride"",
    ""AdvisorProfileNames"", ""GroundingProviderNames"",
    ""IsSystem"", ""FinalizerProfileNames"", ""RunFinalizersOnMaxAttempts""
) VALUES (
    'learning-evaluation',
    'Learning Evaluation',
    'Strict evaluation crew that gates learning candidates. Uses AbortOnCritical and RunFinalizersOnMaxAttempts so the publisher finalizer always runs.',
    'default-executor',
    '[""learning-factual-grounding"",""learning-value"",""learning-generalizability""]',
    1,
    '{""maxIterations"":2,""abortOnCritical"":true}'::jsonb,
    '[]',
    '[]',
    true,
    '[""learning-publisher""]',
    true
) ON CONFLICT (""Name"") DO NOTHING;
");

            // Seed: two learning finalizer profiles
            migrationBuilder.Sql(@"
INSERT INTO ""FinalizerProfiles"" (""Name"", ""DisplayName"", ""Description"", ""FinalizerType"", ""Settings"", ""IsSystem"")
VALUES
(
    'learning-extractor',
    'Learning Extractor',
    'Extracts structured learnings from a completed run and fires a gated learning-evaluation run (opt-in; not attached to any standard template).',
    4,
    '{}'::jsonb,
    true
),
(
    'learning-publisher',
    'Learning Publisher',
    'Publishes approved learning candidates to the learning store, or marks rejected ones. Runs inside the learning-evaluation crew.',
    5,
    '{}'::jsonb,
    true
)
ON CONFLICT (""Name"") DO NOTHING;
");

            // Seed: learning-retrieval grounding provider profile
            migrationBuilder.Sql(@"
INSERT INTO ""GroundingProviderProfiles"" (
    ""Name"", ""DisplayName"", ""Description"", ""ProviderType"", ""ProviderSettings"",
    ""MaxQueriesPerRun"", ""IsSystem""
) VALUES (
    'learning-retriever-default',
    'Learning Retriever (domain-aware)',
    'Retrieves approved learnings from the learning store using cosine similarity with a domain-boost. Place after curated knowledge providers.',
    'learning-retrieval',
    '{
        ""sameDomainBoost"": ""1.0"",
        ""crossDomainPenalty"": ""0.5"",
        ""maxLearnings"": ""4""
    }'::jsonb,
    1,
    true
) ON CONFLICT (""Name"") DO NOTHING;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
DELETE FROM ""GroundingProviderProfiles"" WHERE ""Name"" = 'learning-retriever-default';
DELETE FROM ""FinalizerProfiles"" WHERE ""Name"" IN ('learning-extractor','learning-publisher');
DELETE FROM ""CrewTemplates"" WHERE ""Name"" = 'learning-evaluation';
DELETE FROM ""ReviewerProfiles"" WHERE ""Name"" IN ('learning-factual-grounding','learning-value','learning-generalizability');
DROP TABLE IF EXISTS ""LearningEntries"";
ALTER TABLE ""Runs"" DROP COLUMN IF EXISTS ""Kind"";
");
        }
    }
}
