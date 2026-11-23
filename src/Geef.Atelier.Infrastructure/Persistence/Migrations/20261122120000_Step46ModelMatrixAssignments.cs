using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Model matrix (D-066). Adds the role/domain columns that let the system assign models from the model matrix
    /// and moves the existing catalogue onto them:
    /// <list type="number">
    ///   <item>Schema: nullable <c>ModelSlot</c>/<c>ModelDomain</c> on executor, reviewer and advisor profiles and a
    ///   nullable crew <c>Domain</c> on templates (additive).</item>
    ///   <item>Crew domains of the known custom templates (the domain belongs to the crew, not to the profile).</item>
    ///   <item>Role classification of the known custom profiles and the Step30 learning reviewers, with the matrix
    ///   primary as display binding. The run-time model is assigned per run at snapshot-build time.</item>
    ///   <item>CLI-route rewrite for every remaining manual row: Claude via <c>claude-cli</c>, OpenAI via
    ///   <c>codex-cli</c>, retired codex generations onto the always-latest alias of their line (Sol/Terra) or
    ///   <c>gpt-astra-latest</c>. Open-weight <c>gpt-oss</c> models are not OpenAI subscription models and stay untouched.</item>
    ///   <item>Managed grounding refiners (custom and system rows), the system Transform finalizers, the legacy
    ///   Template Studio default and the CLI provider model lists.</item>
    /// </list>
    /// Literals are frozen on purpose: a migration must not follow later edits of <c>ModelMatrix</c>. Unknown rows
    /// are never given a guessed role — they only obey the CLI-route rule and stay manual. Run snapshots are never
    /// touched, so resumed runs keep the bindings they started with.
    /// </summary>
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20261122120000_Step46ModelMatrixAssignments")]
    public partial class Step46ModelMatrixAssignments : Migration
    {
        internal const string Opus = "claude-opus-latest";
        internal const string Fable = "claude-fable-latest";
        internal const string Sonnet = "claude-sonnet-latest";
        internal const string Haiku = "claude-haiku-latest";
        internal const string Astra = "gpt-astra-latest";
        internal const string Sol = "gpt-sol-latest";
        internal const string Terra = "gpt-terra-latest";

        /// <summary>
        /// Frozen SQL form of <c>CliRoute.IsRetiredCodexModel</c>, applied to the lower-cased id without <c>openai/</c>
        /// prefix: gpt-4/gpt-5 generations, the o-series, the Luna line and <c>-mini</c> variants.
        /// </summary>
        internal const string RetiredCodexPattern = "^(gpt-[45]|o[0-9])|luna|-mini";

        internal static readonly string[] RewriteTransforms = ["anti-ai-voice", "tone-formalization", "tone-casual"];

        internal static readonly string[] CondenseTransforms = ["executive-summary", "key-takeaways", "glossary"];
        internal const string Neon = "google/gemini-4-neon";

        internal static readonly (string Template, string Domain)[] TemplateDomains =
        [
            ("custom-dotnetpro-article", "Technical"),
            ("custom-infrastructure-architecture-review", "Technical"),
            ("custom-erc-advanced-grant-b2-feasibility-auto-202607281535", "Academic"),
            ("custom-german-cv-employee-to-self-employed-transition-auto-202607241953", "Correspondence"),
            ("custom-german-ihk-speaker-outreach-email-auto-202607201039", "Correspondence"),
            ("custom-german-pet-insurance-goodwill-request-auto-202608200834", "Correspondence"),
            ("custom-german-linkedin-editorial-sharpening-auto-202607281502", "Marketing"),
            ("custom-german-linkedin-thought-leadership-post-auto-202607221113", "Marketing"),
            ("custom-german-pre-litigation-settlement-letter-auto-202606301146", "Legal"),
            ("custom-german-weg-etv-motion-package-auto-202607171251", "Legal"),
            ("custom-matzes-redeschreiber", "Speech"),
            ("learning-evaluation", "General"),
        ];

        internal static readonly (string Table, string Name, string Slot, string Provider, string Model)[] Classification =
        [
            // Executors: prose crews -> Opus; technical/analytical crews (crew domain Technical) -> Fable.
            ("ExecutorProfiles", "custom-afd-sachsen-anhalt-speechwriter", "Executor", "claude-cli", Opus),
            ("ExecutorProfiles", "custom-cv-architect-bechtel-executor", "Executor", "claude-cli", Opus),
            ("ExecutorProfiles", "custom-dms-architecture-reviewer-executor", "Executor", "claude-cli", Fable), // architecture review = Technical crew
            ("ExecutorProfiles", "custom-dotnetpro-article-writer", "Executor", "claude-cli", Fable), // dotnetpro technical article = Technical crew
            ("ExecutorProfiles", "custom-erc-adg-b2-feasibility-executor", "Executor", "claude-cli", Opus),
            ("ExecutorProfiles", "custom-ihk-speaker-outreach-email-executor", "Executor", "claude-cli", Opus),
            ("ExecutorProfiles", "custom-linkedin-editorial-sharpener-executor", "Executor", "claude-cli", Opus),
            ("ExecutorProfiles", "custom-linkedin-ki-origin-story-executor", "Executor", "claude-cli", Opus),
            ("ExecutorProfiles", "custom-luna-kulanz-letter-executor", "Executor", "claude-cli", Opus),
            ("ExecutorProfiles", "custom-sixt-vergleichsschreiben-executor", "Executor", "claude-cli", Opus),
            ("ExecutorProfiles", "custom-weg-etv-antragspaket-executor", "Executor", "claude-cli", Opus),

            // FactReviewer: checks facts, sources, numbers, citations or completeness against the briefing.
            ("ReviewerProfiles", "custom-german-journal-fact-grounding", "FactReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-fact-fidelity-citation-page-discipline-reviewer", "FactReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-fact-fidelity-no-departure-reason-reviewer", "FactReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-fact-fidelity-no-fabrication-reviewer", "FactReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-fact-fidelity-no-fabrication-reviewer-2", "FactReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-fact-fidelity-no-invention-reviewer", "FactReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-fact-fidelity-reviewer", "FactReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-source-number-legal-immutability-reviewer", "FactReviewer", "codex-cli", Astra), // verifies unchanged source numbers, not legal substance
            ("ReviewerProfiles", "custom-sachverhalt-faktentreue-reviewer", "FactReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-official-statistics-fact-reviewer", "FactReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-formale-vollstaendigkeit-struktur-reviewer", "FactReviewer", "codex-cli", Astra), // deviates from the formale-* = Style rule: its prompt checks completeness of mandatory parts, a fact check
            ("ReviewerProfiles", "learning-factual-grounding", "FactReviewer", "codex-cli", Astra), // Step30 learning reviewer: is the learning backed by the run

            // StyleReviewer: language, register, gendering, tone, rhetoric, structure and hook strength.
            ("ReviewerProfiles", "custom-german-journal-voice-discipline", "StyleReviewer", "codex-cli", Sol),
            ("ReviewerProfiles", "custom-german-register-chronology-structure-reviewer", "StyleReviewer", "codex-cli", Sol),
            ("ReviewerProfiles", "custom-german-register-form-and-placeholder-shape-reviewer", "StyleReviewer", "codex-cli", Sol),
            ("ReviewerProfiles", "custom-german-register-nogendern-antiai-reviewer", "StyleReviewer", "codex-cli", Sol),
            ("ReviewerProfiles", "custom-german-register-no-gendern-reviewer", "StyleReviewer", "codex-cli", Sol),
            ("ReviewerProfiles", "custom-german-register-no-gendern-reviewer-2", "StyleReviewer", "codex-cli", Sol),
            ("ReviewerProfiles", "custom-german-register-no-gendern-reviewer-3", "StyleReviewer", "codex-cli", Sol),
            ("ReviewerProfiles", "custom-template-mechanics-structure-reviewer", "StyleReviewer", "codex-cli", Sol),
            ("ReviewerProfiles", "custom-rhetoric-dramaturgy-hook-reviewer", "StyleReviewer", "codex-cli", Sol),
            ("ReviewerProfiles", "custom-brevity-hook-strength-reviewer", "StyleReviewer", "codex-cli", Sol),
            ("ReviewerProfiles", "custom-emotional-authenticity-reviewer", "StyleReviewer", "codex-cli", Sol),
            ("ReviewerProfiles", "custom-deutsches-register-schaerfe-ehrschutz-reviewer", "StyleReviewer", "codex-cli", Sol), // wording sharpness; the legal limit is a register question here
            ("ReviewerProfiles", "custom-rhetoric-timing-language-reviewer", "StyleReviewer", "codex-cli", Sol),
            ("ReviewerProfiles", "learning-value", "StyleReviewer", "codex-cli", Sol), // Step30 learning reviewer: clarity and usefulness of the wording

            // DomainReviewer: subject-matter substance, strategy and opposing positions. Neon (paid) only where the
            // owning crew's domain is Legal or Academic (TemplateDomains above), Astra everywhere else.
            ("ReviewerProfiles", "custom-scientific-excellence-pi-reviewer", "DomainReviewer", "openrouter", Neon), // ERC proposal = Academic
            ("ReviewerProfiles", "custom-methodology-feasibility-reviewer", "DomainReviewer", "openrouter", Neon), // ERC proposal = Academic
            ("ReviewerProfiles", "custom-operational-workplan-budget-reviewer", "DomainReviewer", "openrouter", Neon), // ERC proposal = Academic
            ("ReviewerProfiles", "custom-b1-b2-complementarity-reviewer", "DomainReviewer", "openrouter", Neon), // ERC proposal = Academic
            ("ReviewerProfiles", "custom-disaster-politics-cooperation-substance-reviewer", "DomainReviewer", "openrouter", Neon), // ERC proposal = Academic
            ("ReviewerProfiles", "custom-settlement-strategy-tone-reviewer", "DomainReviewer", "openrouter", Neon), // settlement letter = Legal
            ("ReviewerProfiles", "custom-legal-positioning-reviewer", "DomainReviewer", "openrouter", Neon), // comparison letter = Legal
            ("ReviewerProfiles", "custom-weg-anfechtung-defensibility-reviewer", "DomainReviewer", "openrouter", Neon), // WEG motion package = Legal
            ("ReviewerProfiles", "custom-datenschutz-energierecht-reviewer", "DomainReviewer", "openrouter", Neon), // data-protection/energy law = Legal
            ("ReviewerProfiles", "custom-transition-continuity-positioning-reviewer", "DomainReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-adversarial-hr-ceo-gap-reviewer", "DomainReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-conceptual-framing-positioning-reviewer", "DomainReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-insurance-goodwill-strategy-reviewer", "DomainReviewer", "codex-cli", Astra), // goodwill request = Correspondence, not a legal claim
            ("ReviewerProfiles", "custom-veterinary-evidence-and-superlative-reviewer", "DomainReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-infra-feasibility-reviewer", "DomainReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-architecture-spof-complexity-reviewer", "DomainReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-security-dsgvo-backup-dr-reviewer", "DomainReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-mail-spam-pipeline-reviewer", "DomainReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-cdu-opposition-reviewer", "DomainReviewer", "codex-cli", Astra), // opposing positions in a Speech crew
            ("ReviewerProfiles", "custom-spd-gruene-opposition-reviewer", "DomainReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-linke-opposition-reviewer", "DomainReviewer", "codex-cli", Astra),
            ("ReviewerProfiles", "custom-fdp-rebuttal-adversarial-reviewer", "DomainReviewer", "codex-cli", Astra),

            // Helper: cheap meta check of the Step30 learning loop.
            ("ReviewerProfiles", "learning-generalizability", "Helper", "claude-cli", Haiku),
        ];

        internal static readonly string[] ProfileTables = ["ExecutorProfiles", "ReviewerProfiles", "AdvisorProfiles"];

        protected override void Up(MigrationBuilder migrationBuilder)
        {
            foreach (var table in ProfileTables)
            {
                migrationBuilder.Sql($"""
ALTER TABLE "{table}" ADD COLUMN IF NOT EXISTS "ModelSlot" character varying(40) NULL;
ALTER TABLE "{table}" ADD COLUMN IF NOT EXISTS "ModelDomain" character varying(40) NULL;
""");
            }

            migrationBuilder.Sql("""
ALTER TABLE "CrewTemplates" ADD COLUMN IF NOT EXISTS "Domain" character varying(40) NULL;
""");

            foreach (var (template, domain) in TemplateDomains)
            {
                migrationBuilder.Sql($"""
UPDATE "CrewTemplates" SET "Domain" = '{domain}' WHERE "Name" = '{template}' AND "Domain" IS NULL;
""");
            }

            foreach (var (table, name, slot, provider, model) in Classification)
            {
                migrationBuilder.Sql($"""
UPDATE "{table}" SET "ModelSlot" = '{slot}', "Provider" = '{provider}', "Model" = '{model}'
WHERE "Name" = '{name}' AND "ModelSlot" IS NULL;
""");
            }

            foreach (var table in ProfileTables)
            {
                migrationBuilder.Sql($"""
UPDATE "{table}" SET "Provider" = 'claude-cli',
    "Model" = CASE
        WHEN "Model" ILIKE '%fable%' THEN '{Fable}'
        WHEN "Model" ILIKE '%sonnet%' THEN '{Sonnet}'
        WHEN "Model" ILIKE '%haiku%' THEN '{Haiku}'
        ELSE '{Opus}' END
WHERE "ModelSlot" IS NULL AND btrim("Provider", {TrimChars}) <> 'claude-cli'
  AND (btrim("Model", {TrimChars}) ILIKE 'anthropic/%' OR btrim("Model", {TrimChars}) ILIKE 'claude%');

UPDATE "{table}" SET "Provider" = 'codex-cli', "Model" = {CodexTarget(ModelColumn)}
WHERE "ModelSlot" IS NULL AND btrim("Provider", {TrimChars}) <> 'codex-cli'
  AND ({IsOpenAiModel(ModelColumn)});

UPDATE "{table}" SET "Model" = {CodexTarget(ModelColumn)}
WHERE "ModelSlot" IS NULL AND btrim("Provider", {TrimChars}) = 'codex-cli'
  AND {IsRetired(ModelColumn)} AND "Model" NOT ILIKE '%gpt-oss%';
""");
            }

            migrationBuilder.Sql($$"""
UPDATE "GroundingProviderProfiles"
SET "ProviderSettings" = "ProviderSettings"
    || jsonb_build_object('refinementProvider', 'claude-cli', 'refinementModel', '{{Sonnet}}', 'modelSlot', 'GroundingRefiner')
WHERE coalesce("ProviderSettings"->>'refinementProvider', '') <> ''
  AND coalesce("ProviderSettings"->>'refinementModel', '') <> ''
  AND NOT ("ProviderSettings" ? 'modelSlot');
""");

            foreach (var (type, providerKey, modelKey) in new[] { ("Transform", "Provider", "Model"), ("LearningExtract", "provider", "model") })
            {
                var model = $"(\"Settings\"->>'{modelKey}')";
                migrationBuilder.Sql($$"""
UPDATE "FinalizerProfiles"
SET "Settings" = "Settings" || jsonb_build_object('{{providerKey}}', 'claude-cli', '{{modelKey}}', CASE
        WHEN "Settings"->>'{{modelKey}}' ILIKE '%fable%' THEN '{{Fable}}'
        WHEN "Settings"->>'{{modelKey}}' ILIKE '%sonnet%' THEN '{{Sonnet}}'
        WHEN "Settings"->>'{{modelKey}}' ILIKE '%haiku%' THEN '{{Haiku}}'
        ELSE '{{Opus}}' END)
WHERE "IsSystem" = false AND "FinalizerType" = '{{type}}' AND NOT ("Settings" ? 'ModelSlot')
  AND btrim(coalesce("Settings"->>'{{providerKey}}', ''), {{TrimChars}}) NOT IN ('', 'claude-cli')
  AND (btrim("Settings"->>'{{modelKey}}', {{TrimChars}}) ILIKE 'anthropic/%' OR btrim("Settings"->>'{{modelKey}}', {{TrimChars}}) ILIKE 'claude%');

UPDATE "FinalizerProfiles"
SET "Settings" = "Settings" || jsonb_build_object('{{providerKey}}', 'codex-cli', '{{modelKey}}', {{CodexTarget(model)}})
WHERE "IsSystem" = false AND "FinalizerType" = '{{type}}' AND NOT ("Settings" ? 'ModelSlot')
  AND btrim(coalesce("Settings"->>'{{providerKey}}', ''), {{TrimChars}}) NOT IN ('', 'codex-cli')
  AND ({{IsOpenAiModel(model)}});

UPDATE "FinalizerProfiles"
SET "Settings" = "Settings" || jsonb_build_object('{{modelKey}}', {{CodexTarget(model)}})
WHERE "IsSystem" = false AND "FinalizerType" = '{{type}}' AND NOT ("Settings" ? 'ModelSlot')
  AND btrim("Settings"->>'{{providerKey}}', {{TrimChars}}) = 'codex-cli'
  AND {{IsRetired(model)}} AND {{model}} NOT ILIKE '%gpt-oss%';
""");
            }

            foreach (var (names, slot, model) in new[] { (RewriteTransforms, "TransformRewrite", Sol), (CondenseTransforms, "TransformCondense", Sonnet) })
            {
                var provider = model == Sol ? "codex-cli" : "claude-cli";
                var list = string.Join(",", names.Select(n => $"'{n}'"));
                migrationBuilder.Sql($$"""
UPDATE "FinalizerProfiles"
SET "Settings" = "Settings" || jsonb_build_object('Provider', '{{provider}}', 'Model', '{{model}}', 'ModelSlot', '{{slot}}')
WHERE "IsSystem" = true AND "FinalizerType" = 'Transform' AND "Name" IN ({{list}})
  AND NOT ("Settings" ? 'ModelSlot');
""");
            }

            migrationBuilder.Sql("""
UPDATE "StudioSettings" SET "Provider" = '', "Model" = ''
WHERE "Model" ~* '(gemini-[23]|gpt-[45]|grok|deepseek)' OR "Model" ILIKE 'anthropic/%' OR "Model" ILIKE 'openai/%';
""");

            migrationBuilder.Sql("""
UPDATE "Providers" SET "Settings" = jsonb_set("Settings", '{models}',
    '["claude-opus-latest","claude-sonnet-latest","claude-haiku-latest","claude-fable-latest","claude-opus-5-5","claude-sonnet-5-5","claude-haiku-5-5","claude-fable-5-2"]'::jsonb)
WHERE "Name" = 'claude-cli';
UPDATE "Providers" SET "Settings" = jsonb_set("Settings", '{models}',
    '["gpt-sol-latest","gpt-astra-latest","gpt-terra-latest","gpt-6.2-sol","gpt-6.2-astra","gpt-6.1-terra","gpt-6.1-sol","gpt-6-astra","gpt-6-sol"]'::jsonb)
WHERE "Name" = 'codex-cli';
""");
        }

        private const string ModelColumn = "\"Model\"";

        /// <summary>Whitespace removed by <c>btrim</c>, matching <see cref="string.Trim()"/> for ASCII whitespace (incl. tabs).</summary>
        private const string TrimChars = "E' \\t\\r\\n\\f\\x0B'";

        private static string BareLower(string column) => $"lower(regexp_replace(btrim({column}, {TrimChars}), '^openai/', '', 'i'))";

        private static string IsRetired(string column) => $"({BareLower(column)} ~ '{RetiredCodexPattern}')";

        private static string IsOpenAiModel(string column) =>
            $"(btrim({column}, {TrimChars}) ILIKE 'openai/%' OR btrim({column}, {TrimChars}) ILIKE 'gpt-%' OR btrim({column}, {TrimChars}) ~* '^o[0-9]') AND {column} NOT ILIKE '%gpt-oss%'";

        private static string CodexTarget(string column) =>
            $"CASE WHEN {IsRetired(column)} THEN CASE WHEN btrim({column}, {TrimChars}) ~* '-sol$' THEN '{Sol}' WHEN btrim({column}, {TrimChars}) ~* '-terra$' THEN '{Terra}' ELSE '{Astra}' END ELSE {BareLower(column)} END";

        /// <summary>
        /// Drops the added columns. The data rewrite is intentionally not reverted: the previous bindings pointed at
        /// retired or pay-per-token routes, and which row carried which binding is kept only in the pre-deploy backup.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var table in ProfileTables)
            {
                migrationBuilder.Sql($"""
ALTER TABLE "{table}" DROP COLUMN IF EXISTS "ModelSlot";
ALTER TABLE "{table}" DROP COLUMN IF EXISTS "ModelDomain";
""");
            }

            migrationBuilder.Sql("""
ALTER TABLE "CrewTemplates" DROP COLUMN IF EXISTS "Domain";
""");
        }
    }
}
