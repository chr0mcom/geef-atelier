using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260522120000_Step22Finalizers")]
    public partial class Step22Finalizers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── FinalizerProfiles table ─────────────────────────────────────────────
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""FinalizerProfiles"" (
    ""Name""          varchar(200)    NOT NULL PRIMARY KEY,
    ""DisplayName""   varchar(200)    NOT NULL,
    ""Description""   text            NOT NULL DEFAULT '',
    ""FinalizerType"" varchar(32)     NOT NULL,
    ""Settings""      jsonb           NOT NULL DEFAULT '{}',
    ""IsSystem""      boolean         NOT NULL DEFAULT false,
    ""CreatedAt""     timestamptz     NULL,
    ""UpdatedAt""     timestamptz     NULL
);");

            // ── RunArtifacts table ──────────────────────────────────────────────────
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""RunArtifacts"" (
    ""Id""                    uuid            NOT NULL PRIMARY KEY,
    ""RunId""                 uuid            NOT NULL REFERENCES ""Runs""(""Id"") ON DELETE CASCADE,
    ""FinalizerProfileName""  varchar(200)    NOT NULL,
    ""ArtifactType""          varchar(32)     NOT NULL,
    ""Filename""              varchar(500)    NULL,
    ""ContentType""           varchar(200)    NULL,
    ""SizeBytes""             bigint          NULL,
    ""StorageUri""            varchar(2000)   NOT NULL,
    ""StatusMessage""         text            NULL,
    ""CreatedAt""             timestamptz     NOT NULL
);
CREATE INDEX IF NOT EXISTS ""IX_RunArtifacts_RunId"" ON ""RunArtifacts"" (""RunId"");");

            // ── FinalizationActorCosts table ────────────────────────────────────────
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""FinalizationActorCosts"" (
    ""Id""           uuid            NOT NULL PRIMARY KEY,
    ""RunId""        uuid            NOT NULL REFERENCES ""Runs""(""Id"") ON DELETE CASCADE,
    ""ActorName""    varchar(200)    NOT NULL,
    ""ModelName""    varchar(200)    NOT NULL,
    ""InputTokens""  integer         NOT NULL DEFAULT 0,
    ""OutputTokens"" integer         NOT NULL DEFAULT 0,
    ""CostEur""      numeric(10,6)   NULL,
    ""CreatedAt""    timestamptz     NOT NULL
);
CREATE INDEX IF NOT EXISTS ""IX_FinalizationActorCosts_RunId"" ON ""FinalizationActorCosts"" (""RunId"");");

            // ── CrewTemplates: add FinalizerProfileNames + RunFinalizersOnMaxAttempts
            migrationBuilder.Sql(@"
ALTER TABLE ""CrewTemplates""
    ADD COLUMN IF NOT EXISTS ""FinalizerProfileNames""       jsonb   NOT NULL DEFAULT '[]',
    ADD COLUMN IF NOT EXISTS ""RunFinalizersOnMaxAttempts""  boolean NOT NULL DEFAULT false;");

            // ── Runs: add FinalizerCostEur + FinalizerErrorMessage ──────────────────
            migrationBuilder.Sql(@"
ALTER TABLE ""Runs""
    ADD COLUMN IF NOT EXISTS ""FinalizerCostEur""       numeric(10,6) NULL,
    ADD COLUMN IF NOT EXISTS ""FinalizerErrorMessage""  text          NULL;");

            // ── Seed 17 system finalizer profiles (idempotent) ────────────────────
            migrationBuilder.Sql(@"
INSERT INTO ""FinalizerProfiles"" (""Name"", ""DisplayName"", ""Description"", ""FinalizerType"", ""Settings"", ""IsSystem"")
VALUES
    ('export-markdown',        'Export: Markdown',              'Saves the final text as a Markdown (.md) file on the export volume.',                                                                    'FileExport',     '{""Format"":""markdown""}',           true),
    ('export-html',            'Export: HTML',                  'Converts the final Markdown to a self-contained HTML document and saves it on the export volume.',                                       'FileExport',     '{""Format"":""html""}',               true),
    ('export-pdf',             'Export: PDF',                   'Converts the final Markdown to a PDF document (via QuestPDF) and saves it on the export volume.',                                       'FileExport',     '{""Format"":""pdf""}',                true),
    ('export-docx',            'Export: DOCX',                  'Converts the final Markdown to a Word document (.docx) and saves it on the export volume.',                                              'FileExport',     '{""Format"":""docx""}',               true),
    ('export-txt',             'Export: Plain Text',            'Strips Markdown syntax and saves the final text as plain UTF-8 on the export volume.',                                                  'FileExport',     '{""Format"":""txt""}',                true),
    ('export-json',            'Export: JSON',                  'Wraps the final text in a JSON envelope (with run-id, template, and timestamp) and saves it on the export volume.',                    'FileExport',     '{""Format"":""json""}',               true),
    ('add-front-matter',       'Add Front Matter',              'Prepends a YAML front-matter block (title, date, template, run-id) to the final Markdown text.',                                       'MetadataEnrich', '{""EnricherType"":""front-matter""}', true),
    ('add-word-count-footer',  'Add Word-Count Footer',         'Appends a human-readable footer with the exact word count, character count, and estimated reading time.',                               'MetadataEnrich', '{""EnricherType"":""word-count-footer""}', true),
    ('add-reading-level',      'Add Reading Level',             'Computes a Flesch Reading Ease score and appends a brief readability summary to the final text.',                                       'MetadataEnrich', '{""EnricherType"":""reading-level""}', true),
    ('webhook-sink',           'Webhook Sink',                  'POSTs the final text as JSON to a configured webhook URL. Customize URL and optional auth header in settings.',                        'ExternalSink',   '{""SinkKind"":""webhook"",""Url"":"""",""ContentType"":""application/json"",""TimeoutSeconds"":""30""}', true),
    ('email-sink',             'E-Mail Sink',                   'Sends the final text to a configured e-mail address via SMTP. SMTP credentials are resolved from environment variables at runtime.', 'ExternalSink',   '{""SinkKind"":""email"",""ToAddress"":"""",""Subject"":""Geef.Atelier — Run Result"",""AttachAsFile"":""false"",""AttachmentFormat"":""markdown""}', true),
    ('anti-ai-voice',          'Anti-AI-Voice Polish',          'Removes AI-typical phrasing patterns while preserving every factual claim and the author''s intentional style.',                       'Transform',      '{""Provider"":""codex-cli"",""Model"":""gpt-5.5"",""MaxTokens"":""8192"",""SystemPrompt"":""see-code-constant""}', true),
    ('tone-formalization',     'Tone: Formalization',           'Shifts the register of the final text toward formal academic or professional prose without altering its content.',                      'Transform',      '{""Provider"":""codex-cli"",""Model"":""gpt-5.5"",""MaxTokens"":""8192"",""SystemPrompt"":""see-code-constant""}', true),
    ('tone-casual',            'Tone: Casual',                  'Rewrites the final text in a conversational, approachable register suitable for blog posts or social content.',                         'Transform',      '{""Provider"":""codex-cli"",""Model"":""gpt-5.5"",""MaxTokens"":""8192"",""SystemPrompt"":""see-code-constant""}', true),
    ('executive-summary',      'Executive Summary',             'Prepends a 3–5 sentence executive summary in the language of the text, then appends the full original below a horizontal rule.',     'Transform',      '{""Provider"":""codex-cli"",""Model"":""gpt-5.5"",""MaxTokens"":""8192"",""SystemPrompt"":""see-code-constant""}', true),
    ('key-takeaways',          'Key Takeaways',                 'Appends a bulleted list of 5–7 key takeaways distilled from the final text.',                                                           'Transform',      '{""Provider"":""codex-cli"",""Model"":""gpt-5.5"",""MaxTokens"":""8192"",""SystemPrompt"":""see-code-constant""}', true),
    ('glossary',               'Glossary',                      'Identifies up to 10 domain-specific terms in the final text and appends brief, plain-language definitions as a glossary.',            'Transform',      '{""Provider"":""codex-cli"",""Model"":""gpt-5.5"",""MaxTokens"":""8192"",""SystemPrompt"":""see-code-constant""}', true)
ON CONFLICT (""Name"") DO NOTHING;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DELETE FROM ""FinalizerProfiles"" WHERE ""IsSystem"" = true;");
            migrationBuilder.Sql(@"ALTER TABLE ""Runs"" DROP COLUMN IF EXISTS ""FinalizerCostEur"", DROP COLUMN IF EXISTS ""FinalizerErrorMessage"";");
            migrationBuilder.Sql(@"ALTER TABLE ""CrewTemplates"" DROP COLUMN IF EXISTS ""FinalizerProfileNames"", DROP COLUMN IF EXISTS ""RunFinalizersOnMaxAttempts"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""FinalizationActorCosts"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""RunArtifacts"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""FinalizerProfiles"";");
        }
    }
}
