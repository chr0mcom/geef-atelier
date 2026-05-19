using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260524120000_Step24CustomProviders")]
    public partial class Step24CustomProviders : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""Providers"" (
    ""Name""        varchar(100) PRIMARY KEY,
    ""DisplayName"" text         NOT NULL,
    ""Description"" text         NOT NULL,
    ""Type""        integer      NOT NULL,
    ""Settings""    jsonb        NOT NULL,
    ""IsSystem""    boolean      NOT NULL DEFAULT false,
    ""IsActive""    boolean      NOT NULL DEFAULT true,
    ""CreatedAt""   timestamptz  NOT NULL DEFAULT now(),
    ""UpdatedAt""   timestamptz  NOT NULL DEFAULT now()
);

CREATE INDEX IF NOT EXISTS ""IX_Providers_Type""     ON ""Providers""(""Type"");
CREATE INDEX IF NOT EXISTS ""IX_Providers_IsSystem"" ON ""Providers""(""IsSystem"");
CREATE INDEX IF NOT EXISTS ""IX_Providers_IsActive"" ON ""Providers""(""IsActive"");

-- Seed 11 system providers (idempotent)
INSERT INTO ""Providers"" (""Name"",""DisplayName"",""Description"",""Type"",""Settings"",""IsSystem"",""IsActive"",""CreatedAt"",""UpdatedAt"")
VALUES
(
    'openrouter',
    'OpenRouter',
    'Pay-per-token aggregator with access to hundreds of models from many providers. Standard choice when you don''t have a direct subscription.',
    0,
    '{""endpoint"":""https://openrouter.ai/api/v1"",""api_key_env"":""LLM_OPENROUTER_API_KEY"",""auth_header_name"":""Authorization"",""auth_header_format"":""Bearer {key}"",""models_endpoint"":""/models"",""default_headers"":{""HTTP-Referer"":""https://geef.stefan-bechtel.de"",""X-Title"":""Geef.Atelier""}}',
    true, true, now(), now()
),
(
    'openai-direct',
    'OpenAI (Direct)',
    'Direct connection to OpenAI''s API. Use when you have an OpenAI API key and want no OpenRouter-markup. Recommended for production workloads with predictable cost.',
    0,
    '{""endpoint"":""https://api.openai.com/v1"",""api_key_env"":""OPENAI_API_KEY"",""auth_header_name"":""Authorization"",""auth_header_format"":""Bearer {key}"",""models_endpoint"":""/models""}',
    true, true, now(), now()
),
(
    'google-ai-studio',
    'Google AI Studio (Gemini)',
    'Direct connection to Google''s Gemini API via AI Studio. OpenAI-compatible endpoint.',
    0,
    '{""endpoint"":""https://generativelanguage.googleapis.com/v1beta/openai"",""api_key_env"":""GEMINI_API_KEY"",""auth_header_name"":""Authorization"",""auth_header_format"":""Bearer {key}"",""models_endpoint"":""/models""}',
    true, true, now(), now()
),
(
    'groq',
    'Groq',
    'Ultra-fast inference for open-source models (Llama, Mixtral). OpenAI-compatible. Free tier available with rate limits. Good for high-throughput reviewers.',
    0,
    '{""endpoint"":""https://api.groq.com/openai/v1"",""api_key_env"":""GROQ_API_KEY"",""auth_header_name"":""Authorization"",""auth_header_format"":""Bearer {key}"",""models_endpoint"":""/models""}',
    true, true, now(), now()
),
(
    'deepseek',
    'DeepSeek',
    'Affordable Chinese provider with strong coding and reasoning models. OpenAI-compatible. Often 10x cheaper than equivalent OpenAI models.',
    0,
    '{""endpoint"":""https://api.deepseek.com/v1"",""api_key_env"":""DEEPSEEK_API_KEY"",""auth_header_name"":""Authorization"",""auth_header_format"":""Bearer {key}"",""models_endpoint"":""/models""}',
    true, true, now(), now()
),
(
    'ollama-local',
    'Ollama (Local)',
    'Local Ollama instance for self-hosted models. No API key required. Set OLLAMA_ENDPOINT env-var to override default.',
    0,
    '{""endpoint"":""http://host.docker.internal:11434/v1"",""endpoint_env_override"":""OLLAMA_ENDPOINT"",""models_endpoint"":""/models""}',
    true, true, now(), now()
),
(
    'azure-openai',
    'Azure OpenAI',
    'Microsoft Azure OpenAI Service. Requires Azure subscription with OpenAI access. Clone as custom provider and configure resource_name and deployment_name.',
    0,
    '{""endpoint"":""https://{resource_name}.openai.azure.com/openai/deployments/{deployment_name}"",""api_key_env"":""AZURE_OPENAI_API_KEY"",""auth_header_name"":""api-key"",""auth_header_format"":""{key}""}',
    true, true, now(), now()
),
(
    'openai-compatible-generic',
    'OpenAI-Compatible (Generic)',
    'Template for any other OpenAI-compatible endpoint. Configure endpoint and API key. Use for Together AI, Fireworks, Mistral La Plateforme, local LiteLLM, or any OpenAI-compatible service.',
    0,
    '{""endpoint"":"""",""api_key_env"":"""",""auth_header_name"":""Authorization"",""auth_header_format"":""Bearer {key}"",""models_endpoint"":""/models""}',
    true, true, now(), now()
),
(
    'claude-cli',
    'Claude Code CLI',
    'Anthropic Claude via the official Claude Code CLI. Uses your Claude Pro/Max/Team subscription instead of paying per token.',
    1,
    '{""cli_kind"":""claude"",""binary"":""claude"",""auth_volume"":""/auth/claude"",""auth_command"":""claude auth login"",""max_concurrent"":2,""models"":[""anthropic/claude-opus-4.7"",""anthropic/claude-sonnet-4.6"",""anthropic/claude-haiku-4.5""]}',
    true, true, now(), now()
),
(
    'codex-cli',
    'Codex CLI',
    'OpenAI via the official Codex CLI. Uses your ChatGPT Plus/Pro/Team subscription instead of paying per API token.',
    1,
    '{""cli_kind"":""codex"",""binary"":""codex"",""auth_volume"":""/auth/codex"",""auth_command"":""codex auth login"",""max_concurrent"":2,""models"":[""openai/gpt-5.5"",""openai/gpt-5.5-mini"",""openai/gpt-4o"",""openai/o1"",""openai/o3-mini""]}',
    true, true, now(), now()
),
(
    'gemini-cli',
    'Gemini CLI',
    'Google Gemini via the official Gemini CLI. Free tier offers 60 requests/minute and 1000 requests/day with a Google account login.',
    1,
    '{""cli_kind"":""gemini"",""binary"":""gemini"",""auth_volume"":""/auth/gemini"",""auth_command"":""gemini"",""auth_env_var_alternative"":""GEMINI_API_KEY"",""max_concurrent"":2,""models"":[""google/gemini-2.5-pro"",""google/gemini-2.5-flash"",""google/gemini-3.1-pro"",""google/gemini-3.1-flash""]}',
    true, true, now(), now()
)
ON CONFLICT (""Name"") DO NOTHING;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""Providers"";");
        }
    }
}
