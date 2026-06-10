using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Geef.Atelier.Infrastructure.Persistence;

namespace Geef.Atelier.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AtelierDbContext))]
[Migration("20260610160000_Step40SystemToolsSeed")]
public partial class Step40SystemToolsSeed : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Seed 8 system ToolDefinitions (idempotent: ON CONFLICT DO NOTHING)
        migrationBuilder.Sql(@"
INSERT INTO tool_definitions
    (""Name"", ""DisplayName"", ""Description"", ""ToolType"", ""Settings"", ""LlmSchemaJson"", ""AccessClass"", ""IsSystem"", ""SecretRef"")
VALUES
    ('web-search',
     'Web Search (Tavily)',
     'Searches the web using the Tavily API. Retrieves current, real-time information from the internet.',
     'web-search',
     '{""apiKey"":"""",""maxResults"":""5""}',
     '{""type"":""object"",""properties"":{""query"":{""type"":""string"",""description"":""Search query""}},""required"":[""query""]}',
     0, true, 'TAVILY_API_KEY'),

    ('knowledge-base',
     'Knowledge Base (Vector Store)',
     'Searches the project''s vector knowledge base using semantic similarity.',
     'knowledge-base',
     '{""collectionName"":""default"",""TopK"":""5"",""Scope"":""global""}',
     '{""type"":""object"",""properties"":{""query"":{""type"":""string"",""description"":""Semantic search query""}},""required"":[""query""]}',
     0, true, NULL),

    ('url-fetch',
     'URL Fetch',
     'Fetches and extracts readable text content from a given URL. Respects SSRF safety checks.',
     'url-fetch',
     '{""maxContentPerUrl"":""8000""}',
     '{""type"":""object"",""properties"":{""url"":{""type"":""string"",""description"":""URL to fetch (must be public)""}},""required"":[""url""]}',
     0, true, NULL),

    ('news-search',
     'News Search',
     'Searches recent news articles using the Tavily news API.',
     'news-search',
     '{""maxResults"":""5"",""recencyDays"":""7""}',
     '{""type"":""object"",""properties"":{""query"":{""type"":""string"",""description"":""News search query""}},""required"":[""query""]}',
     0, true, 'TAVILY_API_KEY'),

    ('academic-search',
     'Academic Search',
     'Searches academic papers via Semantic Scholar, arXiv, or OpenAlex.',
     'academic-search',
     '{""academicSource"":""semantic-scholar"",""maxPapers"":""5""}',
     '{""type"":""object"",""properties"":{""query"":{""type"":""string"",""description"":""Academic search query""}},""required"":[""query""]}',
     0, true, NULL),

    ('rest-api',
     'REST API',
     'Calls an external REST API endpoint and extracts data using JSONPath.',
     'rest-api',
     '{""httpMethod"":""GET""}',
     '{""type"":""object"",""properties"":{""endpoint"":{""type"":""string"",""description"":""API endpoint URL""},""params"":{""type"":""object"",""additionalProperties"":{""type"":""string""},""description"":""Query parameters""}},""required"":[""endpoint""]}',
     0, true, NULL),

    ('static-context',
     'Static Context',
     'Provides pre-defined static context text to the pipeline.',
     'static-context',
     '{""staticContent"":""""}',
     '{""type"":""object"",""properties"":{},""required"":[]}',
     0, true, NULL),

    ('learning-retrieval',
     'Learning Retrieval',
     'Retrieves approved learnings from the project learning store using semantic similarity.',
     'learning-retrieval',
     '{""TopK"":""5""}',
     '{""type"":""object"",""properties"":{""query"":{""type"":""string"",""description"":""Learning retrieval query""}},""required"":[""query""]}',
     0, true, NULL)

ON CONFLICT (""Name"") DO NOTHING;
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"DELETE FROM tool_definitions WHERE ""IsSystem"" = true;");
    }
}
