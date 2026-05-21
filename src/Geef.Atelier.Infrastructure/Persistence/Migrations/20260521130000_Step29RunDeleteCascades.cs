using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260521130000_Step29RunDeleteCascades")]
    public partial class Step29RunDeleteCascades : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
-- Iterations had only an index on RunId; add FK with CASCADE so deleting a Run also removes its Iterations.
ALTER TABLE ""Iterations""
    ADD CONSTRAINT ""FK_Iterations_Runs_RunId""
    FOREIGN KEY (""RunId"") REFERENCES ""Runs""(""Id"") ON DELETE CASCADE;

-- Events had only an index on RunId.
ALTER TABLE ""Events""
    ADD CONSTRAINT ""FK_Events_Runs_RunId""
    FOREIGN KEY (""RunId"") REFERENCES ""Runs""(""Id"") ON DELETE CASCADE;

-- AdvisorConsultations had only an index on RunId.
ALTER TABLE ""AdvisorConsultations""
    ADD CONSTRAINT ""FK_AdvisorConsultations_Runs_RunId""
    FOREIGN KEY (""RunId"") REFERENCES ""Runs""(""Id"") ON DELETE CASCADE;

-- Findings had only an index on IterationId; cascade from Iterations covers the chain.
ALTER TABLE ""Findings""
    ADD CONSTRAINT ""FK_Findings_Iterations_IterationId""
    FOREIGN KEY (""IterationId"") REFERENCES ""Iterations""(""Id"") ON DELETE CASCADE;
");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
ALTER TABLE ""Findings"" DROP CONSTRAINT IF EXISTS ""FK_Findings_Iterations_IterationId"";
ALTER TABLE ""AdvisorConsultations"" DROP CONSTRAINT IF EXISTS ""FK_AdvisorConsultations_Runs_RunId"";
ALTER TABLE ""Events"" DROP CONSTRAINT IF EXISTS ""FK_Events_Runs_RunId"";
ALTER TABLE ""Iterations"" DROP CONSTRAINT IF EXISTS ""FK_Iterations_Runs_RunId"";
");
        }
    }
}
