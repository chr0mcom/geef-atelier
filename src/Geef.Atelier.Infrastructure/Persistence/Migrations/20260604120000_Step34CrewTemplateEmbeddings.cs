using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260604120000_Step34CrewTemplateEmbeddings")]
    public partial class Step34CrewTemplateEmbeddings : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
CREATE TABLE IF NOT EXISTS ""CrewTemplateEmbeddings"" (
    ""Id""           uuid         NOT NULL PRIMARY KEY,
    ""TemplateName"" text         NOT NULL,
    ""Domain""       text         NOT NULL DEFAULT 'general',
    ""Summary""      text         NOT NULL,
    ""Embedding""    vector(1536) NOT NULL,
    ""CreatedAt""    timestamptz  NOT NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CrewTemplateEmbeddings_TemplateName""
    ON ""CrewTemplateEmbeddings""(""TemplateName"");

CREATE INDEX IF NOT EXISTS ""IX_CrewTemplateEmbeddings_Domain""
    ON ""CrewTemplateEmbeddings""(""Domain"");

CREATE INDEX IF NOT EXISTS ""IX_CrewTemplateEmbeddings_Embedding_HNSW""
    ON ""CrewTemplateEmbeddings"" USING hnsw(""Embedding"" vector_cosine_ops);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""CrewTemplateEmbeddings"";");
        }
    }
}
