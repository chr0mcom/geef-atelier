using Geef.Atelier.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Geef.Atelier.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Data-only backfill for runs that finished on the best-effort non-convergence path before
    /// the <c>PostgresEventSink</c> honoured <c>PipelineCompletedEvent.Success</c>: the trailing
    /// completed-event overwrote the <c>Failed</c>/<c>Aborted</c> status set by the preceding
    /// <c>PipelineFailedEvent</c> with <c>Completed</c>. Such runs are uniquely identified by a
    /// non-null <c>ErrorMessage</c>, which only the failure paths ever write (finalizer errors go
    /// to the separate <c>FinalizerErrorMessage</c> column). The Aborted sweep must run first —
    /// its rows would otherwise be consumed by the broader Failed sweep.
    /// </summary>
    [DbContext(typeof(AtelierDbContext))]
    [Migration("20260717120000_Step44BestEffortRunStatusBackfill")]
    public partial class Step44BestEffortRunStatusBackfill : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
UPDATE ""Runs"" SET ""Status"" = 'Aborted'
WHERE ""Status"" = 'Completed' AND ""ErrorMessage"" IS NOT NULL AND ""ErrorMessage"" LIKE 'Aborted%';
");

            migrationBuilder.Sql(@"
UPDATE ""Runs"" SET ""Status"" = 'Failed'
WHERE ""Status"" = 'Completed' AND ""ErrorMessage"" IS NOT NULL;
");
        }

        /// <summary>
        /// Intentional no-op: after the sink fix, genuine Failed/Aborted runs with an
        /// <c>ErrorMessage</c> exist as well, so the pre-backfill state cannot be reconstructed
        /// losslessly.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
