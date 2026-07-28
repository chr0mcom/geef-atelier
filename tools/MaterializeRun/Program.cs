using Geef.Atelier.Application.Composition;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Infrastructure.Composition;
using Geef.Atelier.Infrastructure.Embeddings;
using Geef.Atelier.Infrastructure.Grounding;
using Geef.Atelier.Infrastructure.Knowledge;
using Geef.Atelier.Infrastructure.Llm;
using Geef.Atelier.Infrastructure.Persistence;
using Geef.Atelier.Infrastructure.TemplateStudio;
using Geef.Atelier.Infrastructure.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// One-off operations tool: materializes the Crew-Spec JSON of an already-terminal
// composition run through the real ICrewMaterializer (validation, dedup, template
// creation). Use when a composition run finished without its CrewMaterialize
// finalizer executing (e.g. the pre-2026-07-28 resume bug that dropped RunKind).
//
// Usage:
//   ConnectionStrings__DefaultConnection="Host=...;..." \
//   Llm__Providers__openrouter__ApiKey=... \
//   dotnet run --project tools/MaterializeRun -- <runId> [--fix-kind]

if (args.Length is < 1 or > 2 || !Guid.TryParse(args[0], out var runId))
{
    Console.Error.WriteLine("Usage: MaterializeRun <runId> [--fix-kind]");
    return 2;
}
var fixKind = args.Contains("--fix-kind");

var builder = Host.CreateApplicationBuilder();
builder.Configuration
    .AddJsonFile(Path.Combine(AppContext.BaseDirectory, "../../../../../src/Geef.Atelier.Web/appsettings.json"), optional: true)
    .AddEnvironmentVariables();

var services = builder.Services;
services.AddDbContext<AtelierDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
           .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning)));
services.AddLlmClient(builder.Configuration);
services.AddAtelierPersistence();
services.AddAtelierApplication();
services.AddEmbeddings(builder.Configuration);
services.AddKnowledge(builder.Configuration);
services.AddCrewComposition();
services.AddGroundingProviders(builder.Configuration);
services.AddToolExecutor();
services.AddTemplateStudio(builder.Configuration);

using var host = builder.Build();
await using var scope = host.Services.CreateAsyncScope();

var runRepo      = scope.ServiceProvider.GetRequiredService<IRunRepository>();
var materializer = scope.ServiceProvider.GetRequiredService<ICrewMaterializer>();

var run = await runRepo.GetByIdAsync(runId);
if (run is null)
{
    Console.Error.WriteLine($"Run {runId} not found.");
    return 1;
}

Console.WriteLine($"Run {runId}: status={run.Status}, kind={run.Kind}, template={run.CrewTemplateName}, user={run.CreatedByUser}");

if (string.IsNullOrWhiteSpace(run.FinalText))
{
    Console.Error.WriteLine("Run has no FinalText (crew spec); nothing to materialize.");
    return 1;
}

var result = await materializer.MaterializeAsync(run.FinalText, runId);
Console.WriteLine($"Materialized: template='{result.TemplateName}', wasDuplicate={result.WasDuplicate}");
foreach (var warning in result.Warnings)
    Console.WriteLine($"  warning: {warning}");

if (fixKind && run.Kind != RunKind.CrewComposition)
{
    var db = scope.ServiceProvider.GetRequiredService<AtelierDbContext>();
    await db.Runs.Where(r => r.Id == runId)
        .ExecuteUpdateAsync(s => s.SetProperty(r => r.Kind, RunKind.CrewComposition));
    Console.WriteLine("Kind corrected to CrewComposition.");
}

return 0;
