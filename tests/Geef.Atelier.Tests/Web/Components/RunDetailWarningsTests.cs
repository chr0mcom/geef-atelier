using Bunit;
using Bunit.TestDoubles;
using Geef.Atelier.Application.Runs;
using Geef.Atelier.Core.Domain;
using Geef.Atelier.Core.Domain.Warnings;
using Geef.Atelier.Core.Persistence;
using Geef.Atelier.Tests.Fakes;
using Geef.Atelier.Web.Components.Pages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Geef.Atelier.Tests.Web.Components;

/// <summary>The run page shows a plain-language "Warnings" block only when the run recorded warnings.</summary>
public sealed class RunDetailWarningsTests : TestContext
{
    private readonly Guid _runId = Guid.NewGuid();

    [Fact]
    public void ShowsTheWarningsBlockWithEveryMessage()
    {
        Register(
        [
            Warning(RunWarningCodes.GroundingProviderSkipped, "A source could not be read and was skipped: Tavily Web Search (reason: timeout)."),
            Warning(RunWarningCodes.LlmFailover, "An AI service was unavailable, so another one answered instead."),
        ]);

        var cut = RenderComponent<RunDetail>(p => p.Add(c => c.RunId, _runId));

        var block = cut.Find("[data-testid='run-warnings']");
        Assert.Equal("Warnings", block.QuerySelector("h2")!.TextContent);
        var items = cut.FindAll("[data-testid='run-warning']");
        Assert.Equal(2, items.Count);
        Assert.Contains("A source could not be read and was skipped", items[0].TextContent);
        Assert.Equal(RunWarningCodes.LlmFailover, items[1].GetAttribute("data-code"));
    }

    [Fact]
    public void ShowsNoWarningsBlockWithoutWarnings()
    {
        Register([]);

        var cut = RenderComponent<RunDetail>(p => p.Add(c => c.RunId, _runId));

        Assert.Empty(cut.FindAll("[data-testid='run-warnings']"));
    }

    private void Register(IReadOnlyList<RunWarningEntity> warnings)
    {
        var details = new RunDetails(
            new RunEntity
            {
                Id = _runId,
                CreatedAt = DateTimeOffset.UtcNow,
                Status = RunStatus.Completed,
                BriefingText = "Test briefing",
                ConfigJson = "{}",
                FinalText = "# Final",
            },
            []);

        Services.AddSingleton<IRunService>(new StubRunService(details, warnings));
        Services.AddScoped<Geef.Atelier.Web.Components.CircuitDataGate>();
        Services.AddSingleton<Geef.Atelier.Web.Notifications.IRunUpdateFeed>(sp => new Geef.Atelier.Web.Notifications.InProcessRunUpdateFeed(
            sp.GetRequiredService<IServiceScopeFactory>(),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<Geef.Atelier.Web.Notifications.InProcessRunUpdateFeed>.Instance));
        Services.AddSingleton(typeof(ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        this.AddTestAuthorization().SetAuthorized("user");
    }

    private RunWarningEntity Warning(string code, string message) => new()
    {
        Id = Guid.NewGuid(),
        RunId = _runId,
        Source = "test",
        Code = code,
        Message = message,
        CreatedAt = DateTimeOffset.UtcNow,
    };
}
