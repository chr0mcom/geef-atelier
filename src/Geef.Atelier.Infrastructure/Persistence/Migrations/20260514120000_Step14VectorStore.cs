using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260514120000_Step14VectorStore")]
    public partial class Step14VectorStore : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS vector;");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""KnowledgeDocuments"" (
                    ""Id""                  uuid           PRIMARY KEY DEFAULT gen_random_uuid(),
                    ""Title""               text           NOT NULL,
                    ""Description""         text           NOT NULL DEFAULT '',
                    ""OriginalFilename""    text           NOT NULL,
                    ""ContentType""         text           NOT NULL,
                    ""FileSizeBytes""       bigint         NOT NULL,
                    ""RawContent""          text           NOT NULL,
                    ""Tags""               text[]         NOT NULL DEFAULT '{}',
                    ""EmbeddingModel""      text           NOT NULL,
                    ""EmbeddingDimensions"" integer        NOT NULL,
                    ""ChunkCount""          integer        NOT NULL DEFAULT 0,
                    ""IndexingCostEur""     numeric(10,4)  NULL,
                    ""CreatedAt""           timestamptz    NOT NULL DEFAULT now(),
                    ""UpdatedAt""           timestamptz    NOT NULL DEFAULT now()
                );
            ");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS ""KnowledgeDocumentChunks"" (
                    ""Id""          uuid         PRIMARY KEY DEFAULT gen_random_uuid(),
                    ""DocumentId""  uuid         NOT NULL REFERENCES ""KnowledgeDocuments""(""Id"") ON DELETE CASCADE,
                    ""ChunkIndex""  integer      NOT NULL,
                    ""Content""     text         NOT NULL,
                    ""Embedding""   vector(1536) NOT NULL,
                    ""TokenCount""  integer      NOT NULL,
                    ""CreatedAt""   timestamptz  NOT NULL DEFAULT now()
                );

                CREATE INDEX IF NOT EXISTS ""IX_KnowledgeDocumentChunks_DocumentId""
                    ON ""KnowledgeDocumentChunks""(""DocumentId"");

                CREATE INDEX IF NOT EXISTS ""IX_KnowledgeDocumentChunks_Embedding_HNSW""
                    ON ""KnowledgeDocumentChunks""
                    USING hnsw (""Embedding"" vector_cosine_ops);

                CREATE INDEX IF NOT EXISTS ""IX_KnowledgeDocuments_Tags""
                    ON ""KnowledgeDocuments"" USING gin(""Tags"");
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""KnowledgeDocumentChunks"";");
            migrationBuilder.Sql(@"DROP TABLE IF EXISTS ""KnowledgeDocuments"";");
            migrationBuilder.Sql("DROP EXTENSION IF EXISTS vector;");
        }
    }
}
