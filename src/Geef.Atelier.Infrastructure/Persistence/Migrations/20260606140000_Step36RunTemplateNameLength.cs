using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Widens <c>Runs.CrewTemplateName</c> from varchar(100) to varchar(200) so it matches
    /// <c>CrewTemplates.Name</c>. A run references a template by name; an auto-composed template
    /// name can be up to 200 chars, which previously overflowed the narrower run column and silently
    /// broke auto-crew chaining (the chained task-run insert threw 22001 inside a swallowed catch).
    /// </summary>
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260606140000_Step36RunTemplateNameLength")]
    public partial class Step36RunTemplateNameLength : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""Runs""
    ALTER COLUMN ""CrewTemplateName"" TYPE character varying(200);
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""Runs""
    ALTER COLUMN ""CrewTemplateName"" TYPE character varying(100);
");
        }
    }
}
