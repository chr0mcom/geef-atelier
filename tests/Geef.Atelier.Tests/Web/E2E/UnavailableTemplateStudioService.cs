using Geef.Atelier.Application.Crew.TemplateStudio;
using Geef.Atelier.Core.Domain.Crew.TemplateStudio;

namespace Geef.Atelier.Tests.Web.E2E;

/// <summary>
/// Template Studio stand-in of the web test host: registered so that the studio MCP tools bind it as a service (as in
/// production) instead of listing it as a client argument; every call fails because no web test needs the studio.
/// </summary>
internal sealed class UnavailableTemplateStudioService : ITemplateStudioService
{
    public Task<TemplateStudioAnalysis> AnalyzeAsync(string taskDescription, CancellationToken ct = default) => throw Unavailable();

    public Task<TemplateStudioAnalysis> AnalyzeAsync(string taskDescription, StudioModelChoice? overrideChoice, CancellationToken ct = default) =>
        throw Unavailable();

    public Task<TemplateStudioAnalysis> AnalyzeAsync(
        string taskDescription, StudioModelChoice? overrideChoice, IProgress<string>? progress, CancellationToken ct = default) =>
        throw Unavailable();

    public Task<StudioModelChoice> GetEffectiveDefaultAsync(CancellationToken ct = default) => throw Unavailable();

    public Task SaveDefaultAsync(StudioModelChoice choice, CancellationToken ct = default) => throw Unavailable();

    public Task<MaterializationResult> MaterializeAsync(Guid analysisId, MaterializationRequest request, CancellationToken ct = default) =>
        throw Unavailable();

    public Task<StudioAnalysesPage> ListRecentAnalysesAsync(int page, int pageSize, CancellationToken ct = default) => throw Unavailable();

    private static NotSupportedException Unavailable() => new("The Template Studio is not available in the web test host.");
}
